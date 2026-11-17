using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class LibraryTrendStatisticsServiceTests
{
    private readonly LibraryTrendStatisticsService service = new();

    [Fact]
    public void Build_ShouldAggregateChangeHistoryAcrossInstances_AndExposeDeltas()
    {
        LibraryChangeHistoryEntry[] historyEntries =
        [
            new()
            {
                SourceKey = "sonarr:1",
                Kind = ArrApplicationKind.Sonarr,
                InstanceId = "sonarr-1",
                InstanceName = "Sonarr Main",
                OccurredAt = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
                ChangeType = LibraryChangeType.Added,
                EntityTitle = "Episode 1",
                DeltaSizeBytes = 100
            },
            new()
            {
                SourceKey = "sonarr:2",
                Kind = ArrApplicationKind.Sonarr,
                InstanceId = "sonarr-1",
                InstanceName = "Sonarr Main",
                OccurredAt = new DateTimeOffset(2026, 5, 2, 10, 0, 0, TimeSpan.Zero),
                ChangeType = LibraryChangeType.Changed,
                EntityTitle = "Episode 1",
                DeltaSizeBytes = 50
            },
            new()
            {
                SourceKey = "radarr:1",
                Kind = ArrApplicationKind.Radarr,
                InstanceId = "radarr-1",
                InstanceName = "Radarr Main",
                OccurredAt = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
                ChangeType = LibraryChangeType.Added,
                EntityTitle = "Dune",
                DeltaSizeBytes = 300
            },
            new()
            {
                SourceKey = "radarr:2",
                Kind = ArrApplicationKind.Radarr,
                InstanceId = "radarr-1",
                InstanceName = "Radarr Main",
                OccurredAt = new DateTimeOffset(2026, 5, 3, 10, 0, 0, TimeSpan.Zero),
                ChangeType = LibraryChangeType.Deleted,
                EntityTitle = "Dune",
                DeltaSizeBytes = -300
            }
        ];
        AppSettings settings = new()
        {
            SonarrSnapshots = [SonarrSnapshot("sonarr-1", (true, 100), (true, 50))],
            RadarrSnapshots = [RadarrSnapshot("radarr-1", (false, 0))]
        };

        LibraryHistoryStatisticsResponse response = service.Build(
            settings,
            historyEntries,
            null,
            null,
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 3, 23, 59, 59, TimeSpan.Zero));

        Assert.NotEmpty(response.Points);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), response.Points[0].OccurredAt);
        Assert.Equal(2, response.Points[0].MediaFileCount);
        Assert.Equal(400, response.Points[0].TotalSizeBytes);
        Assert.Equal(2, response.Points[0].DeltaMediaFileCount);
        Assert.Equal(400, response.Points[0].DeltaSizeBytes);
        Assert.Equal(2, response.TotalMediaFileCount);
        Assert.Equal(150, response.TotalSizeBytes);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), response.AvailableFrom);
        Assert.Equal(new DateTimeOffset(2026, 5, 3, 10, 0, 0, TimeSpan.Zero), response.AvailableTo);
        Assert.Equal(4, response.History.Count);
    }

    [Fact]
    public void Build_ShouldRespectInstanceFilters()
    {
        LibraryChangeHistoryEntry[] historyEntries =
        [
            new()
            {
                SourceKey = "sonarr:1",
                Kind = ArrApplicationKind.Sonarr,
                InstanceId = "sonarr-1",
                OccurredAt = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
                ChangeType = LibraryChangeType.Added,
                EntityTitle = "Episode 1",
                DeltaSizeBytes = 100
            },
            new()
            {
                SourceKey = "radarr:1",
                Kind = ArrApplicationKind.Radarr,
                InstanceId = "radarr-1",
                OccurredAt = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
                ChangeType = LibraryChangeType.Added,
                EntityTitle = "Dune",
                DeltaSizeBytes = 200
            }
        ];
        AppSettings settings = new()
        {
            SonarrSnapshots = [SonarrSnapshot("sonarr-1", (true, 100)), SonarrSnapshot("sonarr-2", (true, 999))],
            RadarrSnapshots = [RadarrSnapshot("radarr-1", (true, 500))]
        };

        HashSet<string> sonarrFilter = ["sonarr-1"];
        LibraryHistoryStatisticsResponse response = service.Build(
            settings,
            historyEntries,
            sonarrFilter,
            new HashSet<string>(),
            null,
            null);

        Assert.Single(response.Points);
        Assert.Equal(1, response.Points[0].MediaFileCount);
        Assert.Equal(100, response.Points[0].TotalSizeBytes);
        Assert.Equal(1, response.TotalMediaFileCount);
        Assert.Equal(100, response.TotalSizeBytes);
    }

    [Fact]
    public void Build_ShouldCollapseMultipleChangesFromTheSameDayIntoOnePoint()
    {
        LibraryChangeHistoryEntry[] historyEntries =
        [
            new()
            {
                SourceKey = "sonarr:1",
                Kind = ArrApplicationKind.Sonarr,
                InstanceId = "sonarr-1",
                OccurredAt = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
                ChangeType = LibraryChangeType.Added,
                EntityTitle = "Episode 1",
                DeltaSizeBytes = 100
            },
            new()
            {
                SourceKey = "sonarr:2",
                Kind = ArrApplicationKind.Sonarr,
                InstanceId = "sonarr-1",
                OccurredAt = new DateTimeOffset(2026, 5, 1, 18, 30, 0, TimeSpan.Zero),
                ChangeType = LibraryChangeType.Deleted,
                EntityTitle = "Episode 2",
                DeltaSizeBytes = -50
            },
            new()
            {
                SourceKey = "radarr:1",
                Kind = ArrApplicationKind.Radarr,
                InstanceId = "radarr-1",
                OccurredAt = new DateTimeOffset(2026, 5, 1, 20, 15, 0, TimeSpan.Zero),
                ChangeType = LibraryChangeType.Added,
                EntityTitle = "Dune",
                DeltaSizeBytes = 300
            }
        ];
        AppSettings settings = new()
        {
            RadarrSnapshots = [RadarrSnapshot("radarr-1", (true, 350), (false, 0))]
        };

        LibraryHistoryStatisticsResponse response = service.Build(settings, historyEntries, null, null, null, null);

        Assert.Single(response.Points);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), response.Points[0].OccurredAt);
        Assert.Equal(1, response.Points[0].MediaFileCount);
        Assert.Equal(350, response.Points[0].TotalSizeBytes);
        Assert.Equal(1, response.Points[0].DeltaMediaFileCount);
        Assert.Equal(350, response.Points[0].DeltaSizeBytes);
        Assert.Equal(1, response.TotalMediaFileCount);
        Assert.Equal(350, response.TotalSizeBytes);
    }

    [Fact]
    public void Build_ShouldKeepOverallTotalsWhenZoomingIntoADateRange()
    {
        LibraryChangeHistoryEntry[] historyEntries =
        [
            new()
            {
                SourceKey = "sonarr:1",
                Kind = ArrApplicationKind.Sonarr,
                InstanceId = "sonarr-1",
                OccurredAt = new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
                ChangeType = LibraryChangeType.Added,
                EntityTitle = "Episode 1",
                DeltaSizeBytes = 100
            },
            new()
            {
                SourceKey = "sonarr:2",
                Kind = ArrApplicationKind.Sonarr,
                InstanceId = "sonarr-1",
                OccurredAt = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
                ChangeType = LibraryChangeType.Added,
                EntityTitle = "Episode 2",
                DeltaSizeBytes = 200
            }
        ];
        AppSettings settings = new()
        {
            SonarrSnapshots = [SonarrSnapshot("sonarr-1", (true, 100), (true, 200))]
        };

        LibraryHistoryStatisticsResponse response = service.Build(
            settings,
            historyEntries,
            null,
            null,
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 1, 23, 59, 59, TimeSpan.Zero));

        Assert.Single(response.Points);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), response.Points[0].OccurredAt);
        Assert.Equal(2, response.Points[0].MediaFileCount);
        Assert.Equal(300, response.Points[0].TotalSizeBytes);
        Assert.Equal(2, response.TotalMediaFileCount);
        Assert.Equal(300, response.TotalSizeBytes);
    }

    [Fact]
    public void Build_ShouldTakeTotalsFromCurrentSnapshotsWhenHistoryIsIncomplete()
    {
        LibraryChangeHistoryEntry[] historyEntries =
        [
            new()
            {
                SourceKey = "sonarr:1",
                Kind = ArrApplicationKind.Sonarr,
                InstanceId = "sonarr-1",
                InstanceName = "Sonarr Main",
                OccurredAt = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
                ChangeType = LibraryChangeType.Added,
                EntityTitle = "Episode 3",
                DeltaSizeBytes = 100
            }
        ];
        AppSettings settings = new()
        {
            SonarrSnapshots = [SonarrSnapshot("sonarr-1", (true, 100), (true, 200), (true, 300), (false, 0))]
        };

        LibraryHistoryStatisticsResponse response = service.Build(settings, historyEntries, null, null, null, null);

        Assert.Single(response.Points);
        Assert.Equal(1, response.Points[0].MediaFileCount);
        Assert.Equal(3, response.TotalMediaFileCount);
        Assert.Equal(600, response.TotalSizeBytes);
    }

    [Fact]
    public void FreshHistoryStartingWithRenameIncludesTheExistingFileInStorageChart()
    {
        DateTimeOffset at = DateTimeOffset.Parse("2026-05-01T10:00:00Z");
        var rename = ChartEntry("sonarr:1", ArrApplicationKind.Sonarr, "sonarr-1", "sonarr:sonarr-1:1", at, LibraryChangeType.Changed, 100, 0);
        var snapshot = SonarrSnapshot("sonarr-1", (true, 100));
        snapshot.SyncedAt = at.AddDays(1);
        var response = service.Build(new() { SonarrSnapshots = [snapshot] }, [rename], null, null, null, null);

        Assert.Equal(100, Assert.Single(response.Points).TotalSizeBytes);
        Assert.Equal(100, response.TotalSizeBytes);
        Assert.Same(rename, Assert.Single(response.History));
        Assert.Equal(0, rename.DeltaSizeBytes);
        Assert.False(response.Points[0].IsSizeDeltaKnown); // Original import date was not recorded.
    }

    [Fact]
    public void FreshHistoryStartingWithUpgradeReconstructsTheMissingOriginalFile()
    {
        DateTimeOffset at = DateTimeOffset.Parse("2026-03-20T08:25:00Z");
        var deleted = ChartEntry("radarr:206", ArrApplicationKind.Radarr, "radarr-1", "radarr:radarr-1:1", at,
            LibraryChangeType.Deleted, 10546568180, -10546568180);
        var imported = ChartEntry("radarr:207", ArrApplicationKind.Radarr, "radarr-1", "radarr:radarr-1:1", at,
            LibraryChangeType.Added, 8141503995, 8141503995);
        var history = LibraryChangeHistoryNormalizer.Normalize([deleted, imported]);
        var snapshot = RadarrSnapshot("radarr-1", (true, 8141503995));
        snapshot.SyncedAt = at.AddDays(1);
        var response = service.Build(new() { RadarrSnapshots = [snapshot] }, history, null, null, null, null);

        Assert.Equal(8141503995, Assert.Single(response.Points).TotalSizeBytes);
        Assert.Equal(response.TotalSizeBytes, response.Points[^1].TotalSizeBytes);
        Assert.Equal(-2405064185, Assert.Single(response.History).DeltaSizeBytes);
        Assert.Equal(LibraryChangeType.Updated, response.History[0].ChangeType);
    }

    [Fact]
    public void ARecordedInitialImportIsNotCountedAgainWhenUpgradesFollow()
    {
        DateTimeOffset at = DateTimeOffset.Parse("2026-05-01T10:00:00Z");
        var history = new[]
        {
            ChartEntry("radarr:1", ArrApplicationKind.Radarr, "radarr-1", "radarr:radarr-1:1", at, LibraryChangeType.Added, 100, 100),
            ChartEntry("updated:1", ArrApplicationKind.Radarr, "radarr-1", "radarr:radarr-1:1", at.AddDays(1), LibraryChangeType.Updated, 60, -40)
        };
        var snapshot = RadarrSnapshot("radarr-1", (true, 60));
        snapshot.SyncedAt = at.AddDays(3);
        var response = service.Build(new() { RadarrSnapshots = [snapshot] }, history, null, null, null, null);

        Assert.Equal(new long[] { 100, 60 }, response.Points.Select(point => point.TotalSizeBytes));
        Assert.All(response.Points, point => Assert.True(point.IsSizeDeltaKnown));
    }

    [Fact]
    public void SnapshotReconciliationIsAppliedAtItsObservationDateWithoutChangingHistoricalData()
    {
        DateTimeOffset at = DateTimeOffset.Parse("2026-05-01T10:00:00Z");
        var imported = ChartEntry("sonarr:1", ArrApplicationKind.Sonarr, "sonarr-1", "sonarr:sonarr-1:1", at, LibraryChangeType.Added, 100, 100);
        var snapshot = SonarrSnapshot("sonarr-1", (true, 100), (true, 200));
        snapshot.SyncedAt = at.AddDays(2);
        AppSettings settings = new() { SonarrSnapshots = [snapshot] };
        var response = service.Build(settings, [imported], null, null, null, null);
        Assert.Equal(new long[] { 100, 300 }, response.Points.Select(point => point.TotalSizeBytes));
        Assert.Equal(new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero), response.Points[^1].OccurredAt);
        Assert.False(response.Points[^1].IsSizeDeltaKnown);
        Assert.Equal(300, response.TotalSizeBytes);
        Assert.Same(imported, Assert.Single(response.History));
        var historical = service.Build(settings, [imported], null, null, null, at.AddDays(1));
        Assert.Equal(100, Assert.Single(historical.Points).TotalSizeBytes);
        Assert.Equal(300, historical.TotalSizeBytes);
    }

    [Fact]
    public void UnknownUpgradeSizeIsNotInventedAndTheCurrentChartStillMatchesTheSnapshot()
    {
        DateTimeOffset at = DateTimeOffset.Parse("2026-05-01T10:00:00Z");
        var update = ChartEntry("updated:1", ArrApplicationKind.Radarr, "radarr-1", "radarr:radarr-1:1", at, LibraryChangeType.Updated, 60, 0);
        update.IsSizeDeltaKnown = false;
        var snapshot = RadarrSnapshot("radarr-1", (true, 60));
        snapshot.SyncedAt = at.AddDays(1);
        var response = service.Build(new() { RadarrSnapshots = [snapshot] }, [update], null, null, null, null);
        Assert.Equal(0, response.Points[0].TotalSizeBytes);
        Assert.Equal(60, response.Points[^1].TotalSizeBytes);
        Assert.False(response.Points[^1].IsSizeDeltaKnown);
        Assert.False(update.IsSizeDeltaKnown);
    }

    [Fact]
    public void StorageBaselinesAndSnapshotMeasurementsRespectInstanceFilters()
    {
        DateTimeOffset at = DateTimeOffset.Parse("2026-05-01T10:00:00Z");
        var sonarr = SonarrSnapshot("sonarr-1", (true, 100));
        sonarr.SyncedAt = at.AddDays(1);
        var radarr = RadarrSnapshot("radarr-1", (true, 300));
        radarr.SyncedAt = at.AddDays(2);
        AppSettings settings = new() { SonarrSnapshots = [sonarr], RadarrSnapshots = [radarr] };
        var history = new[]
        {
            ChartEntry("sonarr:1", ArrApplicationKind.Sonarr, "sonarr-1", "sonarr:sonarr-1:1", at, LibraryChangeType.Changed, 100, 0),
            ChartEntry("radarr:1", ArrApplicationKind.Radarr, "radarr-1", "radarr:radarr-1:1", at, LibraryChangeType.Updated, 200, -100)
        };
        var all = service.Build(settings, history, null, null, null, null);
        Assert.Equal(all.TotalSizeBytes, all.Points[^1].TotalSizeBytes);
        var filtered = service.Build(settings, history, new HashSet<string> { "sonarr-1" }, new HashSet<string>(), null, null);
        Assert.Equal(100, Assert.Single(filtered.Points).TotalSizeBytes);
        Assert.Equal(100, filtered.TotalSizeBytes);
        Assert.Single(filtered.History);
    }

    [Fact]
    public void SameTimestampRenameDoesNotObscureTheOldSizeOfTheUpgrade()
    {
        DateTimeOffset at = DateTimeOffset.Parse("2026-05-01T10:00:00Z");
        var update = ChartEntry("updated:1", ArrApplicationKind.Radarr, "radarr-1", "radarr:radarr-1:1", at, LibraryChangeType.Updated, 60, -40);
        update.HistoryId = 9;
        var rename = ChartEntry("radarr:10", ArrApplicationKind.Radarr, "radarr-1", "radarr:radarr-1:1", at, LibraryChangeType.Changed, 60, 0);
        rename.HistoryId = 10;
        var snapshot = RadarrSnapshot("radarr-1", (true, 60));
        snapshot.SyncedAt = at.AddDays(1);
        var response = service.Build(new() { RadarrSnapshots = [snapshot] }, [rename, update], null, null, null, null);
        Assert.Equal(60, Assert.Single(response.Points).TotalSizeBytes);
    }

    private static LibraryChangeHistoryEntry ChartEntry(string sourceKey, ArrApplicationKind kind, string instanceId,
        string mediaKey, DateTimeOffset at, LibraryChangeType type, long size, long delta) => new()
        {
            SourceKey = sourceKey, Kind = kind, InstanceId = instanceId, MediaKey = mediaKey,
            OccurredAt = at, ChangeType = type, SizeBytes = size, DeltaSizeBytes = delta
        };

    private static SonarrInstanceSnapshot SonarrSnapshot(string instanceId, params (bool HasFile, long Size)[] files)
    {
        return new SonarrInstanceSnapshot
        {
            InstanceId = instanceId,
            Series =
            [
                new SonarrSeriesSnapshot
                {
                    SeriesId = 1,
                    Title = "Series",
                    Seasons =
                    [
                        new SonarrSeasonSnapshot
                        {
                            SeasonNumber = 1,
                            Episodes =
                            [
                                .. files.Select((file, index) => new SonarrEpisodeSnapshot
                                {
                                    EpisodeId = index + 1,
                                    Title = $"Episode {index + 1}",
                                    HasFile = file.HasFile,
                                    File = new MediaFileSnapshot { Size = file.HasFile ? file.Size : null }
                                })
                            ]
                        }
                    ]
                }
            ]
        };
    }

    private static RadarrInstanceSnapshot RadarrSnapshot(string instanceId, params (bool HasFile, long Size)[] files)
    {
        return new RadarrInstanceSnapshot
        {
            InstanceId = instanceId,
            Movies =
            [
                .. files.Select((file, index) => new RadarrMovieSnapshot
                {
                    MovieId = index + 1,
                    Title = $"Movie {index + 1}",
                    HasFile = file.HasFile,
                    File = new MediaFileSnapshot { Size = file.HasFile ? file.Size : null }
                })
            ]
        };
    }
}
