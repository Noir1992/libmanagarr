using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>Rebuilds the display history from durable events and fills gaps proven by snapshots.</summary>
public static class LibraryHistoryReconciler
{
    public static List<LibraryChangeHistoryEntry> MergeEvents(
        IEnumerable<LibraryChangeHistoryEntry> stored, IEnumerable<LibraryChangeHistoryEntry> fetched)
    {
        Dictionary<(ArrApplicationKind, string, string), LibraryChangeHistoryEntry> events = [];
        foreach (LibraryChangeHistoryEntry entry in stored)
            events[(entry.Kind, entry.InstanceId, entry.SourceKey)] = entry;
        var inferredByMedia = events.Where(p => p.Value.IsInferred).GroupBy(p => (p.Value.Kind, p.Value.InstanceId, p.Value.MediaKey))
            .ToDictionary(g => g.Key, g => g.ToArray());
        foreach (LibraryChangeHistoryEntry entry in fetched)
        {
            var key = (entry.Kind, entry.InstanceId, entry.SourceKey);
            // Replaying an import must not replace a previously observed size with today's file.
            if (events.TryGetValue(key, out var existing) && existing.SizeBytes.HasValue && !HasEventSize(entry))
            {
                // Keep the observed size, but enrich old events with newly recovered reasons/IDs.
                entry.SizeBytes = existing.SizeBytes;
                entry.IsSizeDeltaKnown = existing.IsSizeDeltaKnown;
                entry.DeltaSizeBytes = existing.DeltaSizeBytes;
            }
            events[key] = entry;
            if (entry.ChangeType == LibraryChangeType.Added && !entry.IsInferred && entry.FileId is not null
                && inferredByMedia.TryGetValue((entry.Kind, entry.InstanceId, entry.MediaKey), out var additions))
            {
                foreach (var candidate in additions.Where(p => p.Value.EventType == "libraryFileObserved"
                    && p.Value.FileId == entry.FileId && p.Value.ObservedSince <= entry.OccurredAt
                    && entry.OccurredAt <= p.Value.OccurredAt))
                    events.Remove(candidate.Key);
            }
            if (entry.ChangeType == LibraryChangeType.Deleted && !entry.IsInferred)
            {
                if (inferredByMedia.TryGetValue((entry.Kind, entry.InstanceId, entry.MediaKey), out var inferred))
                    foreach (var candidate in inferred.Where(p => p.Value.ChangeType == LibraryChangeType.Deleted && p.Value.ObservedSince.HasValue
                        && entry.OccurredAt >= p.Value.ObservedSince.Value && entry.OccurredAt <= p.Value.OccurredAt
                        && (entry.FileId is null || p.Value.FileId is null || entry.FileId == p.Value.FileId)
                        && (!entry.SizeBytes.HasValue || !p.Value.SizeBytes.HasValue || entry.SizeBytes == p.Value.SizeBytes)))
                        events.Remove(candidate.Key);
            }
        }
        // Replace a seeded initial import only when its real import is recovered, never when a
        // later replacement file is imported (that still needs the initial file count).
        var firstImports = events.Values.Where(e => e.ChangeType == LibraryChangeType.Added && !e.SourceKey.StartsWith("seed:", StringComparison.Ordinal))
            .GroupBy(e => (e.Kind, e.InstanceId, e.MediaKey)).ToDictionary(g => g.Key, g => g.Min(e => e.OccurredAt));
        foreach (var seedKey in events.Where(p => p.Value.SourceKey.StartsWith("seed:", StringComparison.Ordinal)
            && firstImports.TryGetValue((p.Value.Kind, p.Value.InstanceId, p.Value.MediaKey), out var importedAt)
            && importedAt <= p.Value.OccurredAt).Select(p => p.Key).ToArray()) events.Remove(seedKey);
        return events.Values.ToList();
    }

    private static bool HasEventSize(LibraryChangeHistoryEntry entry) => entry.EventData?.Any(p =>
        p.Key.Equals("Size", StringComparison.OrdinalIgnoreCase) && long.TryParse(p.Value, out long size) && size >= 0) == true;

    public static List<LibraryChangeHistoryEntry> Project(IReadOnlyList<LibraryChangeHistoryEntry> events, ILogger? logger = null)
    {
        // A legacy merged update has no constituent events. Replace it only when both originals
        // can be recovered; otherwise keep it and suppress partially recovered duplicates.
        using IDisposable? filter = TemporarySymlinkDeleteTiming.Current(logger, "history-project-filter");
        List<LibraryChangeHistoryEntry> original = events.Where(e => !(e.IsLegacy && e.ChangeType == LibraryChangeType.Updated)
            && !(e.EventType == LibraryInventoryHistoryBuilder.EventType && e.DeltaSizeBytes == 0)).ToList();
        filter?.Dispose();
        List<LibraryChangeHistoryEntry> rebuilt;
        using (TemporarySymlinkDeleteTiming.Current(logger, $"history-project-normalize rows={original.Count}"))
            rebuilt = LibraryChangeHistoryNormalizer.Normalize(original);
        using IDisposable? legacyTiming = TemporarySymlinkDeleteTiming.Current(logger, "history-project-legacy-reconcile");
        // Index the normalized history once. A legacy entry only suppresses recovered events
        // for its own medium; scanning the entire library for every legacy entry is unnecessary.
        var sourceCounts = rebuilt.GroupBy(e => (e.Kind, e.InstanceId, e.SourceKey))
            .ToDictionary(g => g.Key, g => g.Count());
        var recoverableByMedia = rebuilt.Where(e => e.ChangeType is LibraryChangeType.Added or LibraryChangeType.Deleted)
            .GroupBy(e => (e.Kind, e.InstanceId, e.MediaKey)).ToDictionary(g => g.Key, g => g.ToArray());
        HashSet<LibraryChangeHistoryEntry> suppressed = [];
        List<LibraryChangeHistoryEntry> retainedLegacy = [];
        foreach (LibraryChangeHistoryEntry legacy in events.Where(e => e.IsLegacy && e.ChangeType == LibraryChangeType.Updated))
        {
            var source = (legacy.Kind, legacy.InstanceId, legacy.SourceKey);
            if (sourceCounts.ContainsKey(source)) continue;
            if (recoverableByMedia.TryGetValue((legacy.Kind, legacy.InstanceId, legacy.MediaKey), out var recovered))
                foreach (var entry in recovered)
                {
                    if (suppressed.Contains(entry) || (entry.OccurredAt != legacy.OccurredAt
                        && !legacy.SourceKey.Contains($":{entry.OccurredAt:O}:", StringComparison.Ordinal))) continue;
                    suppressed.Add(entry);
                    var key = (entry.Kind, entry.InstanceId, entry.SourceKey);
                    if (--sourceCounts[key] == 0) sourceCounts.Remove(key);
                }
            retainedLegacy.Add(legacy);
            sourceCounts[source] = 1;
        }
        legacyTiming?.Dispose();
        using IDisposable? sort = TemporarySymlinkDeleteTiming.Current(logger, $"history-project-sort rows={rebuilt.Count}");
        return rebuilt.Where(e => !suppressed.Contains(e)).Concat(retainedLegacy).OrderBy(e => e.OccurredAt).ToList();
    }

    public static IReadOnlyList<LibraryChangeHistoryEntry> InferSonarrDeletions(ArrInstanceSettings instance,
        SonarrInstanceSnapshot? previous, SonarrInstanceSnapshot current, IReadOnlyList<LibraryChangeHistoryEntry> events,
        int? seriesId = null)
    {
        var previousFiles = previous is null ? new Dictionary<int, LibraryChangeHistoryBuilder.FileInfo>() : LibraryChangeHistoryBuilder.SonarrFiles(previous);
        var currentFiles = LibraryChangeHistoryBuilder.SonarrFiles(current);
        HashSet<int>? scope = seriesId.HasValue ? (previous?.Series ?? []).Concat(current.Series)
            .Where(series => series.SeriesId == seriesId).SelectMany(series => series.Seasons)
            .SelectMany(season => season.Episodes).Select(episode => episode.EpisodeId).ToHashSet() : null;
        return Infer(instance, ArrApplicationKind.Sonarr, previousFiles, currentFiles,
            previous?.SyncedAt ?? current.SyncedAt, current.SyncedAt, events,
            previous is null ? null : LibraryChangeHistoryBuilder.SonarrObservationTimes(previous), scope);
    }

    public static IReadOnlyList<LibraryChangeHistoryEntry> InferRadarrDeletions(ArrInstanceSettings instance,
        RadarrInstanceSnapshot? previous, RadarrInstanceSnapshot current, IReadOnlyList<LibraryChangeHistoryEntry> events) =>
        Infer(instance, ArrApplicationKind.Radarr,
            previous is null ? [] : LibraryChangeHistoryBuilder.RadarrFiles(previous), LibraryChangeHistoryBuilder.RadarrFiles(current),
            previous?.SyncedAt ?? current.SyncedAt, current.SyncedAt, events);

    /// <summary>Rescans attach files without creating Sonarr import history. Record their observed arrival.</summary>
    public static IReadOnlyList<LibraryChangeHistoryEntry> InferSonarrAdditions(ArrInstanceSettings instance,
        SonarrInstanceSnapshot? previous, SonarrInstanceSnapshot current,
        IReadOnlyList<LibraryChangeHistoryEntry> events, int? seriesId = null)
    {
        var previousFiles = previous is null ? [] : LibraryChangeHistoryBuilder.SonarrFiles(previous);
        var previousEpisodes = (previous?.Series ?? []).SelectMany(s => s.Seasons)
            .SelectMany(s => s.Episodes).Select(e => e.EpisodeId).ToHashSet();
        var observedSince = previous is null ? [] : LibraryChangeHistoryBuilder.SonarrObservationTimes(previous);
        var latest = events.Where(e => e.Kind == ArrApplicationKind.Sonarr && e.InstanceId == instance.Id
                && e.OccurredAt <= current.SyncedAt && e.ChangeType is LibraryChangeType.Added or LibraryChangeType.Deleted)
            .GroupBy(e => e.MediaKey).ToDictionary(g => g.Key,
                g => g.OrderBy(e => e.OccurredAt).ThenBy(e => e.HistoryId ?? NumericId(e.SourceKey)).Last());
        List<LibraryChangeHistoryEntry> additions = [];
        foreach (var series in current.Series.Where(s => !seriesId.HasValue || s.SeriesId == seriesId))
        foreach (var episode in series.Seasons.SelectMany(s => s.Episodes).Where(e => e.HasFile && e.EpisodeId > 0))
        {
            string key = LibraryChangeHistoryBuilder.BuildSonarrMediaKey(instance.Id, episode.EpisodeId);
            latest.TryGetValue(key, out var last);
            DateTimeOffset since = observedSince.GetValueOrDefault(episode.EpisodeId,
                previous?.LastFullSyncAt ?? previous?.SyncedAt ?? current.SyncedAt);
            bool newlyPresent = previousEpisodes.Contains(episode.EpisodeId) && !previousFiles.ContainsKey(episode.EpisodeId);
            bool missingImport = last?.ChangeType == LibraryChangeType.Deleted;
            if (!newlyPresent && !missingImport) continue;
            if (last?.ChangeType == LibraryChangeType.Added && last.OccurredAt >= since
                && (last.FileId is null || last.FileId == episode.File.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture))) continue;
            // With no previous observation, initial inventory is handled by the seed builder.
            if (missingImport && last!.OccurredAt > since) since = last.OccurredAt;
            additions.Add(new LibraryChangeHistoryEntry
            {
                SourceKey = $"snapshot-add:{key}:{current.SyncedAt:O}",
                Kind = ArrApplicationKind.Sonarr,
                InstanceId = instance.Id,
                InstanceName = instance.Name,
                MediaKey = key,
                OccurredAt = current.SyncedAt,
                ObservedSince = since,
                ChangeType = LibraryChangeType.Added,
                IsInferred = true,
                EventType = "libraryFileObserved",
                EntityTitle = episode.Title,
                SeriesTitle = series.Title,
                FilePath = episode.File.Path ?? episode.File.RelativePath,
                FileId = episode.File.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SizeBytes = episode.File.Size,
                DeltaSizeBytes = episode.File.Size ?? 0,
                IsSizeDeltaKnown = episode.File.Size.HasValue
            });
        }
        return additions;
    }

    private static IReadOnlyList<LibraryChangeHistoryEntry> Infer(ArrInstanceSettings instance, ArrApplicationKind kind,
        Dictionary<int, LibraryChangeHistoryBuilder.FileInfo> previous, Dictionary<int, LibraryChangeHistoryBuilder.FileInfo> current,
        DateTimeOffset defaultSince, DateTimeOffset observedAt, IReadOnlyList<LibraryChangeHistoryEntry> events,
        Dictionary<int, DateTimeOffset>? sinceById = null, HashSet<int>? scope = null)
    {
        string prefix = $"{kind.ToString().ToLowerInvariant()}:{instance.Id}:";
        List<LibraryChangeHistoryEntry> inferred = [];
        var states = events.Where(e => e.Kind == kind && e.InstanceId == instance.Id
            && e.OccurredAt <= observedAt && e.MediaKey.StartsWith(prefix, StringComparison.Ordinal))
            .GroupBy(e => e.MediaKey).ToDictionary(g => g.Key,
                g => g.OrderBy(e => e.OccurredAt).ThenBy(e => e.HistoryId ?? NumericId(e.SourceKey)).Last());
        // A fresh sync has no previous snapshot, but retained imports and renames still prove
        // that a file existed. Its absence now proves removal from the library, not its exact date.
        var candidates = new Dictionary<int, LibraryChangeHistoryBuilder.FileInfo>(previous);
        foreach (var (key, state) in states)
        {
            if (state.ChangeType == LibraryChangeType.Deleted || !int.TryParse(key.AsSpan(prefix.Length), out int id) || id <= 0) continue;
            candidates.TryAdd(id, new(state.EntityTitle, state.SeriesTitle, new MediaFileSnapshot
            {
                Size = state.SizeBytes,
                RelativePath = state.FilePath,
                Id = int.TryParse(state.FileId, out int fileId) ? fileId : null
            }));
        }
        foreach (var (id, old) in candidates)
        {
            if (current.ContainsKey(id) || scope is not null && !scope.Contains(id)) continue;
            string mediaKey = $"{prefix}{id}";
            states.TryGetValue(mediaKey, out var state);
            DateTimeOffset since = previous.ContainsKey(id)
                ? sinceById?.GetValueOrDefault(id, defaultSince) ?? defaultSince
                : state?.OccurredAt ?? defaultSince;
            if (state?.ChangeType == LibraryChangeType.Deleted && state.OccurredAt >= since) continue;
            bool replacedSinceSnapshot = state is not null && state.OccurredAt >= since
                && state.ChangeType is LibraryChangeType.Added or LibraryChangeType.Updated or LibraryChangeType.Changed;
            long? size = replacedSinceSnapshot ? state!.SizeBytes : old.File.Size;
            if (state?.ChangeType == LibraryChangeType.Changed) size ??= old.File.Size;
            inferred.Add(new LibraryChangeHistoryEntry
            {
                SourceKey = $"snapshot-delete:{mediaKey}:{observedAt:O}",
                Kind = kind,
                InstanceId = instance.Id,
                InstanceName = instance.Name,
                MediaKey = mediaKey,
                OccurredAt = observedAt,
                ObservedSince = replacedSinceSnapshot ? state!.OccurredAt : since,
                ChangeType = LibraryChangeType.Deleted,
                IsInferred = true,
                EventType = "libraryFileMissing",
                DeletionReason = "MissingFromLibrary",
                EntityTitle = old.Title,
                SeriesTitle = old.SeriesTitle,
                FilePath = replacedSinceSnapshot ? state!.FilePath : old.File.Path ?? old.File.RelativePath,
                FileId = replacedSinceSnapshot ? state!.FileId : old.File.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SizeBytes = size,
                DeltaSizeBytes = -(size ?? 0),
                IsSizeDeltaKnown = size.HasValue
            });
        }
        return inferred;
    }

    private static long NumericId(string key) => long.TryParse(key.AsSpan(key.LastIndexOf(':') + 1), out long id) ? id : long.MaxValue;

    public static void CompleteBackfill(IEnumerable<LibraryChangeHistoryEntry> events, string instanceId)
    {
        foreach (LibraryChangeHistoryEntry entry in events.Where(e => e.InstanceId == instanceId))
            entry.NeedsHistoryBackfill = false;
    }
}
