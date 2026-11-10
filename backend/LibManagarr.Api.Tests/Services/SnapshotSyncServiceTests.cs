using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class SnapshotSyncServiceTests
{
    private readonly InMemoryHistoryStore historyStore = new();

    [Fact]
    public async Task SyncAsync_ShouldPersistSonarrSnapshot_ForExistingInstance()
    {
        InMemorySettingsService settingsService = new(new AppSettings
        {
            SonarrInstances =
            [
                new ArrInstanceSettings
                {
                    Id = "sonarr-main",
                    Name = "Sonarr Main",
                    BaseUrl = "http://sonarr.test",
                    ApiKey = "secret"
                }
            ]
        });
        SnapshotSyncService service = new(
            settingsService,
            historyStore,
            new SonarrClient(new HttpClient(new StubHttpMessageHandler(
                new Dictionary<string, object?>
                {
                    ["/api/v3/qualityprofile"] =
                    new object[]
                    {
                        new
                        {
                            id = 3,
                            name = "HD-1080p",
                            upgradeUntilCustomFormatScore = 10
                        }
                    },
                    ["/api/v3/tag"] = Array.Empty<object>(),
                    ["/api/v3/series"] =
                    new object[]
                    {
                        new
                        {
                            id = 11,
                            title = "Andor",
                            titleSlug = "andor",
                            qualityProfileId = 3,
                            status = "continuing"
                        }
                    },
                    ["/api/v3/episode?seriesId=11"] =
                    new object[]
                    {
                        new
                        {
                            id = 501,
                            title = "Kassa",
                            seasonNumber = 1,
                            episodeNumber = 1,
                            hasFile = true,
                            episodeFileId = 101,
                            runtime = 42
                        }
                    },
                    ["/api/v3/episodefile?seriesId=11"] =
                    new object[]
                    {
                        new
                        {
                            id = 101,
                            customFormatScore = 4,
                            size = 123456789L,
                            releaseGroup = "GROUP",
                            mediaInfo = new
                            {
                                runTime = 2520
                            }
                        }
                    },
                    ["/api/v3/history/since"] =
                    new object[]
                    {
                        new
                        {
                            id = 9001,
                            episodeId = 501,
                            seriesId = 11,
                            sourceTitle = "Kassa",
                            date = "2026-05-01T10:00:00+00:00",
                            eventType = "downloadFolderImported",
                            data = new Dictionary<string, string>
                            {
                                ["path"] = "/media/andor/S01E01.mkv"
                            }
                        }
                    }
                }))),
            CreateUnusedRadarrClient(),
            new SymlinkMatchingRequests(),
            NullLogger<SnapshotSyncService>.Instance);

        SyncInstanceResult result = await service.SyncAsync(
            new SyncInstanceRequest
            {
                Kind = ArrApplicationKind.Sonarr,
                InstanceId = "sonarr-main"
            },
            CancellationToken.None);

        SonarrInstanceSnapshot snapshot = Assert.Single(settingsService.CurrentSettings.SonarrSnapshots);
        SonarrSeriesSnapshot series = Assert.Single(snapshot.Series);
        SonarrSeasonSnapshot season = Assert.Single(series.Seasons);
        SonarrEpisodeSnapshot episode = Assert.Single(season.Episodes);

        Assert.Equal("Sonarr data synchronized.", result.Message);
        Assert.Equal(1, result.ItemCount);
        Assert.NotNull(result.SyncedAt);
        Assert.Equal("sonarr-main", snapshot.InstanceId);
        Assert.Equal("Sonarr Main", snapshot.InstanceName);
        Assert.Equal("Andor", series.Title);
        Assert.Equal("HD-1080p", series.Profile.Name);
        Assert.Equal(10, series.Profile.UpgradeUntilCustomFormatScore);
        Assert.Equal("continuing", series.Status);
        Assert.Equal("Kassa", episode.Title);
        Assert.Equal(501, episode.EpisodeId);
        Assert.Equal(4, episode.File.CustomFormatScore);
        Assert.Equal(TimeSpan.FromMinutes(42), episode.File.Duration);
        Assert.Single(historyStore.SnapshotHistory);
        Assert.Single(historyStore.SonarrLibraryHistory);
        Assert.Equal(1, settingsService.SaveCount);
    }

    [Fact]
    public async Task SyncAsync_ShouldPersistRadarrSnapshot_ForExistingInstance()
    {
        InMemorySettingsService settingsService = new(new AppSettings
        {
            RadarrInstances =
            [
                new ArrInstanceSettings
                {
                    Id = "radarr-main",
                    Name = "Radarr Main",
                    BaseUrl = "http://radarr.test",
                    ApiKey = "secret"
                }
            ]
        });
        SnapshotSyncService service = new(
            settingsService,
            historyStore,
            CreateUnusedSonarrClient(),
            new RadarrClient(new HttpClient(new StubHttpMessageHandler(
                new Dictionary<string, object?>
                {
                    ["/api/v3/qualityprofile"] =
                    new object[]
                    {
                        new
                        {
                            id = 5,
                            name = "UHD",
                            upgradeUntilCustomFormatScore = 20
                        }
                    },
                    ["/api/v3/movie"] =
                    new object[]
                    {
                        new
                        {
                            id = 21,
                            title = "Dune",
                            titleSlug = "dune",
                            qualityProfileId = 5,
                            movieFileId = 210,
                            hasFile = true,
                            status = "released"
                        }
                    },
                    ["/api/v3/moviefile?movieId=21"] =
                    new object[]
                    {
                        new
                        {
                            id = 210,
                            movieId = 21,
                            customFormatScore = 12,
                            size = 987654321L,
                            releaseGroup = "SCENE"
                        }
                    },
                    ["/api/v3/history/since"] =
                    new object[]
                    {
                        new
                        {
                            id = 9101,
                            movieId = 21,
                            sourceTitle = "Dune",
                            date = "2026-05-01T10:00:00+00:00",
                            eventType = "downloadFolderImported",
                            data = new Dictionary<string, string>
                            {
                                ["path"] = "/media/dune.mkv"
                            }
                        }
                    }
                }))),
            new SymlinkMatchingRequests(),
            NullLogger<SnapshotSyncService>.Instance);

        SyncInstanceResult result = await service.SyncAsync(
            new SyncInstanceRequest
            {
                Kind = ArrApplicationKind.Radarr,
                InstanceId = "radarr-main"
            },
            CancellationToken.None);

        RadarrInstanceSnapshot snapshot = Assert.Single(settingsService.CurrentSettings.RadarrSnapshots);
        RadarrMovieSnapshot movie = Assert.Single(snapshot.Movies);

        Assert.Equal("Radarr data synchronized.", result.Message);
        Assert.Equal(1, result.ItemCount);
        Assert.NotNull(result.SyncedAt);
        Assert.Equal("radarr-main", snapshot.InstanceId);
        Assert.Equal("Radarr Main", snapshot.InstanceName);
        Assert.Equal("Dune", movie.Title);
        Assert.Equal("UHD", movie.Profile.Name);
        Assert.Equal(20, movie.Profile.UpgradeUntilCustomFormatScore);
        Assert.Equal("released", movie.Status);
        Assert.Equal(12, movie.File.CustomFormatScore);
        Assert.Single(historyStore.SnapshotHistory);
        Assert.Single(historyStore.RadarrLibraryHistory);
        Assert.Equal(1, settingsService.SaveCount);
    }

    [Fact]
    public async Task SyncAsync_ShouldBootstrapLibraryHistory_WhenSnapshotsExistButHistoryIsMissing()
    {
        DateTimeOffset previousSyncedAt = new(2026, 4, 20, 8, 0, 0, TimeSpan.Zero);
        InMemorySettingsService settingsService = new(new AppSettings
        {
            SonarrInstances =
            [
                new ArrInstanceSettings
                {
                    Id = "sonarr-main",
                    Name = "Sonarr Main",
                    BaseUrl = "http://sonarr.test",
                    ApiKey = "secret"
                }
            ],
            SonarrSnapshots =
            [
                new SonarrInstanceSnapshot
                {
                    InstanceId = "sonarr-main",
                    InstanceName = "Sonarr Main",
                    SyncedAt = previousSyncedAt
                }
            ]
        });

        SnapshotSyncService service = new(
            settingsService,
            historyStore,
            new SonarrClient(new HttpClient(new StubHttpMessageHandler(
                new Dictionary<string, object?>
                {
                    ["/api/v3/qualityprofile"] =
                    new object[]
                    {
                        new
                        {
                            id = 3,
                            name = "HD-1080p",
                            upgradeUntilCustomFormatScore = 10
                        }
                    },
                    ["/api/v3/tag"] = Array.Empty<object>(),
                    ["/api/v3/series"] =
                    new object[]
                    {
                        new
                        {
                            id = 11,
                            title = "Andor",
                            titleSlug = "andor",
                            qualityProfileId = 3,
                            status = "continuing"
                        }
                    },
                    ["/api/v3/episode?seriesId=11"] =
                    new object[]
                    {
                        new
                        {
                            id = 501,
                            title = "Kassa",
                            seasonNumber = 1,
                            episodeNumber = 1,
                            hasFile = true,
                            episodeFileId = 101,
                            runtime = 42
                        }
                    },
                    ["/api/v3/episodefile?seriesId=11"] =
                    new object[]
                    {
                        new
                        {
                            id = 101,
                            customFormatScore = 4,
                            size = 123456789L,
                            releaseGroup = "GROUP",
                            dateAdded = "2026-04-01T10:00:00+00:00",
                            mediaInfo = new
                            {
                                runTime = 2520
                            }
                        }
                    },
                    ["/api/v3/history/since"] =
                    new object[]
                    {
                        new
                        {
                            id = 9001,
                            episodeId = 501,
                            seriesId = 11,
                            sourceTitle = "Kassa",
                            date = "2026-04-05T10:00:00+00:00",
                            eventType = "downloadFolderImported",
                            data = new Dictionary<string, string>
                            {
                                ["path"] = "/media/andor/S01E01.mkv"
                            }
                        }
                    }
                }))),
            CreateUnusedRadarrClient(),
            new SymlinkMatchingRequests(),
            NullLogger<SnapshotSyncService>.Instance);

        await service.SyncAsync(
            new SyncInstanceRequest
            {
                Kind = ArrApplicationKind.Sonarr,
                InstanceId = "sonarr-main"
            },
            CancellationToken.None);

        Assert.Single(historyStore.SonarrLibraryHistory);
        Assert.Equal("sonarr:9001", historyStore.SonarrLibraryHistory[0].SourceKey);
    }

    [Fact]
    public async Task SyncAsync_ShouldMergeDeleteAndAddIntoUpdated_WhenTheyAreWithinFiveMinutes()
    {
        InMemorySettingsService settingsService = new(new AppSettings
        {
            SonarrInstances =
            [
                new ArrInstanceSettings
                {
                    Id = "sonarr-main",
                    Name = "Sonarr Main",
                    BaseUrl = "http://sonarr.test",
                    ApiKey = "secret"
                }
            ]
        });
        historyStore.SonarrLibraryHistory.Add(new LibraryChangeHistoryEntry
        {
            SourceKey = "sonarr:delete-1",
            Kind = ArrApplicationKind.Sonarr,
            InstanceId = "sonarr-main",
            InstanceName = "Sonarr Main",
            OccurredAt = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
            MediaKey = "sonarr:sonarr-main:501",
            ChangeType = LibraryChangeType.Deleted,
            EntityTitle = "Kassa",
            SizeBytes = 100,
            DeltaSizeBytes = -100
        });

        SnapshotSyncService service = new(
            settingsService,
            historyStore,
            new SonarrClient(new HttpClient(new StubHttpMessageHandler(
                new Dictionary<string, object?>
                {
                    ["/api/v3/qualityprofile"] =
                    new object[]
                    {
                        new
                        {
                            id = 3,
                            name = "HD-1080p",
                            upgradeUntilCustomFormatScore = 10
                        }
                    },
                    ["/api/v3/tag"] = Array.Empty<object>(),
                    ["/api/v3/series"] =
                    new object[]
                    {
                        new
                        {
                            id = 11,
                            title = "Andor",
                            titleSlug = "andor",
                            qualityProfileId = 3,
                            status = "continuing"
                        }
                    },
                    ["/api/v3/episode?seriesId=11"] =
                    new object[]
                    {
                        new
                        {
                            id = 501,
                            title = "Kassa",
                            seasonNumber = 1,
                            episodeNumber = 1,
                            hasFile = true,
                            episodeFileId = 101,
                            runtime = 42
                        }
                    },
                    ["/api/v3/episodefile?seriesId=11"] =
                    new object[]
                    {
                        new
                        {
                            id = 101,
                            customFormatScore = 4,
                            size = 120L,
                            releaseGroup = "GROUP",
                            mediaInfo = new
                            {
                                runTime = 2520
                            }
                        }
                    },
                    ["/api/v3/history/since"] =
                    new object[]
                    {
                        new
                        {
                            id = 9002,
                            episodeId = 501,
                            seriesId = 11,
                            sourceTitle = "Kassa",
                            date = "2026-05-01T10:03:00+00:00",
                            eventType = "downloadFolderImported",
                            data = new Dictionary<string, string>
                            {
                                ["path"] = "/media/andor/S01E01.mkv"
                            }
                        }
                    }
                }))),
            CreateUnusedRadarrClient(),
            new SymlinkMatchingRequests(),
            NullLogger<SnapshotSyncService>.Instance);

        await service.SyncAsync(
            new SyncInstanceRequest
            {
                Kind = ArrApplicationKind.Sonarr,
                InstanceId = "sonarr-main"
            },
            CancellationToken.None);

        LibraryChangeHistoryEntry entry = Assert.Single(historyStore.SonarrLibraryHistory);
        Assert.Equal(LibraryChangeType.Updated, entry.ChangeType);
        Assert.Equal("sonarr:sonarr-main:501", entry.MediaKey);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 10, 3, 0, TimeSpan.Zero), entry.OccurredAt);
        Assert.Equal(20, entry.DeltaSizeBytes);
    }

    [Fact]
    public async Task SyncAsync_ShouldSeedLibraryHistory_WhenArrHistoryEndsBeforeFileImports()
    {
        InMemorySettingsService settingsService = new(new AppSettings
        {
            SonarrInstances =
            [
                new ArrInstanceSettings
                {
                    Id = "sonarr-main",
                    Name = "Sonarr Main",
                    BaseUrl = "http://sonarr.test",
                    ApiKey = "secret"
                }
            ]
        });
        SnapshotSyncService service = new(
            settingsService,
            historyStore,
            new SonarrClient(new HttpClient(new StubHttpMessageHandler(
                new Dictionary<string, object?>
                {
                    ["/api/v3/qualityprofile"] =
                    new object[]
                    {
                        new
                        {
                            id = 3,
                            name = "HD-1080p",
                            upgradeUntilCustomFormatScore = 10
                        }
                    },
                    ["/api/v3/tag"] = Array.Empty<object>(),
                    ["/api/v3/series"] =
                    new object[]
                    {
                        new
                        {
                            id = 11,
                            title = "Andor",
                            titleSlug = "andor",
                            qualityProfileId = 3,
                            status = "continuing"
                        }
                    },
                    ["/api/v3/episode?seriesId=11"] =
                    new object[]
                    {
                        new
                        {
                            id = 501,
                            title = "Kassa",
                            seasonNumber = 1,
                            episodeNumber = 1,
                            hasFile = true,
                            episodeFileId = 101,
                            runtime = 42
                        }
                    },
                    ["/api/v3/episodefile?seriesId=11"] =
                    new object[]
                    {
                        new
                        {
                            id = 101,
                            customFormatScore = 4,
                            size = 123456789L,
                            dateAdded = "2026-01-05T10:00:00+00:00",
                            releaseGroup = "GROUP",
                            mediaInfo = new
                            {
                                runTime = 2520
                            }
                        }
                    },
                    ["/api/v3/history/since"] = Array.Empty<object>()
                }))),
            CreateUnusedRadarrClient(),
            new SymlinkMatchingRequests(),
            NullLogger<SnapshotSyncService>.Instance);

        await service.SyncAsync(
            new SyncInstanceRequest
            {
                Kind = ArrApplicationKind.Sonarr,
                InstanceId = "sonarr-main"
            },
            CancellationToken.None);

        LibraryChangeHistoryEntry entry = Assert.Single(historyStore.SonarrLibraryHistory);
        Assert.Equal("seed:sonarr:sonarr-main:501", entry.SourceKey);
        Assert.Equal("sonarr:sonarr-main:501", entry.MediaKey);
        Assert.Equal(LibraryChangeType.Added, entry.ChangeType);
        Assert.Equal(new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero), entry.OccurredAt);
        Assert.Equal(123456789, entry.DeltaSizeBytes);
    }

    [Fact]
    public async Task TargetedSeriesSyncUpdatesTheSpecialHistoryArchiveAndFiltersWithoutReadingOtherSeries()
    {
        var scenario = CreateTargetedScenario();
        SonarrInstanceSnapshot before = scenario.Settings.CurrentSettings.SonarrSnapshots[0];
        SonarrSeriesSnapshot untouched = before.Series.Single(series => series.SeriesId == 22);
        SonarrInstanceSnapshot otherInstance = scenario.Settings.CurrentSettings.SonarrSnapshots[1];

        await scenario.Service.SyncSonarrSeriesAsync("sonarr-main", 11, CancellationToken.None);

        SonarrInstanceSnapshot updated = scenario.Settings.CurrentSettings.SonarrSnapshots.Single(snapshot => snapshot.InstanceId == "sonarr-main");
        SonarrEpisodeSnapshot special = updated.Series.Single(series => series.SeriesId == 11).Seasons[0].Episodes[0];
        Assert.False(special.HasFile);
        Assert.Null(special.File.Id);
        Assert.Null(special.File.Size);
        Assert.Same(untouched, updated.Series.Single(series => series.SeriesId == 22));
        Assert.Same(otherInstance, scenario.Settings.CurrentSettings.SonarrSnapshots.Single(snapshot => snapshot.InstanceId == "sonarr-other"));
        Assert.True(before.Series[0].Seasons[0].Episodes[0].HasFile);
        Assert.Equal(before.SyncedAt, updated.LastFullSyncAt);
        Assert.True(updated.SyncedAt > before.SyncedAt);
        Assert.Equal(updated.SyncedAt, updated.SeriesSyncTimes[11]);
        LibraryChangeHistoryEntry deletion = Assert.Single(historyStore.SonarrLibraryHistory, entry => entry.ChangeType == LibraryChangeType.Deleted);
        Assert.Equal("sonarr:9001", deletion.SourceKey);
        Assert.Equal(-100, deletion.DeltaSizeBytes);
        Assert.Equal(100, deletion.SizeBytes);
        Assert.Single(historyStore.SnapshotHistory);
        LibraryFilterValues filters = scenario.Settings.CurrentSettings.LibraryFilterValues.Single(value => value.InstanceId == "sonarr-main");
        Assert.DoesNotContain("4k", filters.Resolutions);
        Assert.Contains("1080p", filters.Resolutions);
        Assert.DoesNotContain(scenario.Handler.Requests, uri => uri.AbsolutePath == "/api/v3/series");
        Assert.DoesNotContain(scenario.Handler.Requests, uri => uri.Query.Contains("seriesId=22", StringComparison.Ordinal));
        Assert.DoesNotContain(scenario.Handler.Requests, uri => uri.AbsolutePath == "/api/v3/history/since");
    }

    [Fact]
    public async Task TargetedSeriesSyncIsIdempotentAndALaterFullSyncStillImportsOtherSeriesHistory()
    {
        var scenario = CreateTargetedScenario();
        DateTimeOffset fullSyncAt = scenario.Settings.CurrentSettings.SonarrSnapshots[0].SyncedAt;
        await scenario.Service.SyncSonarrSeriesAsync("sonarr-main", 11, CancellationToken.None);
        await scenario.Service.SyncSonarrSeriesAsync("sonarr-main", 11, CancellationToken.None);
        Assert.Equal(4, historyStore.SonarrLibraryHistory.Count);
        Assert.Equal(-100, Assert.Single(historyStore.SonarrLibraryHistory, entry => entry.ChangeType == LibraryChangeType.Deleted).DeltaSizeBytes);

        await scenario.Service.SyncAsync(new SyncInstanceRequest { Kind = ArrApplicationKind.Sonarr, InstanceId = "sonarr-main" }, CancellationToken.None);

        Uri historyRequest = Assert.Single(scenario.Handler.Requests, uri => uri.AbsolutePath == "/api/v3/history/since");
        Assert.Contains(Uri.EscapeDataString(fullSyncAt.ToString("O")), historyRequest.Query);
        Assert.Equal(5, historyStore.SonarrLibraryHistory.Count);
        Assert.Equal(-100, Assert.Single(historyStore.SonarrLibraryHistory, entry => entry.ChangeType == LibraryChangeType.Deleted).DeltaSizeBytes);
        Assert.Equal(50, Assert.Single(historyStore.SonarrLibraryHistory, entry => entry.SourceKey == "sonarr:9002").DeltaSizeBytes);
        SonarrInstanceSnapshot updated = scenario.Settings.CurrentSettings.SonarrSnapshots.Single(snapshot => snapshot.InstanceId == "sonarr-main");
        Assert.Empty(updated.SeriesSyncTimes);
        Assert.Null(updated.LastFullSyncAt);
    }

    [Fact]
    public async Task TargetedSeriesSyncAddsTheSpecialAndCountsAnImportFollowedByRenameOnlyOnce()
    {
        var scenario = CreateTargetedScenario();
        SonarrInstanceSnapshot before = scenario.Settings.CurrentSettings.SonarrSnapshots[0];
        before.Series[0].Seasons[0].Episodes[0].HasFile = false;
        historyStore.SonarrLibraryHistory.Add(new LibraryChangeHistoryEntry
        {
            SourceKey = "sonarr:9001", Kind = ArrApplicationKind.Sonarr, InstanceId = "sonarr-main",
            MediaKey = "sonarr:sonarr-main:501", ChangeType = LibraryChangeType.Deleted,
            OccurredAt = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), SizeBytes = 100, DeltaSizeBytes = -100
        });
        scenario.Responses["/api/v3/episode?seriesId=11"] = new object[]
            { new { id = 501, episodeNumber = 4, seasonNumber = 0, title = "Special", hasFile = true, episodeFileId = 101 } };
        scenario.Responses["/api/v3/episodefile?seriesId=11"] = new object[]
            { new { id = 101, size = 100, relativePath = "Specials/final-name.mkv", mediaInfo = new { resolution = "2160p" } } };
        // Sonarr can return newest events first; the rename must see the size established by the import.
        scenario.Responses["/api/v3/history/series"] = new object[]
        {
            new { id = 9004, seriesId = 11, episodeId = 501, sourceTitle = "Special", eventType = "episodeFileRenamed", date = "2026-07-01T10:00:01Z" },
            new { id = 9003, seriesId = 11, episodeId = 501, sourceTitle = "Special", eventType = "downloadFolderImported", date = "2026-07-01T10:00:00Z" }
        };

        await scenario.Service.SyncSonarrSeriesAsync("sonarr-main", 11, CancellationToken.None);

        SonarrEpisodeSnapshot special = scenario.Settings.CurrentSettings.SonarrSnapshots.Single(snapshot => snapshot.InstanceId == "sonarr-main").Series
            .Single(series => series.SeriesId == 11).Seasons[0].Episodes[0];
        Assert.True(special.HasFile);
        Assert.Equal(101, special.File.Id);
        Assert.Equal("Specials/final-name.mkv", special.File.RelativePath);
        Assert.Equal(100, historyStore.SonarrLibraryHistory.Where(entry => entry.MediaKey == "sonarr:sonarr-main:501").Sum(entry => entry.DeltaSizeBytes));
        Assert.Equal(0, Assert.Single(historyStore.SonarrLibraryHistory, entry => entry.SourceKey == "sonarr:9004").DeltaSizeBytes);
        Assert.Contains("4k", scenario.Settings.CurrentSettings.LibraryFilterValues.Single(value => value.InstanceId == "sonarr-main").Resolutions);
        Assert.DoesNotContain(scenario.Handler.Requests, uri => uri.AbsolutePath == "/api/v3/series");
    }

    [Fact]
    public async Task TargetedSeriesSyncDoesNotPublishAPartialResultWhenReadingHistoryFails()
    {
        var scenario = CreateTargetedScenario();
        SonarrInstanceSnapshot before = scenario.Settings.CurrentSettings.SonarrSnapshots[0];
        scenario.Responses["/api/v3/history/series"] = null;

        await Assert.ThrowsAsync<HttpRequestException>(() => scenario.Service.SyncSonarrSeriesAsync("sonarr-main", 11, CancellationToken.None));

        Assert.Same(before, scenario.Settings.CurrentSettings.SonarrSnapshots[0]);
        Assert.True(before.Series[0].Seasons[0].Episodes[0].HasFile);
        Assert.Empty(historyStore.SnapshotHistory);
        Assert.Equal(0, scenario.Settings.SaveCount);
        Assert.Equal(3, historyStore.SonarrLibraryHistory.Count);
    }

    private (SnapshotSyncService Service, InMemorySettingsService Settings, StubHttpMessageHandler Handler,
        Dictionary<string, object?> Responses) CreateTargetedScenario()
    {
        DateTimeOffset lastFull = new(2026, 4, 20, 8, 0, 0, TimeSpan.Zero);
        SonarrSeriesSnapshot target = new()
        {
            SeriesId = 11, Title = "Target series", Seasons = [new SonarrSeasonSnapshot { SeasonNumber = 0, Episodes =
                [new SonarrEpisodeSnapshot { EpisodeId = 501, EpisodeNumber = 4, Title = "Special", HasFile = true,
                    File = new MediaFileSnapshot { Id = 101, Size = 100, Resolution = "2160p", RelativePath = "Specials/special.mkv" } }] }]
        };
        SonarrSeriesSnapshot untouched = new()
        {
            SeriesId = 22, Title = "Untouched series", Seasons = [new SonarrSeasonSnapshot { SeasonNumber = 1, Episodes =
                [new SonarrEpisodeSnapshot { EpisodeId = 502, EpisodeNumber = 1, Title = "Episode", HasFile = true,
                    File = new MediaFileSnapshot { Id = 102, Size = 200, Resolution = "1080p" } }] }]
        };
        AppSettings settings = new()
        {
            SonarrInstances = [new ArrInstanceSettings { Id = "sonarr-main", Name = "Main", BaseUrl = "http://sonarr.test", ApiKey = "key" },
                new ArrInstanceSettings { Id = "sonarr-other", Name = "Other", BaseUrl = "http://other.test", ApiKey = "key" }],
            SonarrSnapshots = [new SonarrInstanceSnapshot { InstanceId = "sonarr-main", InstanceName = "Main", SyncedAt = lastFull,
                Series = [target, untouched] }, new SonarrInstanceSnapshot { InstanceId = "sonarr-other", SyncedAt = lastFull }]
        };
        foreach ((string instance, int episode, long size) in new[] { ("sonarr-main", 501, 100L), ("sonarr-main", 502, 200L), ("sonarr-other", 888, 300L) })
            historyStore.SonarrLibraryHistory.Add(new LibraryChangeHistoryEntry { SourceKey = $"import:{episode}", Kind = ArrApplicationKind.Sonarr,
                InstanceId = instance, MediaKey = $"sonarr:{instance}:{episode}", ChangeType = LibraryChangeType.Added,
                OccurredAt = lastFull.AddDays(-1), SizeBytes = size, DeltaSizeBytes = size });
        object deleted = new { id = 9001, seriesId = 11, episodeId = 501, sourceTitle = "Special", eventType = "episodeFileDeleted", date = "2026-05-01T10:00:00Z" };
        object imported = new { id = 9002, seriesId = 22, episodeId = 503, sourceTitle = "New episode", eventType = "downloadFolderImported", date = "2026-06-01T10:00:00Z" };
        Dictionary<string, object?> responses = new()
        {
            ["/api/v3/qualityprofile"] = Array.Empty<object>(), ["/api/v3/tag"] = Array.Empty<object>(),
            ["/api/v3/series/11"] = new { id = 11, title = "Target series" },
            ["/api/v3/series"] = new object[] { new { id = 11, title = "Target series" }, new { id = 22, title = "Untouched series" } },
            ["/api/v3/episode?seriesId=11"] = new object[] { new { id = 501, episodeNumber = 4, seasonNumber = 0, title = "Special", hasFile = false, episodeFileId = 0 } },
            ["/api/v3/episodefile?seriesId=11"] = Array.Empty<object>(),
            ["/api/v3/episode?seriesId=22"] = new object[] { new { id = 502, episodeNumber = 1, seasonNumber = 1, title = "Episode", hasFile = true, episodeFileId = 102 },
                new { id = 503, episodeNumber = 2, seasonNumber = 1, title = "New episode", hasFile = true, episodeFileId = 103 } },
            ["/api/v3/episodefile?seriesId=22"] = new object[] { new { id = 102, size = 200 }, new { id = 103, size = 50 } },
            ["/api/v3/history/series"] = new[] { deleted }, ["/api/v3/history/since"] = new[] { deleted, imported }
        };
        StubHttpMessageHandler handler = new(responses);
        InMemorySettingsService saved = new(settings);
        SnapshotSyncService service = new(saved, historyStore, new SonarrClient(new HttpClient(handler)), CreateUnusedRadarrClient(),
            new SymlinkMatchingRequests(), NullLogger<SnapshotSyncService>.Instance);
        return (service, saved, handler, responses);
    }

    private static SonarrClient CreateUnusedSonarrClient()
    {
        return new SonarrClient(new HttpClient(new StubHttpMessageHandler(new Dictionary<string, object?>())));
    }

    private static RadarrClient CreateUnusedRadarrClient()
    {
        return new RadarrClient(new HttpClient(new StubHttpMessageHandler(new Dictionary<string, object?>())));
    }

    private sealed class InMemorySettingsService(AppSettings settings) : ISettingsService
    {
        public AppSettings CurrentSettings { get; private set; } = settings;

        public int SaveCount { get; private set; }

        public Task<AppSettings> GetAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(CurrentSettings);
        }

        public Task<AppSettings> SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            SaveCount += 1;
            CurrentSettings = settings;

            return Task.FromResult(settings);
        }
    }

    private sealed class StubHttpMessageHandler(
        IReadOnlyDictionary<string, object?> responses) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            string key = request.RequestUri?.PathAndQuery ?? string.Empty;

            if (!responses.TryGetValue(key, out object? responseBody))
            {
                key = request.RequestUri?.AbsolutePath ?? string.Empty;
                if (!responses.TryGetValue(key, out responseBody))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        RequestMessage = request
                    });
                }
            }

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(responseBody),
                RequestMessage = request
            };

            return Task.FromResult(response);
        }
    }
}
