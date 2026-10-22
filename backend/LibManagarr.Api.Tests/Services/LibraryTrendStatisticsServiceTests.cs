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
