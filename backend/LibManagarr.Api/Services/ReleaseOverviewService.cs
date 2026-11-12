using System.Reflection;
using System.Text.RegularExpressions;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

// Combines the published GitHub releases with the version this instance was built from. The
// comparison is deliberately numeric over major.minor.patch: the navigation hint must stay off
// for edge builds, prereleases and development runs that carry no release number at all.
public sealed partial class ReleaseOverviewService(
    GitHubReleasesClient releasesClient,
    ReleaseOverviewCache cache,
    TimeProvider timeProvider,
    ILogger<ReleaseOverviewService> logger)
{
    // Builds without the LIBMANAGARR_VERSION build argument report the SDK default, which is
    // unrelated to any published release and must never raise the update hint.
    public const string DevelopmentVersion = "1.0.0";

    // The assembly value is the only one for a running instance; tests initialise the property
    // with the version they need to exercise the comparison rules.
    public string CurrentVersion { get; init; } = ReadBuildVersion();

    // Page reads never leave the process: they serve the stored snapshot, empty until the first
    // scheduled check has landed. Only the releaseCheck task asks GitHub, through RefreshAsync.
    public Task<ReleaseOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        cache.TryGet(out IReadOnlyList<ReleaseVersion> versions, out ReleaseOverviewStatus status, out DateTimeOffset checkedAt);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(MapOverview(versions, status, checkedAt));
    }

    // The task scheduler already runs one releaseCheck at a time, so the fetch needs no own gate.
    public Task<ReleaseOverview> RefreshAsync(CancellationToken cancellationToken) => FetchOverviewAsync(cancellationToken);

    // The running instance only ever learns its own version from the assembly, because the
    // release image is the single artifact that ships without a source tree to read it from.
    private static string ReadBuildVersion()
    {
        string? informationalVersion = typeof(ReleaseOverviewService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return DevelopmentVersion;
        }

        // The SDK appends the source revision after a plus sign when building from a working copy.
        int revisionSeparator = informationalVersion.IndexOf('+');

        return revisionSeparator < 0 ? informationalVersion : informationalVersion[..revisionSeparator];
    }

    private async Task<ReleaseOverview> FetchOverviewAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<GitHubReleaseResource> resources;
        try
        {
            resources = await releasesClient.FetchReleasesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning(exception, "The published releases could not be fetched from GitHub.");
            ReleaseOverviewStatus failureStatus = StatusFromFailure(exception);
            (IReadOnlyList<ReleaseVersion> keptVersions, DateTimeOffset keptCheckedAt) = cache.StoreFailure(failureStatus, timeProvider.GetUtcNow());

            return MapOverview(keptVersions, failureStatus, keptCheckedAt);
        }

        IReadOnlyList<ReleaseVersion> versions = MapVersions(resources);
        DateTimeOffset checkedAt = timeProvider.GetUtcNow();
        cache.Store(versions, ReleaseOverviewStatus.Ok, checkedAt);

        return MapOverview(versions, ReleaseOverviewStatus.Ok, checkedAt);
    }

    private static ReleaseOverviewStatus StatusFromFailure(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return ReleaseOverviewStatus.Unreachable;
        }

        return (exception as HttpRequestException)?.StatusCode switch
        {
            System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests => ReleaseOverviewStatus.RateLimited,
            null => ReleaseOverviewStatus.Unreachable,
            _ => ReleaseOverviewStatus.Rejected
        };
    }

    private ReleaseOverview MapOverview(IReadOnlyList<ReleaseVersion> versions, ReleaseOverviewStatus status, DateTimeOffset checkedAt)
    {
        return new ReleaseOverview
        {
            CurrentVersion = CurrentVersion,
            UpdateAvailable = versions.Any(version => version.IsNewerThanCurrent),
            Status = status,
            CheckedAt = checkedAt,
            Versions = versions
        };
    }

    private IReadOnlyList<ReleaseVersion> MapVersions(IReadOnlyList<GitHubReleaseResource> resources)
    {
        int[]? currentParts = ParseCurrentVersionParts();

        return [.. resources
            .Where(resource => !resource.Draft && !string.IsNullOrWhiteSpace(resource.TagName))
            .Select(resource => MapVersion(resource, currentParts))
            .OrderByDescending(version => version.PublishedAt)
            .ThenByDescending(version => version.Version, Comparer<string>.Create(CompareVersions))];
    }

    private ReleaseVersion MapVersion(GitHubReleaseResource resource, int[]? currentParts)
    {
        string tag = resource.TagName!.Trim();
        string version = tag.TrimStart('v', 'V');
        int[]? releaseParts = ParseVersionParts(version);
        // Positive means the listed release is ahead of the running instance.
        int comparisonToCurrent = CompareParts(releaseParts, currentParts);

        return new ReleaseVersion
        {
            Version = version,
            Tag = tag,
            PublishedAt = resource.PublishedAt ?? default,
            Notes = string.IsNullOrWhiteSpace(resource.Body) ? null : resource.Body,
            Url = resource.HtmlUrl ?? string.Empty,
            IsPrerelease = resource.Prerelease,
            IsCurrent = comparisonToCurrent == 0 && releaseParts is not null && currentParts is not null,
            // A prerelease competes with nothing: the hint is reserved for stable releases.
            IsNewerThanCurrent = !resource.Prerelease && comparisonToCurrent > 0
        };
    }

    private static int CompareVersions(string left, string right)
    {
        return CompareParts(ParseVersionParts(left), ParseVersionParts(right));
    }

    private static int CompareParts(int[]? left, int[]? right)
    {
        // A version without a plain release number cannot be placed on the scale, so it never
        // counts as newer and never matches the running instance.
        if (left is null || right is null)
        {
            return 0;
        }

        for (int index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            int leftPart = index < left.Length ? left[index] : 0;
            int rightPart = index < right.Length ? right[index] : 0;
            if (leftPart != rightPart)
            {
                return leftPart.CompareTo(rightPart);
            }
        }

        return 0;
    }

    [GeneratedRegex(@"^v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseTagPattern();

    // The development version is the SDK default rather than a published release, so it is kept
    // off the scale on purpose: comparing it would make every real release look newer.
    private int[]? ParseCurrentVersionParts()
    {
        if (string.Equals(CurrentVersion.Trim(), DevelopmentVersion, StringComparison.Ordinal))
        {
            return null;
        }

        return ParseVersionParts(CurrentVersion);
    }

    private static int[]? ParseVersionParts(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Match match = ReleaseTagPattern().Match(value.Trim());
        if (!match.Success)
        {
            return null;
        }

        return
        [
            int.Parse(match.Groups["major"].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(match.Groups["minor"].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(match.Groups["patch"].Value, System.Globalization.CultureInfo.InvariantCulture)
        ];
    }
}
