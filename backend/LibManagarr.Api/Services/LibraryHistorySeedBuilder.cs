using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

// Synthesizes initial "Added" entries for library files whose real import event predates the retained
// arr history (retention/clearing), so the trend chart starts at the real library size instead of zero.
// Files whose media key already has an Added entry are skipped, which keeps repeated syncs idempotent.
public static class LibraryHistorySeedBuilder
{
    public static IReadOnlyList<LibraryChangeHistoryEntry> BuildSonarrSeeds(
        ArrInstanceSettings instance,
        SonarrInstanceSnapshot snapshot,
        IReadOnlyList<LibraryChangeHistoryEntry> history)
    {
        HashSet<string> addedMediaKeys = BuildAddedMediaKeys(history);
        Dictionary<string, long> accountedDeltas = BuildAccountedDeltas(history);
        List<LibraryChangeHistoryEntry> seeds = [];

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

                    string mediaKey = LibraryChangeHistoryBuilder.BuildSonarrMediaKey(instance.Id, episode.EpisodeId);
                    if (addedMediaKeys.Contains(mediaKey))
                    {
                        continue;
                    }

                    seeds.Add(new LibraryChangeHistoryEntry
                    {
                        SourceKey = $"seed:{mediaKey}",
                        Kind = ArrApplicationKind.Sonarr,
                        InstanceId = instance.Id,
                        InstanceName = instance.Name,
                        OccurredAt = episode.File.CreatedAt ?? snapshot.SyncedAt,
                        MediaKey = mediaKey,
                        ChangeType = LibraryChangeType.Added,
                        EntityTitle = episode.Title,
                        SeriesTitle = string.IsNullOrWhiteSpace(series.Title) ? null : series.Title,
                        FilePath = episode.File.RelativePath,
                        SizeBytes = episode.File.Size,
                        DeltaSizeBytes = ResolveSeedDelta(episode.File.Size, mediaKey, accountedDeltas)
                    });
                }
            }
        }

        return seeds;
    }

    public static IReadOnlyList<LibraryChangeHistoryEntry> BuildRadarrSeeds(
        ArrInstanceSettings instance,
        RadarrInstanceSnapshot snapshot,
        IReadOnlyList<LibraryChangeHistoryEntry> history)
    {
        HashSet<string> addedMediaKeys = BuildAddedMediaKeys(history);
        Dictionary<string, long> accountedDeltas = BuildAccountedDeltas(history);
        List<LibraryChangeHistoryEntry> seeds = [];

        foreach (RadarrMovieSnapshot movie in snapshot.Movies)
        {
            if (!movie.HasFile || movie.MovieId <= 0)
            {
                continue;
            }

            string mediaKey = LibraryChangeHistoryBuilder.BuildRadarrMediaKey(instance.Id, movie.MovieId);
            if (addedMediaKeys.Contains(mediaKey))
            {
                continue;
            }

            seeds.Add(new LibraryChangeHistoryEntry
            {
                SourceKey = $"seed:{mediaKey}",
                Kind = ArrApplicationKind.Radarr,
                InstanceId = instance.Id,
                InstanceName = instance.Name,
                OccurredAt = movie.File.CreatedAt ?? snapshot.SyncedAt,
                MediaKey = mediaKey,
                ChangeType = LibraryChangeType.Added,
                EntityTitle = movie.Title,
                FilePath = movie.File.RelativePath,
                SizeBytes = movie.File.Size,
                DeltaSizeBytes = ResolveSeedDelta(movie.File.Size, mediaKey, accountedDeltas)
            });
        }

        return seeds;
    }

    private static HashSet<string> BuildAddedMediaKeys(IReadOnlyList<LibraryChangeHistoryEntry> history)
    {
        return history
            .Where((entry) => entry.ChangeType == LibraryChangeType.Added && !string.IsNullOrWhiteSpace(entry.MediaKey))
            .Select((entry) => entry.MediaKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, long> BuildAccountedDeltas(IReadOnlyList<LibraryChangeHistoryEntry> history)
    {
        return history
            .Where((entry) => !string.IsNullOrWhiteSpace(entry.MediaKey))
            .GroupBy((entry) => entry.MediaKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                (group) => group.Key,
                (group) => group.Sum((entry) => entry.DeltaSizeBytes),
                StringComparer.OrdinalIgnoreCase);
    }

    // Existing Changed/Deleted/Updated events for the same media key already account for part of the
    // file's size (bootstrap rename events booked it in full); only the remainder is seeded, so the
    // storage curve stays truthful while the file count gets its missing +1.
    private static long ResolveSeedDelta(long? size, string mediaKey, IReadOnlyDictionary<string, long> accountedDeltas)
    {
        long accounted = accountedDeltas.GetValueOrDefault(mediaKey, 0L);
        return Math.Max(0, (size ?? 0L) - accounted);
    }
}
