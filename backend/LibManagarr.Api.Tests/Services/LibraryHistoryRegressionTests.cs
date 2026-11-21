using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class LibraryHistoryRegressionTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-05-01T12:00:00Z");
    private static readonly ArrInstanceSettings Instance = new() { Id = "main", Name = "Main" };

    [Theory]
    [InlineData(true, 60, -40)]
    [InlineData(false, 60, -40)]
    [InlineData(true, 140, 40)]
    [InlineData(false, 140, 40)]
    public void ReplacementUsesTheSizesOfTheOriginalEvents(bool sonarr, long newSize, long expected)
    {
        Dictionary<string, string> deleted = new() { ["Size"] = "100", ["Reason"] = "Upgrade" };
        Dictionary<string, string> added = new() { ["size"] = newSize.ToString(), ["FileId"] = "2" };
        var events = sonarr
            ? LibraryChangeHistoryBuilder.BuildSonarrEntries(Instance, Sonarr(newSize), Sonarr(100),
                [new() { Id = 9, EpisodeId = 1, Date = At, EventType = "episodeFileDeleted", Data = deleted },
                 new() { Id = 10, EpisodeId = 1, Date = At.AddSeconds(1), EventType = "downloadFolderImported", Data = added }])
            : LibraryChangeHistoryBuilder.BuildRadarrEntries(Instance, Radarr(newSize), Radarr(100),
                [new() { Id = 9, MovieId = 1, Date = At, EventType = "movieFileDeleted", Data = deleted },
                 new() { Id = 10, MovieId = 1, Date = At.AddSeconds(1), EventType = "downloadFolderImported", Data = added }]);
        LibraryChangeHistoryEntry update = Assert.Single(LibraryChangeHistoryNormalizer.Normalize(events));
        Assert.Equal(LibraryChangeType.Updated, update.ChangeType);
        Assert.Equal(expected, update.DeltaSizeBytes);
        Assert.True(update.IsSizeDeltaKnown);
        Assert.Equal("100", events[0].EventData!["Size"]);
    }

    [Theory]
    [InlineData("Upgrade", 600, true)]
    [InlineData("Manual", 1, false)]
    [InlineData(null, 301, false)]
    public void UpdateMatchingRespectsDeletionReasons(string? reason, int delay, bool expectedUpdate)
    {
        var deleted = Entry("radarr:9", LibraryChangeType.Deleted, At, 100);
        deleted.DeletionReason = reason;
        var imported = Entry("radarr:10", LibraryChangeType.Added, At.AddSeconds(delay), 60);
        var renamed = Entry("radarr:11", LibraryChangeType.Changed, At.AddMilliseconds(500), 60);
        var result = LibraryChangeHistoryNormalizer.Normalize([deleted, renamed, imported]);
        Assert.Equal(expectedUpdate, result.Any(e => e.ChangeType == LibraryChangeType.Updated));
        Assert.Single(result, e => e.ChangeType == LibraryChangeType.Changed);
    }

    [Fact]
    public void EqualTimestampsUseNumericHistoryIds()
    {
        var result = LibraryChangeHistoryNormalizer.Normalize([
            Entry("radarr:10", LibraryChangeType.Added, At, 60),
            Entry("radarr:9", LibraryChangeType.Deleted, At, 100)]);
        Assert.Equal(-40, Assert.Single(result).DeltaSizeBytes);
    }

    [Fact]
    public void ExplicitUpgradeCanPairDeletionRecordedJustAfterImport()
    {
        var deletion = Entry("radarr:11", LibraryChangeType.Deleted, At.AddSeconds(1), 100);
        deletion.DeletionReason = "Upgrade";
        var result = LibraryChangeHistoryNormalizer.Normalize([deletion, Entry("radarr:10", LibraryChangeType.Added, At, 60)]);
        Assert.Equal(-40, Assert.Single(result).DeltaSizeBytes);
    }

    [Fact]
    public void UnknownOldSizeDoesNotBecomeNoChange()
    {
        var deletion = Entry("radarr:9", LibraryChangeType.Deleted, At, null);
        var result = LibraryChangeHistoryNormalizer.Normalize([deletion, Entry("radarr:10", LibraryChangeType.Added, At.AddSeconds(1), 60)]);
        Assert.False(Assert.Single(result).IsSizeDeltaKnown);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SnapshotDiffFindsDeletionWithoutArrHistoryAndPreservesMetadata(bool sonarr)
    {
        var result = sonarr
            ? LibraryHistoryReconciler.InferSonarrDeletions(Instance, Sonarr(100), Sonarr(null), [])
            : LibraryHistoryReconciler.InferRadarrDeletions(Instance, Radarr(100), Radarr(null), []);
        var deletion = Assert.Single(result);
        Assert.Equal(-100, deletion.DeltaSizeBytes);
        Assert.Equal("For the Glory", deletion.EntityTitle);
        Assert.Equal("old.mkv", deletion.FilePath);
        Assert.True(deletion.IsInferred);
        Assert.Empty(sonarr
            ? LibraryHistoryReconciler.InferSonarrDeletions(Instance, Sonarr(100), Sonarr(null), result)
            : LibraryHistoryReconciler.InferRadarrDeletions(Instance, Radarr(100), Radarr(null), result));
    }

    [Fact]
    public void FirstSnapshotDoesNotInventDeletions()
    {
        Assert.Empty(LibraryHistoryReconciler.InferSonarrDeletions(Instance, null, Sonarr(null), []));
        Assert.Empty(LibraryHistoryReconciler.InferRadarrDeletions(Instance, null, Radarr(null), []));
    }

    [Fact]
    public void ReplayKeepsRawEventsWithoutDuplicatingTheirMergedUpdate()
    {
        var events = new[] { Entry("radarr:9", LibraryChangeType.Deleted, At, 100), Entry("radarr:10", LibraryChangeType.Added, At.AddSeconds(1), 60) };
        var first = LibraryHistoryReconciler.MergeEvents([], events);
        var second = LibraryHistoryReconciler.MergeEvents(first, events);
        Assert.Equal(2, second.Count);
        Assert.Equal(-40, Assert.Single(LibraryHistoryReconciler.Project(second)).DeltaSizeBytes);
    }

    [Fact]
    public void RecoveredEventsReplaceLegacyMergedUpdateOnlyOnce()
    {
        var events = new[] { Entry("radarr:9", LibraryChangeType.Deleted, At, 100), Entry("radarr:10", LibraryChangeType.Added, At.AddSeconds(1), 60) };
        var legacy = Assert.Single(LibraryChangeHistoryNormalizer.Normalize(events));
        legacy.IsLegacy = true;
        legacy.IsSizeDeltaKnown = false;
        legacy.DeltaSizeBytes = 0;
        Assert.Equal(-40, Assert.Single(LibraryHistoryReconciler.Project([legacy, .. events])).DeltaSizeBytes);
        Assert.Same(legacy, Assert.Single(LibraryHistoryReconciler.Project([legacy, events[1]])));
    }

    [Fact]
    public void LegacyReconciliationSeparatesMediaInstancesAndKindsAndPreservesPartialRecovery()
    {
        List<LibraryChangeHistoryEntry> history = [];
        List<LibraryChangeHistoryEntry> retained = [];
        for (int i = 0; i < 600; i++)
        {
            var deletion = Entry($"delete:{i}", LibraryChangeType.Deleted, At, 100);
            var import = Entry($"import:{i}", LibraryChangeType.Added, At.AddSeconds(1), 60);
            // Reuse media keys across instances and applications, just like their numeric IDs.
            foreach (var entry in new[] { deletion, import })
            {
                entry.MediaKey = $"medium:{i / 4}";
                entry.InstanceId = $"instance:{i % 2}";
                entry.Kind = i % 4 < 2 ? ArrApplicationKind.Sonarr : ArrApplicationKind.Radarr;
            }
            var legacy = Assert.Single(LibraryChangeHistoryNormalizer.Normalize([deletion, import]));
            legacy.IsLegacy = true;
            legacy.DeltaSizeBytes = 0;
            legacy.IsSizeDeltaKnown = false;
            history.Add(legacy);
            if (i % 3 == 0) history.AddRange([deletion, import]);
            else
            {
                retained.Add(legacy);
                if (i % 3 == 1) history.Add(import);
            }
        }

        var projected = LibraryHistoryReconciler.Project(history);
        Assert.Equal(600, projected.Count);
        Assert.Equal(200, projected.Count(e => !e.IsLegacy && e.DeltaSizeBytes == -40));
        Assert.Equal(400, projected.Count(e => e.IsLegacy));
        foreach (var legacy in retained) Assert.Contains(legacy, projected);
        // The journal itself must remain available for the next recovery attempt.
        Assert.Equal(600, history.Count(e => e.IsLegacy && e.DeltaSizeBytes == 0));
    }

    [Fact]
    public void ADelayedHistoryEventReplacesAnInferredDeletion()
    {
        var inferred = Assert.Single(LibraryHistoryReconciler.InferRadarrDeletions(Instance, Radarr(100), Radarr(null), []));
        var actual = Entry("radarr:9", LibraryChangeType.Deleted, At, 100);
        var merged = LibraryHistoryReconciler.MergeEvents([inferred], [actual]);
        Assert.Same(actual, Assert.Single(merged));
    }

    [Fact]
    public void RecoveredOriginalImportReplacesSeedButLaterReplacementKeepsIt()
    {
        var seed = Entry("seed:radarr:main:1", LibraryChangeType.Added, At, 100);
        var original = Entry("radarr:1", LibraryChangeType.Added, At.AddDays(-1), 100);
        var later = Entry("radarr:2", LibraryChangeType.Added, At.AddDays(1), 60);
        Assert.Same(original, Assert.Single(LibraryHistoryReconciler.MergeEvents([seed], [original])));
        Assert.Equal(2, LibraryHistoryReconciler.MergeEvents([seed], [later]).Count);
    }

    [Fact]
    public void FullSyncUsesEachSeriesObservationTimeAfterAPartialSync()
    {
        var previous = Sonarr(100);
        previous.LastFullSyncAt = At.AddMinutes(-1);
        previous.SyncedAt = At.AddSeconds(10);
        previous.SeriesSyncTimes[99] = At.AddSeconds(10);
        var deletion = Entry("sonarr:9", LibraryChangeType.Deleted, At, 100);
        deletion.Kind = ArrApplicationKind.Sonarr;
        deletion.MediaKey = "sonarr:main:1";
        Assert.Empty(LibraryHistoryReconciler.InferSonarrDeletions(Instance, previous, Sonarr(null), [deletion]));
    }

    [Fact]
    public void AnUpgradeDoesNotHideASubsequentUnrecordedDeletionInTheSameSync()
    {
        var deleted = Entry("radarr:9", LibraryChangeType.Deleted, At, 100);
        var imported = Entry("radarr:10", LibraryChangeType.Added, At.AddSeconds(1), 60);
        var inferred = Assert.Single(LibraryHistoryReconciler.InferRadarrDeletions(Instance, Radarr(100), Radarr(null), [deleted, imported]));
        Assert.True(inferred.IsInferred);
        Assert.Equal(-60, inferred.DeltaSizeBytes);
        var replayed = LibraryHistoryReconciler.MergeEvents([deleted, imported, inferred], [deleted, imported]);
        Assert.Equal(3, replayed.Count);
        Assert.Single(replayed, e => e.IsInferred);
    }

    [Theory]
    [InlineData(true, LibraryChangeType.Changed)]
    [InlineData(true, LibraryChangeType.Added)]
    [InlineData(false, LibraryChangeType.Updated)]
    public void AFirstSnapshotCanProveMissingFilesFromTheirRetainedHistory(bool sonarr, LibraryChangeType type)
    {
        var history = Entry("history:9", type, At, 100);
        history.Kind = sonarr ? ArrApplicationKind.Sonarr : ArrApplicationKind.Radarr;
        history.MediaKey = $"{(sonarr ? "sonarr" : "radarr")}:main:1";
        history.EntityTitle = "Missing episode";
        history.SeriesTitle = sonarr ? "Removed series" : null;
        history.FilePath = "removed.mkv";
        var deleted = Assert.Single(sonarr
            ? LibraryHistoryReconciler.InferSonarrDeletions(Instance, null, Sonarr(null), [history])
            : LibraryHistoryReconciler.InferRadarrDeletions(Instance, null, Radarr(null), [history]));
        Assert.Equal(-100, deleted.DeltaSizeBytes);
        Assert.Equal("Missing episode", deleted.EntityTitle);
        Assert.Equal(history.SeriesTitle, deleted.SeriesTitle);
        Assert.Equal("removed.mkv", deleted.FilePath);
        Assert.Equal(At.AddMinutes(1), deleted.OccurredAt);
        Assert.Equal(At, deleted.ObservedSince);
        Assert.True(deleted.IsInferred);
        Assert.Empty(sonarr
            ? LibraryHistoryReconciler.InferSonarrDeletions(Instance, null, Sonarr(null), [history, deleted])
            : LibraryHistoryReconciler.InferRadarrDeletions(Instance, null, Radarr(null), [history, deleted]));
    }

    [Fact]
    public void TargetedSyncCannotDeclareAnUnrelatedHistoricalEpisodeDeleted()
    {
        var unrelated = Entry("sonarr:9", LibraryChangeType.Changed, At, 100);
        unrelated.Kind = ArrApplicationKind.Sonarr;
        unrelated.MediaKey = "sonarr:main:99";
        Assert.Empty(LibraryHistoryReconciler.InferSonarrDeletions(Instance, null, Sonarr(null), [unrelated], seriesId: 1));
    }

    private static LibraryChangeHistoryEntry Entry(string key, LibraryChangeType type, DateTimeOffset at, long? size) => new()
    {
        SourceKey = key,
        Kind = ArrApplicationKind.Radarr,
        InstanceId = Instance.Id,
        MediaKey = "radarr:main:1",
        OccurredAt = at,
        ChangeType = type,
        SizeBytes = size,
        IsSizeDeltaKnown = size.HasValue,
        DeltaSizeBytes = type == LibraryChangeType.Deleted ? -(size ?? 0) : size ?? 0
    };

    private static SonarrInstanceSnapshot Sonarr(long? size) => new()
    {
        InstanceId = Instance.Id,
        SyncedAt = size.HasValue ? At.AddMinutes(-1) : At.AddMinutes(1),
        Series = size.HasValue ? [new() { SeriesId = 1, Title = "Series", Seasons = [new() { Episodes =
            [new() { EpisodeId = 1, HasFile = true, Title = "For the Glory", File = new() { Id = 2, Size = size, RelativePath = "old.mkv" } }] }] }] : []
    };

    private static RadarrInstanceSnapshot Radarr(long? size) => new()
    {
        InstanceId = Instance.Id,
        SyncedAt = size.HasValue ? At.AddMinutes(-1) : At.AddMinutes(1),
        Movies = size.HasValue ? [new() { MovieId = 1, HasFile = true, Title = "For the Glory", File = new() { Id = 2, Size = size, RelativePath = "old.mkv" } }] : []
    };
}
