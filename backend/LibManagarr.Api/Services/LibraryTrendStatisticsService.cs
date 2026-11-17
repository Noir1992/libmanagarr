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
            .ThenBy((entry) => entry.HistoryId ?? NumericHistoryId(entry.SourceKey))
            .ThenBy((entry) => entry.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allEntries.Count == 0)
        {
            return new LibraryHistoryStatisticsResponse();
        }

        IReadOnlyList<LibraryStorageAdjustment> storageAdjustments = BuildLibraryStorageAdjustments(
            settings, allEntries, sonarrInstanceIds, radarrInstanceIds);
        List<LibraryHistoryPoint> allPoints = BuildPoints(allEntries, storageAdjustments);
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
            AvailableFrom = allEntries.Select(entry => entry.OccurredAt).Concat(storageAdjustments.Select(adjustment => adjustment.At)).Min(),
            AvailableTo = allEntries.Select(entry => entry.OccurredAt).Concat(storageAdjustments.Select(adjustment => adjustment.At)).Max()
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

    private static List<LibraryHistoryPoint> BuildPoints(IReadOnlyList<LibraryChangeHistoryEntry> entries,
        IReadOnlyList<LibraryStorageAdjustment> adjustments)
    {
        List<LibraryHistoryPoint> points = [];
        int totalCount = 0;
        long totalSizeBytes = 0;

        var eventsByDay = entries.GroupBy(entry => GetUtcDayBucket(entry.OccurredAt)).ToDictionary(group => group.Key, group => group.ToArray());
        var adjustmentsByDay = adjustments.GroupBy(adjustment => GetUtcDayBucket(adjustment.At))
            .ToDictionary(group => group.Key, group => group.Sum(adjustment => adjustment.SizeBytes));
        foreach (DateTime day in eventsByDay.Keys.Union(adjustmentsByDay.Keys).Order())
        {
            LibraryChangeHistoryEntry[] dayEntries = eventsByDay.GetValueOrDefault(day) ?? [];
            int deltaCount = 0;
            long deltaSizeBytes = adjustmentsByDay.GetValueOrDefault(day);

            foreach (LibraryChangeHistoryEntry entry in dayEntries)
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
                OccurredAt = new DateTimeOffset(day, TimeSpan.Zero),
                MediaFileCount = totalCount,
                TotalSizeBytes = totalSizeBytes,
                DeltaMediaFileCount = deltaCount,
                IsSizeDeltaKnown = !adjustmentsByDay.ContainsKey(day) && dayEntries.All(entry => entry.IsSizeDeltaKnown && entry.EventType != LibraryInventoryHistoryBuilder.EventType),
                DeltaSizeBytes = deltaSizeBytes
            });
        }

        return points;
    }

    private static IReadOnlyList<LibraryStorageAdjustment> BuildLibraryStorageAdjustments(AppSettings settings,
        IReadOnlyList<LibraryChangeHistoryEntry> entries, IReadOnlySet<string>? sonarrInstanceIds,
        IReadOnlySet<string>? radarrInstanceIds)
    {
        List<LibraryStorageAdjustment> openingBalances = LibraryHistoryStorageBalance.OpeningBalances(entries);
        List<LibraryStorageAdjustment> adjustments = [.. openingBalances];
        void ReconcileSnapshot(ArrApplicationKind kind, string instanceId, DateTimeOffset syncedAt, long sizeBytes)
        {
            if (syncedAt == default) return; // No trustworthy observation time (e.g. imported legacy data).
            long recorded = entries.Where(entry => entry.Kind == kind && entry.InstanceId == instanceId && entry.OccurredAt <= syncedAt)
                .Sum(entry => entry.DeltaSizeBytes);
            recorded += openingBalances.Where(balance => balance.Kind == kind && balance.InstanceId == instanceId && balance.At <= syncedAt)
                .Sum(balance => balance.SizeBytes);
            long difference = sizeBytes - recorded;
            if (difference != 0) adjustments.Add(new(kind, instanceId, syncedAt, difference));
        }

        foreach (SonarrInstanceSnapshot snapshot in settings.SonarrSnapshots.Where(snapshot => sonarrInstanceIds is null || sonarrInstanceIds.Contains(snapshot.InstanceId)))
            ReconcileSnapshot(ArrApplicationKind.Sonarr, snapshot.InstanceId, snapshot.SyncedAt,
                snapshot.Series.SelectMany(series => series.Seasons).SelectMany(season => season.Episodes)
                    .Where(episode => episode.HasFile).Sum(episode => episode.File.Size ?? 0));
        foreach (RadarrInstanceSnapshot snapshot in settings.RadarrSnapshots.Where(snapshot => radarrInstanceIds is null || radarrInstanceIds.Contains(snapshot.InstanceId)))
            ReconcileSnapshot(ArrApplicationKind.Radarr, snapshot.InstanceId, snapshot.SyncedAt,
                snapshot.Movies.Where(movie => movie.HasFile).Sum(movie => movie.File.Size ?? 0));
        return adjustments;
    }

    private static long NumericHistoryId(string key) => long.TryParse(key.AsSpan(key.LastIndexOf(':') + 1), out long id) ? id : long.MaxValue;

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
