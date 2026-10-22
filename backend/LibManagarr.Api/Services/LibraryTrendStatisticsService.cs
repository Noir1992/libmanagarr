using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public sealed class LibraryTrendStatisticsService
{
    public LibraryHistoryStatisticsResponse Build(
        AppSettings settings,
        IReadOnlyList<LibraryChangeHistoryEntry> historyEntries,
        IReadOnlySet<string>? sonarrInstanceIds,
        IReadOnlySet<string>? radarrInstanceIds,
        DateTimeOffset? start,
        DateTimeOffset? end)
    {
        if (start is not null && end is not null && start > end)
        {
            return new LibraryHistoryStatisticsResponse();
        }

        List<LibraryChangeHistoryEntry> allEntries = historyEntries
            .Where((entry) => MatchesInstance(entry, sonarrInstanceIds, radarrInstanceIds))
            .OrderBy((entry) => entry.OccurredAt)
            .ThenBy((entry) => entry.Kind)
            .ThenBy((entry) => entry.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allEntries.Count == 0)
        {
            return new LibraryHistoryStatisticsResponse();
        }

        List<LibraryHistoryPoint> allPoints = BuildPoints(allEntries);
        List<LibraryHistoryPoint> points = allPoints
            .Where((point) => start is null || point.OccurredAt >= start)
            .Where((point) => end is null || point.OccurredAt <= end)
            .ToList();
        List<LibraryChangeHistoryEntry> visibleHistory = allEntries
            .Where((entry) => start is null || entry.OccurredAt >= start)
            .Where((entry) => end is null || entry.OccurredAt <= end)
            .OrderByDescending((entry) => entry.OccurredAt)
            .ThenByDescending((entry) => entry.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        (long totalMediaFileCount, long totalSizeBytes) = CalculateTotals(settings, sonarrInstanceIds, radarrInstanceIds);

        return new LibraryHistoryStatisticsResponse
        {
            Points = points,
            History = visibleHistory,
            TotalMediaFileCount = totalMediaFileCount,
            TotalSizeBytes = totalSizeBytes,
            AvailableFrom = allEntries.Min((entry) => entry.OccurredAt),
            AvailableTo = allEntries.Max((entry) => entry.OccurredAt)
        };
    }

    private static bool MatchesInstance(
        LibraryChangeHistoryEntry entry,
        IReadOnlySet<string>? sonarrInstanceIds,
        IReadOnlySet<string>? radarrInstanceIds)
    {
        return entry.Kind switch
        {
            ArrApplicationKind.Sonarr => sonarrInstanceIds is null || sonarrInstanceIds.Contains(entry.InstanceId),
            ArrApplicationKind.Radarr => radarrInstanceIds is null || radarrInstanceIds.Contains(entry.InstanceId),
            _ => false
        };
    }

    private static DateTime GetUtcDayBucket(DateTimeOffset value)
    {
        return value.UtcDateTime.Date;
    }

    private static List<LibraryHistoryPoint> BuildPoints(IReadOnlyList<LibraryChangeHistoryEntry> entries)
    {
        List<LibraryHistoryPoint> points = [];
        int totalCount = 0;
        long totalSizeBytes = 0;

        foreach (IGrouping<DateTime, LibraryChangeHistoryEntry> group in entries.GroupBy((entry) => GetUtcDayBucket(entry.OccurredAt)))
        {
            int deltaCount = 0;
            long deltaSizeBytes = 0;

            foreach (LibraryChangeHistoryEntry entry in group)
            {
                deltaCount += entry.ChangeType switch
                {
                    LibraryChangeType.Added => 1,
                    LibraryChangeType.Deleted => -1,
                    LibraryChangeType.Updated => 0,
                    _ => 0
                };
                deltaSizeBytes += entry.DeltaSizeBytes;
            }

            totalCount += deltaCount;
            totalSizeBytes += deltaSizeBytes;

            points.Add(new LibraryHistoryPoint
            {
                OccurredAt = new DateTimeOffset(group.Key, TimeSpan.Zero),
                MediaFileCount = totalCount,
                TotalSizeBytes = totalSizeBytes,
                DeltaMediaFileCount = deltaCount,
                DeltaSizeBytes = deltaSizeBytes
            });
        }

        return points;
    }

    // Totals reflect the current library state from the latest snapshots; the change history can be
    // incomplete (arr history retention/clearing), which would otherwise undercount files permanently.
    private static (long totalMediaFileCount, long totalSizeBytes) CalculateTotals(
        AppSettings settings,
        IReadOnlySet<string>? sonarrInstanceIds,
        IReadOnlySet<string>? radarrInstanceIds)
    {
        long totalMediaFileCount = 0;
        long totalSizeBytes = 0;

        foreach (SonarrInstanceSnapshot snapshot in settings.SonarrSnapshots)
        {
            if (sonarrInstanceIds is not null && !sonarrInstanceIds.Contains(snapshot.InstanceId))
            {
                continue;
            }

            foreach (SonarrSeriesSnapshot series in snapshot.Series)
            {
                foreach (SonarrSeasonSnapshot season in series.Seasons)
                {
                    foreach (SonarrEpisodeSnapshot episode in season.Episodes)
                    {
                        if (episode.HasFile)
                        {
                            totalMediaFileCount++;
                            totalSizeBytes += episode.File.Size ?? 0;
                        }
                    }
                }
            }
        }

        foreach (RadarrInstanceSnapshot snapshot in settings.RadarrSnapshots)
        {
            if (radarrInstanceIds is not null && !radarrInstanceIds.Contains(snapshot.InstanceId))
            {
                continue;
            }

            foreach (RadarrMovieSnapshot movie in snapshot.Movies)
            {
                if (movie.HasFile)
                {
                    totalMediaFileCount++;
                    totalSizeBytes += movie.File.Size ?? 0;
                }
            }
        }

        return (totalMediaFileCount, totalSizeBytes);
    }
}
