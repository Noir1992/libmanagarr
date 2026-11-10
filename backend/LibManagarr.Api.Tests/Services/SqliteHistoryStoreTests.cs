using Microsoft.Data.Sqlite;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using LibManagarr.Api.Services.Backup;

namespace LibManagarr.Api.Tests.Services;

public sealed class SqliteHistoryStoreTests : IDisposable
{
    private readonly TestStorageEnvironment environment = new();

    [Fact]
    public async Task ReplaceLibraryHistoryAsync_ShouldRoundTripEntries_AndFilterByKind()
    {
        SqliteHistoryStore store = CreateStore();
        LibraryChangeHistoryEntry sonarrEntry = CreateEntry(
            "sonarr:9001",
            ArrApplicationKind.Sonarr,
            "sonarr-main",
            new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
            LibraryChangeType.Deleted,
            -100);
        sonarrEntry.SeriesTitle = "Andor";
        sonarrEntry.FilePath = @"/media/andor/S01E01.mkv";
        sonarrEntry.SizeBytes = 100;
        LibraryChangeHistoryEntry radarrEntry = CreateEntry(
            "radarr:9101",
            ArrApplicationKind.Radarr,
            "radarr-main",
            new DateTimeOffset(2026, 7, 9, 12, 30, 0, TimeSpan.FromHours(2)),
            LibraryChangeType.Added,
            321);

        await store.ReplaceLibraryHistoryAsync(ArrApplicationKind.Sonarr, [sonarrEntry], CancellationToken.None);
        await store.ReplaceLibraryHistoryAsync(ArrApplicationKind.Radarr, [radarrEntry], CancellationToken.None);

        LibraryChangeHistoryEntry reloaded = Assert.Single(
            await store.GetLibraryHistoryAsync(ArrApplicationKind.Sonarr, CancellationToken.None));
        Assert.Equal("sonarr:9001", reloaded.SourceKey);
        Assert.Equal(ArrApplicationKind.Sonarr, reloaded.Kind);
        Assert.Equal("sonarr-main", reloaded.InstanceId);
        Assert.Equal("Sonarr Main", reloaded.InstanceName);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), reloaded.OccurredAt);
        Assert.Equal("sonarr:sonarr-main:501", reloaded.MediaKey);
        Assert.Equal(LibraryChangeType.Deleted, reloaded.ChangeType);
        Assert.Equal("Kassa", reloaded.EntityTitle);
        Assert.Equal("Andor", reloaded.SeriesTitle);
        Assert.Equal(@"/media/andor/S01E01.mkv", reloaded.FilePath);
        Assert.Equal(100, reloaded.SizeBytes);
        Assert.Equal(-100, reloaded.DeltaSizeBytes);

        LibraryChangeHistoryEntry reloadedRadarr = Assert.Single(
            await store.GetLibraryHistoryAsync(ArrApplicationKind.Radarr, CancellationToken.None));
        Assert.Equal(new DateTimeOffset(2026, 7, 9, 12, 30, 0, TimeSpan.FromHours(2)), reloadedRadarr.OccurredAt);
        Assert.Null(reloadedRadarr.SeriesTitle);
        Assert.Null(reloadedRadarr.FilePath);
        Assert.Null(reloadedRadarr.SizeBytes);

        IReadOnlyList<LibraryChangeHistoryEntry> combined =
            await store.GetLibraryHistoryAsync(null, CancellationToken.None);
        Assert.Equal(2, combined.Count);
    }

    [Fact]
    public async Task ReplaceLibraryHistoryAsync_ShouldOnlyReplaceEntriesOfTheGivenKind()
    {
        SqliteHistoryStore store = CreateStore();
        await store.ReplaceLibraryHistoryAsync(
            ArrApplicationKind.Radarr,
            [CreateEntry("radarr:keep", ArrApplicationKind.Radarr, "radarr-main", DateTimeOffset.Parse("2026-06-01T00:00:00Z"), LibraryChangeType.Added, 10)],
            CancellationToken.None);
        await store.ReplaceLibraryHistoryAsync(
            ArrApplicationKind.Sonarr,
            [CreateEntry("sonarr:old", ArrApplicationKind.Sonarr, "sonarr-main", DateTimeOffset.Parse("2026-05-01T00:00:00Z"), LibraryChangeType.Added, 1)],
            CancellationToken.None);

        await store.ReplaceLibraryHistoryAsync(
            ArrApplicationKind.Sonarr,
            [
                CreateEntry("sonarr:new-1", ArrApplicationKind.Sonarr, "sonarr-main", DateTimeOffset.Parse("2026-05-02T00:00:00Z"), LibraryChangeType.Added, 2),
                CreateEntry("sonarr:new-2", ArrApplicationKind.Sonarr, "sonarr-main", DateTimeOffset.Parse("2026-05-03T00:00:00Z"), LibraryChangeType.Updated, 3)
            ],
            CancellationToken.None);

        IReadOnlyList<LibraryChangeHistoryEntry> sonarrHistory =
            await store.GetLibraryHistoryAsync(ArrApplicationKind.Sonarr, CancellationToken.None);
        Assert.Equal(
            new[] { "sonarr:new-1", "sonarr:new-2" },
            sonarrHistory.Select((entry) => entry.SourceKey));
        Assert.Single(await store.GetLibraryHistoryAsync(ArrApplicationKind.Radarr, CancellationToken.None));
    }

    [Fact]
    public async Task HasLibraryHistoryAsync_ShouldMatchKindAndInstanceId()
    {
        SqliteHistoryStore store = CreateStore();
        await store.ReplaceLibraryHistoryAsync(
            ArrApplicationKind.Sonarr,
            [CreateEntry("sonarr:9001", ArrApplicationKind.Sonarr, "sonarr-main", DateTimeOffset.Parse("2026-05-01T00:00:00Z"), LibraryChangeType.Added, 1)],
            CancellationToken.None);

        Assert.True(await store.HasLibraryHistoryAsync(ArrApplicationKind.Sonarr, "sonarr-main", CancellationToken.None));
        Assert.False(await store.HasLibraryHistoryAsync(ArrApplicationKind.Sonarr, "sonarr-other", CancellationToken.None));
        Assert.False(await store.HasLibraryHistoryAsync(ArrApplicationKind.Radarr, "sonarr-main", CancellationToken.None));
    }

    [Fact]
    public async Task LibraryHistory_ShouldPersistAcrossStoreInstances()
    {
        SqliteHistoryStore firstStore = CreateStore();
        await firstStore.ReplaceLibraryHistoryAsync(
            ArrApplicationKind.Sonarr,
            [CreateEntry("sonarr:9001", ArrApplicationKind.Sonarr, "sonarr-main", DateTimeOffset.Parse("2026-05-01T00:00:00Z"), LibraryChangeType.Added, 7)],
            CancellationToken.None);

        SqliteHistoryStore secondStore = CreateStore();
        LibraryChangeHistoryEntry reloaded = Assert.Single(
            await secondStore.GetLibraryHistoryAsync(ArrApplicationKind.Sonarr, CancellationToken.None));
        Assert.Equal("sonarr:9001", reloaded.SourceKey);
        Assert.Equal(7, reloaded.DeltaSizeBytes);
    }

    [Fact]
    public async Task AppendSnapshotHistoryAsync_ShouldStoreCamelCasePayloadInDatabaseFile()
    {
        SqliteHistoryStore store = CreateStore();
        SonarrInstanceSnapshot snapshot = new()
        {
            InstanceId = "sonarr-main",
            InstanceName = "Sonarr Main",
            SyncedAt = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero)
        };

        await store.AppendSnapshotHistoryAsync(
            ArrApplicationKind.Sonarr, snapshot.InstanceId, snapshot.InstanceName, snapshot.SyncedAt, snapshot, CancellationToken.None);
        await store.AppendSnapshotHistoryAsync(
            ArrApplicationKind.Sonarr, snapshot.InstanceId, snapshot.InstanceName, snapshot.SyncedAt.AddMinutes(5), snapshot, CancellationToken.None);

        string databasePath = Path.Combine(environment.ContentRootPath, "data", "libmanagarr.db");
        Assert.True(File.Exists(databasePath));

        SqliteConnectionStringBuilder builder = new() { DataSource = databasePath };
        await using SqliteConnection connection = new(builder.ToString());
        await connection.OpenAsync();
        await using SqliteCommand countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM snapshot_history WHERE kind = 'sonarr'";
        Assert.Equal(2L, await countCommand.ExecuteScalarAsync());

        await using SqliteCommand payloadCommand = connection.CreateCommand();
        payloadCommand.CommandText = "SELECT payload FROM snapshot_history ORDER BY id LIMIT 1";
        string payload = (string?)await payloadCommand.ExecuteScalarAsync() ?? string.Empty;
        Assert.Contains("\"instanceId\":\"sonarr-main\"", payload);
        Assert.Contains("\"series\":", payload);
    }

    private SqliteHistoryStore CreateStore() => new(new StoragePaths(environment, configDirectory: null), new BackupOperationCoordinator(TimeProvider.System));

    [Fact]
    public async Task InstanceSnapshots_ShouldRoundTripPayload_AndReplacePerKind()
    {
        SqliteHistoryStore store = CreateStore();
        SonarrInstanceSnapshot sonarr = new()
        {
            InstanceId = "sonarr-main",
            InstanceName = "Sonarr Main",
            SyncedAt = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.FromHours(2)),
            Series = [new() { SeriesId = 7, Title = "Andor", Path = "/media/series/Andor", Seasons = [new() { SeasonNumber = 1, Episodes = [new() { EpisodeId = 1, Title = "Kassa", HasFile = true, File = new() { Size = 100 } }] }] }]
        };
        sonarr.PosterUrls[7] = "/posters/7.jpg";
        RadarrInstanceSnapshot radarr = new()
        {
            InstanceId = "radarr-main",
            InstanceName = "Radarr Main",
            SyncedAt = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            Movies = [new() { MovieId = 21, Title = "Dune", HasFile = true, File = new() { Size = 300 } }]
        };

        await store.ReplaceSonarrInstanceSnapshotsAsync([sonarr], CancellationToken.None);
        await store.ReplaceRadarrInstanceSnapshotsAsync([radarr], CancellationToken.None);

        SonarrInstanceSnapshot reloaded = Assert.Single(await store.GetSonarrInstanceSnapshotsAsync(CancellationToken.None));
        Assert.Equal("Sonarr Main", reloaded.InstanceName);
        Assert.Equal(sonarr.SyncedAt, reloaded.SyncedAt);
        SonarrSeriesSnapshot series = Assert.Single(reloaded.Series);
        Assert.Equal("Andor", series.Title);
        Assert.Equal("/media/series/Andor", series.Path);
        Assert.Equal("/posters/7.jpg", reloaded.PosterUrls[7]);
        Assert.Equal(100, series.Seasons[0].Episodes[0].File.Size);

        RadarrInstanceSnapshot reloadedRadarr = Assert.Single(await store.GetRadarrInstanceSnapshotsAsync(CancellationToken.None));
        Assert.Equal("Dune", Assert.Single(reloadedRadarr.Movies).Title);
    }

    [Fact]
    public async Task ReplaceInstanceSnapshotsAsync_ShouldDropInstancesMissingFromNewList_AndKeepOtherKind()
    {
        SqliteHistoryStore store = CreateStore();
        RadarrInstanceSnapshot radarr = new() { InstanceId = "radarr-main", InstanceName = "Radarr Main", SyncedAt = DateTimeOffset.UnixEpoch };
        await store.ReplaceSonarrInstanceSnapshotsAsync(
            [
                new SonarrInstanceSnapshot { InstanceId = "sonarr-main", InstanceName = "Sonarr Main", SyncedAt = DateTimeOffset.UnixEpoch },
                new SonarrInstanceSnapshot { InstanceId = "sonarr-old", InstanceName = "Sonarr Old", SyncedAt = DateTimeOffset.UnixEpoch }
            ],
            CancellationToken.None);
        await store.ReplaceRadarrInstanceSnapshotsAsync([radarr], CancellationToken.None);

        await store.ReplaceSonarrInstanceSnapshotsAsync(
            [new SonarrInstanceSnapshot { InstanceId = "sonarr-main", InstanceName = "Sonarr Main", SyncedAt = DateTimeOffset.UnixEpoch }],
            CancellationToken.None);

        SonarrInstanceSnapshot remaining = Assert.Single(await store.GetSonarrInstanceSnapshotsAsync(CancellationToken.None));
        Assert.Equal("sonarr-main", remaining.InstanceId);
        Assert.Single(await store.GetRadarrInstanceSnapshotsAsync(CancellationToken.None));

        await store.ReplaceSonarrInstanceSnapshotsAsync([], CancellationToken.None);
        Assert.Empty(await store.GetSonarrInstanceSnapshotsAsync(CancellationToken.None));
        Assert.Single(await store.GetRadarrInstanceSnapshotsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task InstanceSnapshots_ShouldPersistAcrossStoreInstances()
    {
        SqliteHistoryStore firstStore = CreateStore();
        await firstStore.ReplaceRadarrInstanceSnapshotsAsync(
            [new RadarrInstanceSnapshot { InstanceId = "radarr-main", InstanceName = "Radarr Main", SyncedAt = DateTimeOffset.UnixEpoch, Movies = [new() { MovieId = 21, Title = "Dune" }] }],
            CancellationToken.None);

        SqliteHistoryStore secondStore = CreateStore();
        RadarrInstanceSnapshot reloaded = Assert.Single(await secondStore.GetRadarrInstanceSnapshotsAsync(CancellationToken.None));
        Assert.Equal("Radarr Main", reloaded.InstanceName);
        Assert.Equal("Dune", Assert.Single(reloaded.Movies).Title);
    }

    [Fact]
    public async Task LibraryFilterValues_ShouldRoundTrip_AndReplacePreviousRows()
    {
        SqliteHistoryStore store = CreateStore();
        LibraryFilterValues sonarrValues = new()
        {
            Version = LibraryFilterValues.CurrentVersion,
            InstanceId = "sonarr-main",
            Genres = ["Drama", "Sci-Fi"],
            Counts = { ["genre"] = new Dictionary<string, int> { ["Drama"] = 3 } }
        };
        LibraryFilterValues radarrValues = new() { Version = 1, InstanceId = "radarr-main", VideoCodecs = ["x264"] };

        await store.ReplaceLibraryFilterValuesAsync([sonarrValues, radarrValues], CancellationToken.None);


        IReadOnlyList<LibraryFilterValues> reloaded = await store.GetLibraryFilterValuesAsync(CancellationToken.None);
        Assert.Equal(new[] { "radarr-main", "sonarr-main" }, reloaded.Select((values) => values.InstanceId));
        LibraryFilterValues sonarrReloaded = reloaded.First((values) => values.InstanceId == "sonarr-main");
        Assert.Equal(LibraryFilterValues.CurrentVersion, sonarrReloaded.Version);
        Assert.Equal(new[] { "Drama", "Sci-Fi" }, sonarrReloaded.Genres);
        Assert.Equal(3, sonarrReloaded.Counts["genre"]["Drama"]);

        await store.ReplaceLibraryFilterValuesAsync([radarrValues], CancellationToken.None);
        LibraryFilterValues remaining = Assert.Single(await store.GetLibraryFilterValuesAsync(CancellationToken.None));
        Assert.Equal("radarr-main", remaining.InstanceId);


    }

    [Fact]
    public async Task LibraryFilterValues_ShouldPersistAcrossStoreInstances()
    {
        SqliteHistoryStore firstStore = CreateStore();
        await firstStore.ReplaceLibraryFilterValuesAsync(
            [new LibraryFilterValues { Version = 2, InstanceId = "sonarr-main", Genres = ["Drama"] }],
            CancellationToken.None);


        SqliteHistoryStore secondStore = CreateStore();
        LibraryFilterValues reloaded = Assert.Single(await secondStore.GetLibraryFilterValuesAsync(CancellationToken.None));
        Assert.Equal(2, reloaded.Version);
        Assert.Equal("Drama", Assert.Single(reloaded.Genres));
    }

    private static LibraryChangeHistoryEntry CreateEntry(
        string sourceKey,
        ArrApplicationKind kind,
        string instanceId,
        DateTimeOffset occurredAt,
        LibraryChangeType changeType,
        long deltaSizeBytes)
    {
        return new LibraryChangeHistoryEntry
        {
            SourceKey = sourceKey,
            Kind = kind,
            InstanceId = instanceId,
            InstanceName = kind == ArrApplicationKind.Sonarr ? "Sonarr Main" : "Radarr Main",
            OccurredAt = occurredAt,
            MediaKey = $"{kind.ToString().ToLowerInvariant()}:{instanceId}:501",
            ChangeType = changeType,
            EntityTitle = "Kassa",
            DeltaSizeBytes = deltaSizeBytes
        };
    }

    public void Dispose()
    {
        // Pooled sqlite connections keep a handle on the database file, which would block the
        // temp directory cleanup in TestStorageEnvironment on Windows.
        SqliteConnection.ClearAllPools();
        environment.Dispose();
    }
}
