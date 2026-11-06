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
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
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
