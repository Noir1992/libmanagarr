using System.Collections.Concurrent;
using System.Text.Json;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>Checks unregistered episode files using current Arr assignments and read-only filesystem operations.</summary>
public sealed class ExistingEpisodeFileInspector(
    SonarrClient sonarr, RadarrClient radarr, MediaPathMappingService paths, ISymlinkFileSystem files, TimeProvider clock)
{
    private const int MaxLinkDepth = 40;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly ConcurrentDictionary<(string, int), Lazy<Task<SonarrEpisodeResource?>>> episodes = new();
    private readonly ConcurrentDictionary<(string, int), Lazy<Task<SonarrEpisodeFileResource?>>> episodeFiles = new();
    private readonly ConcurrentDictionary<(string, int), Lazy<Task<IReadOnlyList<RadarrMovieFileResource>>>> movieFiles = new();

    public async Task<ExistingEpisodeFileCheck?> CheckAsync(AppSettings settings, SymlinkSuggestion suggestion, CancellationToken token)
    {
        if (!settings.Symlinks.Enabled) return null;
        token.ThrowIfCancellationRequested();
        if (suggestion.Status is SymlinkSuggestionStatus.Completed or SymlinkSuggestionStatus.Creating or SymlinkSuggestionStatus.Rejected)
            return null;
        SpecialMoviePair pair = suggestion.Pair;
        bool hadFile = settings.SonarrSnapshots.Where(instance => instance.InstanceId == pair.SonarrInstanceId)
            .SelectMany(instance => instance.Series).Where(series => series.SeriesId == pair.SonarrSeriesId)
            .SelectMany(series => series.Seasons).SelectMany(season => season.Episodes)
            .Any(episode => episode.EpisodeId == pair.SonarrEpisodeId && episode.HasFile);
        if (!hadFile && !suggestion.Reasons.Any(reason => reason.Code == SpecialMovieMatchingRules.EpisodeHasFileCode)) return null;

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        CancellationToken readToken = timeout.Token;
        ExistingEpisodeFileCheck result = new() { CheckedAt = clock.GetUtcNow() };
        try
        {
            ArrInstanceSettings? sonarrInstance = settings.SonarrInstances.FirstOrDefault(instance => instance.Id == pair.SonarrInstanceId);
            ArrInstanceSettings? radarrInstance = settings.RadarrInstances.FirstOrDefault(instance => instance.Id == pair.RadarrInstanceId);
            if (sonarrInstance is null || radarrInstance is null)
                return Finish(result, ExistingEpisodeFileStatus.Unavailable, "One of the Arr instances is no longer configured.");

            SonarrEpisodeResource? episode = await ReadOnce(episodes, (sonarrInstance.Id, pair.SonarrEpisodeId),
                () => sonarr.FetchEpisodeAsync(sonarrInstance, pair.SonarrEpisodeId, readToken));
            if (episode?.Id != pair.SonarrEpisodeId || (episode.SeriesId.HasValue && episode.SeriesId != pair.SonarrSeriesId))
                return Finish(result, ExistingEpisodeFileStatus.Unavailable, "Sonarr did not return the expected episode.");
            if (episode.EpisodeFileId is null)
                return Finish(result, episode.HasFile == false ? ExistingEpisodeFileStatus.NoFile : ExistingEpisodeFileStatus.Unavailable,
                    "Sonarr currently reports no episode file assignment that can be inspected.");

            SonarrEpisodeFileResource? file = await ReadOnce(episodeFiles, (sonarrInstance.Id, episode.EpisodeFileId.Value),
                () => sonarr.FetchEpisodeFileAsync(sonarrInstance, episode.EpisodeFileId.Value, readToken));
            if (file?.Id != episode.EpisodeFileId || string.IsNullOrWhiteSpace(file.Path))
                return Finish(result, ExistingEpisodeFileStatus.Unavailable, "Sonarr did not return the assigned episode file and its path.");
            result.SonarrFileId = file.Id;
            PathMappingResult episodePath = await paths.TranslateToLibManagarrPathAsync(sonarrInstance.Id, ArrApplicationKind.Sonarr, file.Path, readToken);
            if (!episodePath.Success) return Finish(result, ExistingEpisodeFileStatus.Unavailable, episodePath.Failure!);
            result.EpisodePath = episodePath.Path;
            PathMappingResult episodeBoundary = await paths.EnsureWithinStorageAreaAsync(ArrApplicationKind.Sonarr, result.EpisodePath!, readToken);
            if (!episodeBoundary.Success) return Finish(result, ExistingEpisodeFileStatus.Unavailable, episodeBoundary.Failure!);
            if (!files.EntryExists(result.EpisodePath!))
                return Finish(result, ExistingEpisodeFileStatus.MissingFile, $"The episode entry '{result.EpisodePath}' reported by Sonarr is missing.");
            if (!files.IsSymlink(result.EpisodePath!))
                return Finish(result, ExistingEpisodeFileStatus.RegularFile, $"'{result.EpisodePath}' is an existing file, not a symbolic link. It was left unchanged.");

            IReadOnlyList<RadarrMovieFileResource> sources = await ReadOnce(movieFiles, (radarrInstance.Id, pair.RadarrMovieId),
                () => radarr.FetchMovieFilesAsync(radarrInstance, pair.RadarrMovieId, readToken));
            RadarrMovieFileResource? source = sources.FirstOrDefault(candidate => candidate.Id == suggestion.RadarrFileId
                && candidate.MovieId == pair.RadarrMovieId);
            if (source is null || string.IsNullOrWhiteSpace(source.Path))
                return Finish(result, ExistingEpisodeFileStatus.Unavailable, "Radarr no longer reports the proposed movie file. Refresh the library data and suggestions.");
            PathMappingResult sourcePath = await paths.TranslateToLibManagarrPathAsync(radarrInstance.Id, ArrApplicationKind.Radarr, source.Path, readToken);
            if (!sourcePath.Success) return Finish(result, ExistingEpisodeFileStatus.Unavailable, sourcePath.Failure!);
            result.ExpectedTarget = sourcePath.Path;
            PathMappingResult sourceBoundary = await paths.EnsureWithinStorageAreaAsync(ArrApplicationKind.Radarr, result.ExpectedTarget!, readToken);
            if (!sourceBoundary.Success) return Finish(result, ExistingEpisodeFileStatus.Unavailable, sourceBoundary.Failure!);

            return Inspect(settings.Symlinks, result, readToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException
            or OperationCanceledException or JsonException or ArgumentException or NotSupportedException)
        {
            token.ThrowIfCancellationRequested();
            return Finish(result, ExistingEpisodeFileStatus.Unavailable, $"The existing episode file could not be verified: {exception.Message}");
        }
    }

    private ExistingEpisodeFileCheck Inspect(SymlinkSettings settings, ExistingEpisodeFileCheck result, CancellationToken token)
    {
        string? linkText = files.ReadLinkTarget(result.EpisodePath!);
        result.LinkText = linkText;
        if (linkText is null) return Finish(result, ExistingEpisodeFileStatus.Unavailable, "The symlink target could not be read.");
        Resolution actual = Resolve(settings, result.EpisodePath!, token);
        result.ActualTarget = actual.Path;
        if (actual.State == ResolutionState.Outside)
            return Finish(result, ExistingEpisodeFileStatus.TargetMismatch, $"The link points outside the configured media directories: '{actual.Path}'. The target was not opened.");
        if (actual.State is ResolutionState.Missing or ResolutionState.Cycle)
            return Finish(result, ExistingEpisodeFileStatus.BrokenSymlink, $"The symlink is broken or cyclic at '{actual.Path}'. Expected movie: '{result.ExpectedTarget}'.");

        Resolution expected = Resolve(settings, result.ExpectedTarget!, token);
        if (expected.State != ResolutionState.Resolved)
            return Finish(result, ExistingEpisodeFileStatus.Unavailable, $"The proposed movie cannot be read safely at '{expected.Path}'.");
        if (!PathComparer.Equals(actual.Path, expected.Path))
            return Finish(result, ExistingEpisodeFileStatus.TargetMismatch, $"The symlink resolves to '{actual.Path}', but the proposed movie is '{expected.Path}'.");

        files.OpenForRead(result.EpisodePath!);
        Resolution afterRead = Resolve(settings, result.EpisodePath!, token);
        if (files.ReadLinkTarget(result.EpisodePath!) != linkText || afterRead.State != ResolutionState.Resolved
            || !PathComparer.Equals(afterRead.Path, actual.Path))
            return Finish(result, ExistingEpisodeFileStatus.Unavailable, "The symlink changed while being inspected. Reload to check it again.");
        return Finish(result, ExistingEpisodeFileStatus.Matches,
            $"The existing symlink resolves to the proposed movie '{expected.Path}' and is readable. It was not adopted or modified.");
    }

    private Resolution Resolve(SymlinkSettings settings, string path, CancellationToken token)
    {
        HashSet<string> seen = new(PathComparer);
        path = Path.GetFullPath(path);
        for (int depth = 0; depth < MaxLinkDepth; depth++)
        {
            token.ThrowIfCancellationRequested();
            if (!settings.StorageAreas.Any(area => ArrPathRules.IsWithinRoot(area.MoviesRoot, path)
                || ArrPathRules.IsWithinRoot(area.SeriesRoot, path))) return new(path, ResolutionState.Outside);
            SymlinkPathGuard.CheckParents(files, path);
            if (!seen.Add(path)) return new(path, ResolutionState.Cycle);
            if (!files.EntryExists(path)) return new(path, ResolutionState.Missing);
            if (!files.IsSymlink(path)) return new(path, ResolutionState.Resolved);
            string target = files.ReadLinkTarget(path) ?? throw new IOException($"The target of '{path}' cannot be read.");
            path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, target));
        }
        return new(path, ResolutionState.Cycle);
    }

    private static Task<T> ReadOnce<T>(ConcurrentDictionary<(string, int), Lazy<Task<T>>> cache, (string, int) key, Func<Task<T>> read) =>
        cache.GetOrAdd(key, _ => new Lazy<Task<T>>(read)).Value;

    private static ExistingEpisodeFileCheck Finish(ExistingEpisodeFileCheck result, ExistingEpisodeFileStatus status, string detail)
    {
        result.Status = status;
        result.Detail = detail;
        return result;
    }

    private enum ResolutionState { Resolved, Missing, Outside, Cycle }
    private sealed record Resolution(string Path, ResolutionState State);
}
