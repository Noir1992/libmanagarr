using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class SonarrObservedHistoryTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-11-21T12:00:00Z");
    private static readonly ArrInstanceSettings Instance = new() { Id = "sonarr", Name = "Sonarr" };

    [Fact]
    public void RescannedFileGetsOneObservedAdditionWithoutAnImportEvent()
    {
        var before = Snapshot(At, false);
        var after = Snapshot(At.AddMinutes(1), true);
        var added = Assert.Single(LibraryHistoryReconciler.InferSonarrAdditions(Instance, before, after, []));
        Assert.Equal(LibraryChangeType.Added, added.ChangeType);
        Assert.Equal(100, added.DeltaSizeBytes);
        Assert.Equal("44", added.FileId);
        Assert.Equal("Special", added.EntityTitle);
        Assert.Equal(after.SyncedAt, added.OccurredAt);
        Assert.Equal(before.SyncedAt, added.ObservedSince);
        Assert.True(added.IsInferred);
        Assert.Empty(LibraryHistoryReconciler.InferSonarrAdditions(Instance, before, after, [added]));
        Assert.Empty(LibraryHistoryReconciler.InferSonarrAdditions(Instance, after, Snapshot(At.AddMinutes(2), true), [added]));
        Assert.Empty(LibraryHistorySeedBuilder.BuildSonarrSeeds(Instance, after, [added]));
    }

    [Fact]
    public void RecreatedLinkBalancesTheDeletionEvenWhenPreviousSnapshotHadAFile()
    {
        var deleted = History(LibraryChangeType.Deleted, At.AddSeconds(10));
        var added = Assert.Single(LibraryHistoryReconciler.InferSonarrAdditions(Instance,
            Snapshot(At, true), Snapshot(At.AddMinutes(1), true), [deleted]));
        Assert.Equal(0, LibraryHistoryReconciler.Project([deleted, added]).Sum(e => e.DeltaSizeBytes));
        Assert.Equal(2, LibraryHistoryReconciler.MergeEvents([deleted, added], [deleted]).Count);
    }

    [Fact]
    public void InitialInventoryAndUnrelatedSeriesAreNotInferredAsNewImports()
    {
        Assert.Empty(LibraryHistoryReconciler.InferSonarrAdditions(Instance, null, Snapshot(At, true), []));
        Assert.Empty(LibraryHistoryReconciler.InferSonarrAdditions(Instance,
            Snapshot(At, false), Snapshot(At.AddMinutes(1), true), [], seriesId: 99));
    }

    [Fact]
    public void RealImportPreventsAnInferredAdditionAndReplacesALaterRecoveredOne()
    {
        var before = Snapshot(At, false);
        var after = Snapshot(At.AddMinutes(1), true);
        var imported = History(LibraryChangeType.Added, At.AddSeconds(10));
        Assert.Empty(LibraryHistoryReconciler.InferSonarrAdditions(Instance, before, after, [imported]));
        var observed = Assert.Single(LibraryHistoryReconciler.InferSonarrAdditions(Instance, before, after, []));
        Assert.Same(imported, Assert.Single(LibraryHistoryReconciler.MergeEvents([observed], [imported])));
        imported.FileId = "45";
        Assert.Equal(2, LibraryHistoryReconciler.MergeEvents([observed], [imported]).Count);
    }

    private static LibraryChangeHistoryEntry History(LibraryChangeType type, DateTimeOffset at) => new()
    {
        SourceKey = "sonarr:1", Kind = ArrApplicationKind.Sonarr, InstanceId = Instance.Id,
        MediaKey = "sonarr:sonarr:5", ChangeType = type, OccurredAt = at,
        FileId = "44", SizeBytes = 100, DeltaSizeBytes = type == LibraryChangeType.Deleted ? -100 : 100
    };

    private static SonarrInstanceSnapshot Snapshot(DateTimeOffset at, bool hasFile) => new()
    {
        InstanceId = Instance.Id, SyncedAt = at,
        Series = [new() { SeriesId = 12, Title = "Series", Seasons = [new() { Episodes = [new()
        {
            EpisodeId = 5, Title = "Special", HasFile = hasFile,
            File = new() { Id = hasFile ? 44 : null, RelativePath = "Specials/special.mkv", Size = hasFile ? 100 : null }
        }] }] }]
    };
}
