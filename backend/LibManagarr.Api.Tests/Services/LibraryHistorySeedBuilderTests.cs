using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class LibraryHistorySeedBuilderTests
{
    private static readonly ArrInstanceSettings Instance = new() { Id = "sonarr-main", Name = "Sonarr Main" };
    private static readonly ArrInstanceSettings RadarrInstance = new() { Id = "radarr-main", Name = "Radarr Main" };
    private static readonly DateTimeOffset SyncedAt = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ImportAt = new(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildSonarrSeeds_ShouldSeedOnlyFilesWithoutAddedEntry_AndUseImportDate()
    {
        SonarrInstanceSnapshot snapshot = SonarrSnapshot(
            Episode(501, "Covered", ImportAt, 100),
            Episode(502, "Gina", ImportAt, 200),
            Episode(503, "Narkina 5", null, 300),
            EpisodeWithoutFile(504));
        List<LibraryChangeHistoryEntry> history = [Entry("sonarr:sonarr-main:501", LibraryChangeType.Added, 100)];

        List<LibraryChangeHistoryEntry> seeds = [.. LibraryHistorySeedBuilder.BuildSonarrSeeds(Instance, snapshot, history)];

        Assert.Equal(
            ["seed:sonarr:sonarr-main:502", "seed:sonarr:sonarr-main:503"],
            seeds.Select(entry => entry.SourceKey));

        LibraryChangeHistoryEntry seed = seeds[0];
        Assert.Equal(ArrApplicationKind.Sonarr, seed.Kind);
        Assert.Equal("sonarr-main", seed.InstanceId);
        Assert.Equal("Sonarr Main", seed.InstanceName);
        Assert.Equal(ImportAt, seed.OccurredAt);
        Assert.Equal("sonarr:sonarr-main:502", seed.MediaKey);
        Assert.Equal(LibraryChangeType.Added, seed.ChangeType);
        Assert.Equal("Gina", seed.EntityTitle);
        Assert.Equal("Andor", seed.SeriesTitle);
        Assert.Equal($"andor/502.mkv", seed.FilePath);
        Assert.Equal(200, seed.SizeBytes);
        Assert.Equal(200, seed.DeltaSizeBytes);
        Assert.Equal(SyncedAt, seeds[1].OccurredAt);
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(40, 60)]
    [InlineData(0, 100)]
    public void BuildSonarrSeeds_ShouldClampDeltaToTheUnaccountedSizeRemainder(
        long accountedDelta, long expectedSeedDelta)
    {
        SonarrInstanceSnapshot snapshot = SonarrSnapshot(Episode(501, "Kassa", ImportAt, 100));
        List<LibraryChangeHistoryEntry> history =
        [
            Entry("sonarr:sonarr-main:501", LibraryChangeType.Changed, accountedDelta)
        ];

        LibraryChangeHistoryEntry seed = Assert.Single(
            LibraryHistorySeedBuilder.BuildSonarrSeeds(Instance, snapshot, history));

        Assert.Equal(expectedSeedDelta, seed.DeltaSizeBytes);
    }

    [Fact]
    public void BuildSonarrSeeds_ShouldNotReseedCoveredKeys_OnRepeatedSyncs()
    {
        SonarrInstanceSnapshot snapshot = SonarrSnapshot(Episode(501, "Kassa", ImportAt, 100));
        LibraryChangeHistoryEntry seededEntry = Entry("sonarr:sonarr-main:501", LibraryChangeType.Added, 100);
        seededEntry.SourceKey = "seed:sonarr:sonarr-main:501";

        Assert.Empty(LibraryHistorySeedBuilder.BuildSonarrSeeds(Instance, snapshot, [seededEntry]));
    }

    [Fact]
    public void BuildRadarrSeeds_ShouldSeedMoviesWithoutAddedEvent_EvenWhenOtherEventsExist()
    {
        RadarrInstanceSnapshot snapshot = new()
        {
            InstanceId = "radarr-main",
            InstanceName = "Radarr Main",
            SyncedAt = SyncedAt,
            Movies =
            [
                new RadarrMovieSnapshot
                {
                    MovieId = 21,
                    Title = "Dune",
                    HasFile = true,
                    File = new MediaFileSnapshot { Size = 300, CreatedAt = ImportAt, RelativePath = "dune.mkv" }
                },
                new RadarrMovieSnapshot
                {
                    MovieId = 22,
                    Title = "Arrival",
                    HasFile = true,
                    File = new MediaFileSnapshot { Size = 500 }
                }
            ]
        };
        List<LibraryChangeHistoryEntry> history =
        [
            Entry("radarr:radarr-main:21", LibraryChangeType.Added, 300, ArrApplicationKind.Radarr),
            Entry("radarr:radarr-main:22", LibraryChangeType.Updated, 50, ArrApplicationKind.Radarr)
        ];

        LibraryChangeHistoryEntry seed = Assert.Single(
            LibraryHistorySeedBuilder.BuildRadarrSeeds(RadarrInstance, snapshot, history));

        Assert.Equal("seed:radarr:radarr-main:22", seed.SourceKey);
        Assert.Equal(ArrApplicationKind.Radarr, seed.Kind);
        Assert.Equal(LibraryChangeType.Added, seed.ChangeType);
        Assert.Equal("Arrival", seed.EntityTitle);
        Assert.Null(seed.SeriesTitle);
        Assert.Equal(SyncedAt, seed.OccurredAt);
        Assert.Equal(500, seed.SizeBytes);
        Assert.Equal(450, seed.DeltaSizeBytes);
    }

    private static SonarrInstanceSnapshot SonarrSnapshot(params SonarrEpisodeSnapshot[] episodes)
    {
        return new SonarrInstanceSnapshot
        {
            InstanceId = Instance.Id,
            InstanceName = Instance.Name,
            SyncedAt = SyncedAt,
            Series =
            [
                new SonarrSeriesSnapshot
                {
                    SeriesId = 11,
                    Title = "Andor",
                    Seasons =
                    [
                        new SonarrSeasonSnapshot
                        {
                            SeasonNumber = 1,
                            Episodes = [.. episodes]
                        }
                    ]
                }
            ]
        };
    }

    private static SonarrEpisodeSnapshot Episode(int episodeId, string title, DateTimeOffset? createdAt, long size)
    {
        return new SonarrEpisodeSnapshot
        {
            EpisodeId = episodeId,
            Title = title,
            HasFile = true,
            File = new MediaFileSnapshot
            {
                Size = size,
                CreatedAt = createdAt,
                RelativePath = $"andor/{episodeId}.mkv"
            }
        };
    }

    private static SonarrEpisodeSnapshot EpisodeWithoutFile(int episodeId)
    {
        return new SonarrEpisodeSnapshot { EpisodeId = episodeId, Title = "Unaired", HasFile = false };
    }

    private static LibraryChangeHistoryEntry Entry(
        string mediaKey,
        LibraryChangeType changeType,
        long deltaSizeBytes,
        ArrApplicationKind kind = ArrApplicationKind.Sonarr)
    {
        return new LibraryChangeHistoryEntry
        {
            SourceKey = mediaKey,
            Kind = kind,
            InstanceId = Instance.Id,
            InstanceName = Instance.Name,
            OccurredAt = ImportAt,
            MediaKey = mediaKey,
            ChangeType = changeType,
            EntityTitle = "Entry",
            DeltaSizeBytes = deltaSizeBytes
        };
    }
}
