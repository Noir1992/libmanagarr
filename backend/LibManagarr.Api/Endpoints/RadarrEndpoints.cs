using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Endpoints;

public static class RadarrEndpoints
{
    public static IEndpointRouteBuilder MapRadarrEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/radarr/instances");
        group.RequireAuthorization();

        group.MapGet("/{instanceId}/library", GetLibraryAsync);
        group.MapPost("/library", PostLibraryAsync);
        group.MapPost("/library/filter-values", PostFilterValuesAsync);
        group.MapGet("/{instanceId}/poster/{movieId:int}", GetPosterAsync);

        return endpoints;
    }

    private static async Task<Results<Ok<RadarrLibraryFilterValuesResponse>, NotFound<ProblemDetails>>>
        PostFilterValuesAsync(
            FilterValuesRequest request,
            ISettingsService settingsService = null!)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken: CancellationToken.None);

        IEnumerable<ArrInstanceSettings> instances = settings.RadarrInstances
            .Where(i => request.InstanceIds.Contains("__all__") || request.InstanceIds.Contains(i.Id));

        if (!instances.Any())
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Radarr instance not found",
                Detail = "No Radarr instances found for the requested instance IDs.",
                Status = StatusCodes.Status404NotFound
            });
        }

        Dictionary<string, Dictionary<string, int>> allCounts = [];
        HashSet<string> allResolutions = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allVideoCodecs = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allAudioCodecs = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allDynamicRanges = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allContainerFormats = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allGenres = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allTags = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allStatuses = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allProfiles = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allAudioLangs = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allSubtitleLangs = new(StringComparer.OrdinalIgnoreCase);

        foreach (string instanceId in instances.Select(instance => instance.Id))
        {
            LibraryFilterValues? fv = settings.LibraryFilterValues.FirstOrDefault(f => f.InstanceId == instanceId);
            if ((fv is null || fv.Version < LibraryFilterValues.CurrentVersion) && settings.RadarrSnapshots.FirstOrDefault(snapshot => snapshot.InstanceId == instanceId) is { } snapshot)
            {
                fv = LibraryFilterValuesBuilder.Build(instanceId, snapshot);
            }
            if (fv is not null)
            {
                LibraryFilterCountBuilder.MergeInto(allCounts, fv.Counts);
                allResolutions.UnionWith(fv.Resolutions);
                allVideoCodecs.UnionWith(fv.VideoCodecs);
                allAudioCodecs.UnionWith(fv.AudioCodecs);
                allDynamicRanges.UnionWith(fv.DynamicRanges);
                allContainerFormats.UnionWith(fv.ContainerFormats);
                foreach (string g in fv.Genres) allGenres.Add(g);
                foreach (string t in fv.Tags) allTags.Add(t);
                foreach (string s in fv.Statuses) allStatuses.Add(s);
                foreach (string p in fv.QualityProfiles) allProfiles.Add(p);
                foreach (string a in fv.AudioLanguages) allAudioLangs.Add(a);
                foreach (string sl in fv.SubtitleLanguages) allSubtitleLangs.Add(sl);
            }
        }

        Dictionary<string, int> instanceCounts = new(StringComparer.OrdinalIgnoreCase);
        foreach (string instanceId in settings.RadarrInstances.Select(instance => instance.Id))
        {
            RadarrInstanceSnapshot? instanceSnapshot = settings.RadarrSnapshots.FirstOrDefault(snapshot => snapshot.InstanceId == instanceId);
            if (instanceSnapshot is not null)
                instanceCounts[instanceId] = instanceSnapshot.Movies.Count;
        }
        instanceCounts[LibraryFilterCountBuilder.All] = instanceCounts.Values.Sum();
        allCounts["radarr-instance-filter"] = instanceCounts;

        return TypedResults.Ok(new RadarrLibraryFilterValuesResponse
        {
            Counts = allCounts,
            Resolutions = allResolutions.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            VideoCodecs = allVideoCodecs.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            AudioCodecs = allAudioCodecs.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            DynamicRanges = allDynamicRanges.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            ContainerFormats = allContainerFormats.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            Genres = allGenres.ToList(),
            Tags = allTags.ToList(),
            Statuses = allStatuses.ToList(),
            QualityProfiles = allProfiles.ToList(),
            AudioLanguages = allAudioLangs.ToList(),
            SubtitleLanguages = allSubtitleLangs.ToList(),
        });
    }

    private sealed class FilterValuesRequest
    {
        public IReadOnlyList<string> InstanceIds { get; set; } = [];
    }

    private static async Task<Results<Ok<RadarrLibraryBatchResponse>, NotFound<ProblemDetails>>> PostLibraryAsync(
        LibraryRequestFilter filter,
        ISettingsService settingsService = null!,
        CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);

        LibraryFilterMatcher matcher = new(filter);

        IEnumerable<ArrInstanceSettings> instances = settings.RadarrInstances
            .Where(i => matcher.MatchesInstance(i.Id, false));

        if (!instances.Any())
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Radarr instance not found",
                Detail = "No Radarr instances found for the requested instance IDs.",
                Status = StatusCodes.Status404NotFound
            });
        }

        DateTimeOffset? syncedAt = null;
        List<RadarrLibraryInstanceResult> instanceResults = [];

        foreach (ArrInstanceSettings instance in instances)
        {
            RadarrInstanceSnapshot? snapshot =
                settings.RadarrSnapshots.FirstOrDefault(currentSnapshot => currentSnapshot.InstanceId == instance.Id);

            if (snapshot is not null && syncedAt is null)
                syncedAt = snapshot.SyncedAt;

            IReadOnlyList<RadarrLibraryEntryResponse> rows = snapshot is null
                ? []
                : LibrarySorter.SortRadarr(snapshot.Movies
                    .Where(matcher.MatchesMovie)
                    .Select((m) => new RadarrLibraryEntryResponse
                    {
                        InstanceName = snapshot.InstanceName,
                        MovieId = m.MovieId,
                        MovieTitle = m.Title,
                        MovieTitleSlug = m.TitleSlug,
                        QualityProfileName = m.Profile.Name,
                        UpgradeUntilCustomFormatScore = m.Profile.UpgradeUntilCustomFormatScore,
                        Status = m.Status,
                        AddedAt = m.AddedAt,
                        Genres = m.Genres,
                        Tags = m.Tags,
                        HasAired = m.HasAired,
                        MediaDetails = LibraryMediaDetailsResponse.From(m.File, m.HasFile),
                        AirDateUtc = m.AirDateUtc,
                        Monitored = m.Monitored,
                        HasFile = m.HasFile,
                        CustomFormatScore = m.HasFile ? m.File.CustomFormatScore : null,
                        Resolution = m.File.Resolution,
                        Size = m.File.Size,
                    }), filter, settings.TitleSorting);

            instanceResults.Add(new RadarrLibraryInstanceResult
            {
                InstanceId = instance.Id,
                InstanceName = instance.Name,
                Rows = rows
            });
        }

        return TypedResults.Ok(new RadarrLibraryBatchResponse
        {
            SyncedAt = syncedAt,
            Counts = LibraryFilterCountBuilder.BuildRadarrResults(instanceResults, settings.RadarrInstances.Select(instance => instance.Id)),
            Instances = instanceResults
        });
    }

    private static async Task<Results<Ok<RadarrLibraryResponse>, NotFound<ProblemDetails>>> GetLibraryAsync(
        string instanceId,
        string? airedFilter = null,
        string? monitoredFilter = null,
        string? statuses = null,
        string? genres = null,
        string? tags = null,
        string? profiles = null,
        string? audioLanguages = null,
        string? subtitleLanguages = null,
        int? scoreMin = null,
        int? scoreMax = null,
        string? airedFrom = null,
        string? airedTo = null,
        ISettingsService settingsService = null!,
        CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        ArrInstanceSettings? instance =
            settings.RadarrInstances.FirstOrDefault((currentInstance) => currentInstance.Id == instanceId);

        if (instance is null)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Radarr instance not found",
                Detail = $"No Radarr instance exists for id '{instanceId}'.",
                Status = StatusCodes.Status404NotFound
            });
        }

        RadarrInstanceSnapshot? snapshot =
            settings.RadarrSnapshots.FirstOrDefault((currentSnapshot) => currentSnapshot.InstanceId == instanceId);

        IReadOnlyList<string>? statusList = ParseCsvFilter(statuses);
        IReadOnlyList<string>? genreList = ParseCsvFilter(genres);
        IReadOnlyList<string>? tagList = ParseCsvFilter(tags);
        IReadOnlyList<string>? profileList = ParseCsvFilter(profiles);
        IReadOnlyList<string>? audioLangList = ParseCsvFilter(audioLanguages);
        IReadOnlyList<string>? subtitleLangList = ParseCsvFilter(subtitleLanguages);

        DateTimeOffset? airDateFrom = null;
        DateTimeOffset? airDateTo = null;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (!string.IsNullOrWhiteSpace(airedFrom) && DateTimeOffset.TryParse(airedFrom, culture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset parsedFrom))
            airDateFrom = new DateTimeOffset(parsedFrom.Date, parsedFrom.Offset);
        if (!string.IsNullOrWhiteSpace(airedTo) && DateTimeOffset.TryParse(airedTo, culture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset parsedTo))
            airDateTo = new DateTimeOffset(parsedTo.Date.Add(TimeSpan.FromDays(1)).AddTicks(-1), parsedTo.Offset);

        IReadOnlyList<RadarrLibraryEntryResponse> movies = snapshot is null
            ? []
            : LibrarySorter.SortRadarr(snapshot.Movies
                .Where((m) =>
                    MatchAired(m.HasAired, airedFilter)
                    && MatchMonitored(m.Monitored, monitoredFilter)
                    && (statusList == null || statusList.Contains(m.Status, StringComparer.OrdinalIgnoreCase))
                    && (genreList == null || m.Genres.Any(g => genreList.Contains(g, StringComparer.OrdinalIgnoreCase)))
                    && (tagList == null || m.Tags.Any(t => tagList.Contains(t, StringComparer.OrdinalIgnoreCase)))
                    && (profileList == null || profileList.Contains(m.Profile.Name, StringComparer.OrdinalIgnoreCase))
                    && (audioLangList == null || m.File.AudioLanguages.Any(al =>
                        audioLangList.Contains(al, StringComparer.OrdinalIgnoreCase)))
                    && (subtitleLangList == null || m.File.SubtitleLanguages.Any(sl =>
                        subtitleLangList.Contains(sl, StringComparer.OrdinalIgnoreCase)))
                    && (!(scoreMin.HasValue || scoreMax.HasValue) || m.HasFile)
                    && (!scoreMin.HasValue || m.File.CustomFormatScore >= scoreMin.Value)
                    && (!scoreMax.HasValue || m.File.CustomFormatScore <= scoreMax.Value)
                    && (!airDateFrom.HasValue || (m.AirDateUtc.HasValue && m.AirDateUtc.Value >= airDateFrom.Value))
                    && (!airDateTo.HasValue || (m.AirDateUtc.HasValue && m.AirDateUtc.Value <= airDateTo.Value)))
                .Select((m) => new RadarrLibraryEntryResponse
                {
                    InstanceName = snapshot.InstanceName,
                    MovieId = m.MovieId,
                    MovieTitle = m.Title,
                    MovieTitleSlug = m.TitleSlug,
                    QualityProfileName = m.Profile.Name,
                    UpgradeUntilCustomFormatScore = m.Profile.UpgradeUntilCustomFormatScore,
                    Status = m.Status,
                    AddedAt = m.AddedAt,
                    Genres = m.Genres,
                    Tags = m.Tags,
                    HasAired = m.HasAired,
                    MediaDetails = LibraryMediaDetailsResponse.From(m.File, m.HasFile),
                    AirDateUtc = m.AirDateUtc,
                    Monitored = m.Monitored,
                    HasFile = m.HasFile,
                    CustomFormatScore = m.HasFile ? m.File.CustomFormatScore : null,
                    Resolution = m.File.Resolution,
                    Size = m.File.Size,
                }), new LibraryRequestFilter(), settings.TitleSorting);

        return TypedResults.Ok(new RadarrLibraryResponse
        {
            SyncedAt = snapshot?.SyncedAt,
            Rows = movies
        });
    }

    private static IReadOnlyList<string>? ParseCsvFilter(string? value)
    {
        if (value == null) return null;
        if (string.IsNullOrWhiteSpace(value)) return [];
        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool MatchAired(bool hasAired, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        return filter switch
        {
            "aired" => hasAired,
            "notAired" => !hasAired,
            _ => true
        };
    }

    private static bool MatchMonitored(bool monitored, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        return filter switch
        {
            "monitored" => monitored,
            "notMonitored" => !monitored,
            _ => true
        };
    }

    private static async Task<IResult> GetPosterAsync(
        string instanceId,
        int movieId,
        [FromQuery] int width = 342,
        HttpResponse httpResponse = null!,
        ISettingsService settingsService = null!,
        IPosterCacheService posterCache = null!,
        CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        ArrInstanceSettings? instance =
            settings.RadarrInstances.FirstOrDefault((currentInstance) => currentInstance.Id == instanceId);

        if (instance is null)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Radarr instance not found",
                Detail = $"No Radarr instance exists for id '{instanceId}'.",
                Status = StatusCodes.Status404NotFound
            });
        }

        RadarrInstanceSnapshot? snapshot =
            settings.RadarrSnapshots.FirstOrDefault((currentSnapshot) => currentSnapshot.InstanceId == instanceId);

        string? remoteUrl = null;
        if (snapshot is not null && snapshot.PosterUrls.TryGetValue(movieId, out string? url))
        {
            remoteUrl = url;
        }

        PosterImage? poster = await posterCache.GetOrDownloadPosterAsync(
            $"radarr_{movieId}", remoteUrl, width, cancellationToken);

        if (poster is null)
        {
            httpResponse.StatusCode = 404;
            httpResponse.ContentType = "application/json";
            await httpResponse.WriteAsync("{\"error\":\"Poster not found\"}", cancellationToken);
            return Results.Empty;
        }

        httpResponse.ContentType = poster.ContentType;
        httpResponse.Headers.CacheControl = "no-store";
        await httpResponse.Body.WriteAsync(poster.Bytes, 0, poster.Bytes.Length, cancellationToken);
        return Results.Empty;
    }
}
