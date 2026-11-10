using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public sealed class LibraryChangeHistoryBuilder
{
    public static IReadOnlyList<LibraryChangeHistoryEntry> BuildSonarrEntries(
        ArrInstanceSettings instance,
        SonarrInstanceSnapshot currentSnapshot,
        SonarrInstanceSnapshot? previousSnapshot,
        IReadOnlyList<SonarrHistoryResource> historyResources)
    {
        Dictionary<int, SonarrEpisodeSnapshotInfo> currentEpisodes = BuildCurrentSonarrEpisodeMap(currentSnapshot);
        Dictionary<int, string> currentSeriesTitles = BuildCurrentSonarrSeriesTitleMap(currentSnapshot);
        Dictionary<int, long> previousSizes = previousSnapshot is null
            ? new Dictionary<int, long>()
            : BuildCurrentSonarrEpisodeSizeMap(previousSnapshot);

        List<LibraryChangeHistoryEntry> entries = [];
        foreach (SonarrHistoryResource history in historyResources.Where(history => history.Date.HasValue)
                     .OrderBy(history => history.Date).ThenBy(history => history.Id))
        {
            LibraryChangeHistoryEntry? entry = BuildSonarrEntry(instance, currentEpisodes, currentSeriesTitles, previousSizes, history);
            if (entry is null) continue;
            entries.Add(entry);
            if (history.EpisodeId is not > 0) continue;
            int episodeId = history.EpisodeId.Value;
            // A later rename in this batch must see the size already accounted for by its import.
            if (entry.ChangeType == LibraryChangeType.Deleted) previousSizes.Remove(episodeId);
            else if (entry.SizeBytes is long size) previousSizes[episodeId] = size;
        }
        return entries;
    }

    public static IReadOnlyList<LibraryChangeHistoryEntry> BuildRadarrEntries(
        ArrInstanceSettings instance,
        RadarrInstanceSnapshot currentSnapshot,
        RadarrInstanceSnapshot? previousSnapshot,
        IReadOnlyList<RadarrHistoryResource> historyResources)
    {
        Dictionary<int, RadarrMovieSnapshotInfo> currentMovies = BuildCurrentRadarrMovieMap(currentSnapshot);
        Dictionary<int, long> previousSizes = previousSnapshot is null
            ? new Dictionary<int, long>()
            : BuildCurrentRadarrMovieSizeMap(previousSnapshot);

        return historyResources
            .Where((history) => history.Date.HasValue)
            .Select((history) => BuildRadarrEntry(instance, currentMovies, previousSizes, history))
            .Where((entry) => entry is not null)
            .Select((entry) => entry!)
            .ToArray();
    }

    private static LibraryChangeHistoryEntry? BuildSonarrEntry(
        ArrInstanceSettings instance,
        IReadOnlyDictionary<int, SonarrEpisodeSnapshotInfo> currentEpisodes,
        IReadOnlyDictionary<int, string> currentSeriesTitles,
        IReadOnlyDictionary<int, long> previousSizes,
        SonarrHistoryResource history)
    {
        LibraryChangeType? changeType = MapSonarrChangeType(history.EventType);
        if (changeType is null)
        {
            return null;
        }

        int episodeId = history.EpisodeId ?? 0;
        SonarrEpisodeSnapshotInfo? currentEpisode = episodeId > 0 && currentEpisodes.TryGetValue(episodeId, out SonarrEpisodeSnapshotInfo? currentValue)
            ? currentValue
            : null;

        long? currentSize = currentEpisode?.SizeBytes;
        long? previousSize = episodeId > 0 && previousSizes.TryGetValue(episodeId, out long previousSizeValue)
            ? previousSizeValue
            : null;

        long deltaSizeBytes = ResolveDeltaSize(changeType.Value, currentSize, previousSize);

        return new LibraryChangeHistoryEntry
        {
            SourceKey = history.Id.HasValue ? $"sonarr:{history.Id.Value}" : BuildFallbackSourceKey(
                "sonarr",
                history.EventType,
                history.Date,
                history.SourceTitle,
                episodeId),
            Kind = ArrApplicationKind.Sonarr,
            InstanceId = instance.Id,
            InstanceName = instance.Name,
            OccurredAt = history.Date!.Value,
            MediaKey = BuildSonarrMediaKey(instance.Id, episodeId),
            ChangeType = changeType.Value,
            EntityTitle = currentEpisode?.Title ?? history.SourceTitle ?? "Unknown episode",
            SeriesTitle = ResolveSonarrSeriesTitle(history, currentEpisode, currentSeriesTitles),
            FilePath = currentEpisode?.FilePath,
            SizeBytes = currentSize ?? previousSize,
            DeltaSizeBytes = deltaSizeBytes
        };
    }

    private static LibraryChangeHistoryEntry? BuildRadarrEntry(
        ArrInstanceSettings instance,
        IReadOnlyDictionary<int, RadarrMovieSnapshotInfo> currentMovies,
        IReadOnlyDictionary<int, long> previousSizes,
        RadarrHistoryResource history)
    {
        LibraryChangeType? changeType = MapRadarrChangeType(history.EventType);
        if (changeType is null)
        {
            return null;
        }

        int movieId = history.MovieId ?? 0;
        RadarrMovieSnapshotInfo? currentMovie = movieId > 0 && currentMovies.TryGetValue(movieId, out RadarrMovieSnapshotInfo? currentValue)
            ? currentValue
            : null;

        long? currentSize = currentMovie?.SizeBytes;
        long? previousSize = movieId > 0 && previousSizes.TryGetValue(movieId, out long previousSizeValue)
            ? previousSizeValue
            : null;

        long deltaSizeBytes = ResolveDeltaSize(changeType.Value, currentSize, previousSize);

        return new LibraryChangeHistoryEntry
        {
            SourceKey = history.Id.HasValue ? $"radarr:{history.Id.Value}" : BuildFallbackSourceKey(
                "radarr",
                history.EventType,
                history.Date,
                history.SourceTitle,
                movieId),
            Kind = ArrApplicationKind.Radarr,
            InstanceId = instance.Id,
            InstanceName = instance.Name,
            OccurredAt = history.Date!.Value,
            MediaKey = BuildRadarrMediaKey(instance.Id, movieId),
            ChangeType = changeType.Value,
            EntityTitle = currentMovie?.Title ?? history.SourceTitle ?? "Unknown movie",
            FilePath = currentMovie?.FilePath,
            SizeBytes = currentSize ?? previousSize,
            DeltaSizeBytes = deltaSizeBytes
        };
    }

    private static Dictionary<int, SonarrEpisodeSnapshotInfo> BuildCurrentSonarrEpisodeMap(SonarrInstanceSnapshot snapshot)
    {
        Dictionary<int, SonarrEpisodeSnapshotInfo> items = new();

        foreach (SonarrSeriesSnapshot series in snapshot.Series)
        {
            foreach (SonarrSeasonSnapshot season in series.Seasons)
            {
                foreach (SonarrEpisodeSnapshot episode in season.Episodes)
                {
                    if (!episode.HasFile || episode.EpisodeId <= 0)
                    {
                        continue;
                    }

                    items[episode.EpisodeId] = new SonarrEpisodeSnapshotInfo(
                        episode.Title,
                        episode.File.RelativePath,
                        episode.File.Size,
                        series.Title);
                }
            }
        }

        return items;
    }

    private static Dictionary<int, string> BuildCurrentSonarrSeriesTitleMap(SonarrInstanceSnapshot snapshot)
    {
        Dictionary<int, string> items = new();

        foreach (SonarrSeriesSnapshot series in snapshot.Series)
        {
            if (series.SeriesId > 0 && !string.IsNullOrWhiteSpace(series.Title))
            {
                items[series.SeriesId] = series.Title;
            }
        }

        return items;
    }

    private static string? ResolveSonarrSeriesTitle(
        SonarrHistoryResource history,
        SonarrEpisodeSnapshotInfo? currentEpisode,
        IReadOnlyDictionary<int, string> currentSeriesTitles)
    {
        if (history.SeriesId is int historySeriesId && currentSeriesTitles.TryGetValue(historySeriesId, out string? seriesTitle))
        {
            return seriesTitle;
        }

        return string.IsNullOrWhiteSpace(currentEpisode?.SeriesTitle) ? null : currentEpisode!.SeriesTitle;
    }

    private static Dictionary<int, long> BuildCurrentSonarrEpisodeSizeMap(SonarrInstanceSnapshot snapshot)
    {
        Dictionary<int, long> items = new();

        foreach (SonarrSeriesSnapshot series in snapshot.Series)
        {
            foreach (SonarrSeasonSnapshot season in series.Seasons)
            {
                foreach (SonarrEpisodeSnapshot episode in season.Episodes)
                {
                    if (!episode.HasFile || episode.EpisodeId <= 0)
                    {
                        continue;
                    }

                    items[episode.EpisodeId] = episode.File.Size ?? 0;
                }
            }
        }

        return items;
    }

    private static Dictionary<int, RadarrMovieSnapshotInfo> BuildCurrentRadarrMovieMap(RadarrInstanceSnapshot snapshot)
    {
        Dictionary<int, RadarrMovieSnapshotInfo> items = new();

        foreach (RadarrMovieSnapshot movie in snapshot.Movies.Where(movie => movie.HasFile))
        {
            items[movie.MovieId] = new RadarrMovieSnapshotInfo(
                movie.Title,
                movie.File.RelativePath,
                movie.File.Size);
        }

        return items;
    }

    private static Dictionary<int, long> BuildCurrentRadarrMovieSizeMap(RadarrInstanceSnapshot snapshot)
    {
        Dictionary<int, long> items = new();

        foreach (RadarrMovieSnapshot movie in snapshot.Movies.Where(movie => movie.HasFile))
        {
            items[movie.MovieId] = movie.File.Size ?? 0;
        }

        return items;
    }

    private static LibraryChangeType? MapSonarrChangeType(string? eventType)
    {
        return eventType switch
        {
            "downloadFolderImported" => LibraryChangeType.Added,
            "seriesFolderImported" => LibraryChangeType.Added,
            "episodeFileRenamed" => LibraryChangeType.Changed,
            "episodeFileDeleted" => LibraryChangeType.Deleted,
            _ => null
        };
    }

    private static LibraryChangeType? MapRadarrChangeType(string? eventType)
    {
        return eventType switch
        {
            "downloadFolderImported" => LibraryChangeType.Added,
            "movieFolderImported" => LibraryChangeType.Added,
            "movieFileRenamed" => LibraryChangeType.Changed,
            "movieFileDeleted" => LibraryChangeType.Deleted,
            _ => null
        };
    }

    private static long ResolveDeltaSize(
        LibraryChangeType changeType,
        long? currentSize,
        long? previousSize)
    {
        return changeType switch
        {
            LibraryChangeType.Added => currentSize ?? 0,
            LibraryChangeType.Deleted => -(previousSize ?? currentSize ?? 0),
            LibraryChangeType.Changed => (currentSize ?? 0) - (previousSize ?? 0),
            _ => 0
        };
    }

    private static string BuildFallbackSourceKey(
        string prefix,
        string? eventType,
        DateTimeOffset? date,
        string? sourceTitle,
        int entityId)
    {
        string dateValue = date.HasValue ? date.Value.ToUniversalTime().ToString("O") : string.Empty;
        return $"{prefix}:{eventType}:{entityId}:{dateValue}:{sourceTitle}";
    }

    // Internal: LibraryHistorySeedBuilder must derive byte-identical keys for its coverage check.
    internal static string BuildSonarrMediaKey(string instanceId, int episodeId)
    {
        return $"sonarr:{instanceId}:{episodeId}";
    }

    internal static string BuildRadarrMediaKey(string instanceId, int movieId)
    {
        return $"radarr:{instanceId}:{movieId}";
    }

    private sealed record SonarrEpisodeSnapshotInfo(string Title, string? FilePath, long? SizeBytes, string? SeriesTitle = null);

    private sealed record RadarrMovieSnapshotInfo(string Title, string? FilePath, long? SizeBytes);
}
