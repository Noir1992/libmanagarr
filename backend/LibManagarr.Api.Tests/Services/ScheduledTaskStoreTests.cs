using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using LibManagarr.Api.Services.Backup;

namespace LibManagarr.Api.Tests.Services;

public sealed class ScheduledTaskStoreTests : IDisposable
{
    private readonly TestStorageEnvironment environment = new();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
    private StoragePaths Paths => new(environment, null);
    private string SettingsFile => Path.Combine(Paths.ConfigDirectory, "settings.json");

    internal static ScheduledTaskCatalog CreateCatalog(params ScheduledTaskDefinition[] extra)
    {
        ServiceCollection services = new();
        ScheduledTaskCatalog.AddBuiltInScheduledTasks(services);
        using ServiceProvider provider = services.BuildServiceProvider();
        return new ScheduledTaskCatalog(provider.GetServices<ScheduledTaskDefinition>().Concat(extra));
    }

    private SqliteScheduledTaskStore CreateStore(ScheduledTaskCatalog? catalog = null) =>
        new(Paths, catalog ?? CreateCatalog(), clock, new BackupOperationCoordinator(clock), NullLogger<SqliteScheduledTaskStore>.Instance);

    private async Task WriteSettingsAsync(string json)
    {
        Directory.CreateDirectory(Paths.ConfigDirectory);
        await File.WriteAllTextAsync(SettingsFile, json);
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        Directory.CreateDirectory(Paths.ConfigDirectory);
        await using SqliteConnection connection = new($"Data Source={Paths.DatabasePath}");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task NewInstallation_SeedsDefaultsAndPersistsFirstDueTime()
    {
        SqliteScheduledTaskStore store = CreateStore();
        ScheduledTaskService service = new(store, CreateCatalog(), clock);
        IReadOnlyList<ScheduledTask> tasks = await service.GetAllAsync(CancellationToken.None);
        Assert.Equal(4, tasks.Count);
        ScheduledTask sync = tasks.Single(task => task.TaskKey == "arrDataSync");
        ScheduledTask cleanup = tasks.Single(task => task.TaskKey == "cleanup");
        ScheduledTask health = tasks.Single(task => task.TaskKey == "symlinkHealthCheck");
        ScheduledTask release = tasks.Single(task => task.TaskKey == "releaseCheck");
        Assert.True(sync.Enabled);
        Assert.False(sync.ReadOnly);
        Assert.True(sync.CanRunManually);
        Assert.Equal(ScheduleMode.Interval, sync.ScheduleMode);
        Assert.Equal(ScheduleIntervalUnit.Hour, sync.IntervalUnit);
        Assert.Equal(1, sync.IntervalValue);
        Assert.Equal(clock.GetUtcNow().AddHours(1), sync.NextRunAt);
        Assert.True(health.Enabled);
        Assert.False(health.ReadOnly);
        Assert.True(health.CanRunManually);
        Assert.Equal(ScheduleMode.DailyAtTime, health.ScheduleMode);
        Assert.Equal(3, health.TimeHour);
        Assert.True(health.NextRunAt > clock.GetUtcNow());
        Assert.True(cleanup.Enabled);
        Assert.True(cleanup.ReadOnly);
        Assert.True(cleanup.CanRunManually);
        Assert.Equal(ScheduleMode.DailyAtTime, cleanup.ScheduleMode);
        Assert.Equal(0, cleanup.TimeHour);
        Assert.True(cleanup.NextRunAt > clock.GetUtcNow());
        // The release check keeps a fixed rhythm nobody edits, but its look-up can be triggered by hand.
        Assert.True(release.Enabled);
        Assert.True(release.ReadOnly);
        Assert.True(release.CanRunManually);
        Assert.Equal(ScheduleMode.Interval, release.ScheduleMode);
        Assert.Equal(ScheduleIntervalUnit.Hour, release.IntervalUnit);
        Assert.Equal(12, release.IntervalValue);
        Assert.Equal(clock.GetUtcNow().AddHours(12), release.NextRunAt);
        clock.Advance(TimeSpan.FromDays(3));
        ScheduledTask restarted = (await CreateStore().GetAsync("cleanup", CancellationToken.None))!;
        Assert.Equal(cleanup.NextRunAt, restarted.NextRunAt);
        Assert.Equal(health.NextRunAt, (await CreateStore().GetAsync("symlinkHealthCheck", CancellationToken.None))!.NextRunAt);
        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public async Task ExistingDisabledTasks_SurviveRestarts_EvenThoughTheDefaultsNowSayEnabled()
    {
        await CreateStore().InitializeAsync(CancellationToken.None);
        // An earlier installation switched both tasks off, on purpose or from the old defaults. No
        // provenance is stored, so a stored row is a user decision from here on and no restart may undo it.
        await ExecuteSqlAsync("""
            UPDATE scheduled_tasks SET enabled=0, next_run_at=NULL WHERE task_key IN ('arrDataSync','symlinkHealthCheck');
            UPDATE scheduled_tasks SET interval_value=6, time_hour=5 WHERE task_key IN ('arrDataSync','symlinkHealthCheck');
            """);
        SqliteScheduledTaskStore restarted = CreateStore();
        ScheduledTask sync = (await restarted.GetAsync("arrDataSync", CancellationToken.None))!;
        ScheduledTask health = (await restarted.GetAsync("symlinkHealthCheck", CancellationToken.None))!;
        Assert.False(sync.Enabled);
        Assert.Null(sync.NextRunAt);
        Assert.Equal(6, sync.IntervalValue);
        Assert.Equal(0, sync.Revision);
        Assert.False(health.Enabled);
        Assert.Null(health.NextRunAt);
        Assert.Equal(5, health.TimeHour);
    }

    [Fact]
    public async Task LegacyImport_KeepsExplicitlyDisabledTask_AndOnlyMissingTasksGetTheNewDefaults()
    {
        await WriteSettingsAsync("""
            {"scheduledTasks":{"symlinkHealthCheck":{"enabled":false,"scheduleMode":"DailyAtTime","timeHour":5,"timeMinute":30}}}
            """);
        SqliteScheduledTaskStore store = CreateStore();
        ScheduledTask health = (await store.GetAsync("symlinkHealthCheck", CancellationToken.None))!;
        Assert.False(health.Enabled);
        Assert.Equal(5, health.TimeHour);
        Assert.Equal(30, health.TimeMinute);
        Assert.Null(health.NextRunAt);
        ScheduledTask sync = (await store.GetAsync("arrDataSync", CancellationToken.None))!;
        Assert.True(sync.Enabled);
        Assert.Equal(ScheduleMode.Interval, sync.ScheduleMode);
        Assert.Equal(ScheduleIntervalUnit.Hour, sync.IntervalUnit);
        Assert.Equal(1, sync.IntervalValue);
        Assert.NotNull(sync.NextRunAt);
    }

    [Fact]
    public async Task Migration_ImportsConfigurationAndDbTimes_PreservesOtherJsonAndCreatesConsistentBackups()
    {
        const string original = """
            {"language":"De","custom":{"keep":42},"scheduledTasks":{
              "arrDataSync":{"enabled":true,"scheduleMode":"EveryNDaysAtTime","intervalUnit":"Minute",
                "intervalValue":17,"timeHour":9,"timeMinute":12,"everyNDays":3,"lastRunAt":"2020-01-01T00:00:00Z"},
              "cleanup":{"lastRunAt":"2026-10-19T00:00:00Z"}}}
            """;
        await WriteSettingsAsync(original);
        await ExecuteSqlAsync("""
            PRAGMA journal_mode=WAL;
            CREATE TABLE task_run_state(task_key TEXT PRIMARY KEY,last_run_at TEXT,next_run_at TEXT);
            INSERT INTO task_run_state VALUES('arrDataSync','2026-10-20T08:00:00+02:00','2026-10-23T09:12:00+02:00');
            CREATE TABLE unrelated(value TEXT); INSERT INTO unrelated VALUES('keep');
            """);
        SqliteScheduledTaskStore store = CreateStore();
        ScheduledTask task = (await store.GetAsync("arrDataSync", CancellationToken.None))!;
        Assert.True(task.Enabled);
        Assert.Equal(ScheduleMode.EveryNDaysAtTime, task.ScheduleMode);
        Assert.Equal(ScheduleIntervalUnit.Minute, task.IntervalUnit);
        Assert.Equal(17, task.IntervalValue);
        Assert.Equal(9, task.TimeHour);
        Assert.Equal(12, task.TimeMinute);
        Assert.Equal(3, task.EveryNDays);
        Assert.Equal(DateTimeOffset.Parse("2026-10-20T06:00:00Z"), task.LastRunAt);
        Assert.Equal(DateTimeOffset.Parse("2026-10-23T07:12:00Z"), task.NextRunAt);
        Assert.Equal(TimeSpan.Zero, task.NextRunAt!.Value.Offset);
        Assert.Equal(DateTimeOffset.Parse("2026-10-19T00:00:00Z"), (await store.GetAsync("cleanup", CancellationToken.None))!.LastRunAt);
        JsonObject cleaned = JsonNode.Parse(await File.ReadAllTextAsync(SettingsFile))!.AsObject();
        Assert.False(cleaned.ContainsKey("scheduledTasks"));
        Assert.Equal(42, cleaned["custom"]!["keep"]!.GetValue<int>());
        Assert.Equal("De", cleaned["language"]!.GetValue<string>());
        string backup = Assert.Single(Directory.GetDirectories(Path.Combine(Paths.ConfigDirectory, "backups")));
        Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(backup, "settings.json")));
        await using SqliteConnection database = new($"Data Source={Path.Combine(backup, "libmanagarr.db")}");
        await database.OpenAsync();
        using SqliteCommand command = database.CreateCommand();
        command.CommandText = "SELECT value FROM unrelated";
        Assert.Equal("keep", await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task LegacyDbRowWithNulls_OverridesJsonFallback_AndValuesAreNormalized()
    {
        await WriteSettingsAsync("""
            {"scheduledTasks":{"arrDataSync":{"enabled":true,"intervalValue":0,"timeHour":99,"timeMinute":-4,
              "everyNDays":0,"lastRunAt":"2020-01-01T00:00:00Z","nextRunAt":"2020-01-02T00:00:00Z"}}}
            """);
        await ExecuteSqlAsync("""
            CREATE TABLE task_run_state(task_key TEXT PRIMARY KEY,last_run_at TEXT,next_run_at TEXT);
            INSERT INTO task_run_state VALUES('arrDataSync',NULL,NULL);
            """);
        ScheduledTask task = (await CreateStore().GetAsync("arrDataSync", CancellationToken.None))!;
        Assert.Null(task.LastRunAt);
        Assert.Equal(clock.GetUtcNow().AddHours(1), task.NextRunAt);
        Assert.Equal(1, task.IntervalValue);
        Assert.Equal(23, task.TimeHour);
        Assert.Equal(0, task.TimeMinute);
        Assert.Equal(1, task.EveryNDays);
    }

    [Fact]
    public async Task RestartAfterCommitBeforeCleanup_DoesNotReimportOrOverwriteNewConfiguration()
    {
        const string original = """{"custom":"preserved","scheduledTasks":{"arrDataSync":{"enabled":true,"intervalValue":7}}}""";
        await WriteSettingsAsync(original);
        SqliteScheduledTaskStore store = CreateStore();
        ScheduledTask task = (await store.GetAsync("arrDataSync", CancellationToken.None))!;
        task.IntervalValue = 2;
        task.NextRunAt = clock.GetUtcNow().AddHours(2);
        Assert.True(await store.UpdateConfigurationAsync(task, task.Revision, CancellationToken.None));
        // Recreate exactly the file state left by a crash or a failed cleanup after commit.
        await WriteSettingsAsync(original);
        clock.Advance(TimeSpan.FromHours(1));
        ScheduledTask restarted = (await CreateStore().GetAsync("arrDataSync", CancellationToken.None))!;
        Assert.Equal(2, restarted.IntervalValue);
        Assert.Equal(1, restarted.Revision);
        Assert.Equal(task.NextRunAt, restarted.NextRunAt);
        Assert.DoesNotContain("scheduledTasks", await File.ReadAllTextAsync(SettingsFile));
        Assert.Single(Directory.GetDirectories(Path.Combine(Paths.ConfigDirectory, "backups")));
    }

    [Fact]
    public async Task CleanupFailure_IsRetriedWithoutImportingAgain()
    {
        await WriteSettingsAsync("""{"custom":true,"scheduledTasks":{"arrDataSync":{"enabled":true}}}""");
        using (FileStream locked = new(SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            // Windows permits reading the file but prevents its atomic replacement.
            await CreateStore().InitializeAsync(CancellationToken.None);
        }
        ScheduledTask task = (await CreateStore().GetAsync("arrDataSync", CancellationToken.None))!;
        Assert.True(task.Enabled);
        Assert.DoesNotContain("scheduledTasks", await File.ReadAllTextAsync(SettingsFile));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    public async Task CorruptFile_StopsMigrationWithoutChangingOriginal(string json)
    {
        await WriteSettingsAsync(json);
        await Assert.ThrowsAsync<InvalidDataException>(() => CreateStore().InitializeAsync(CancellationToken.None));
        Assert.Equal(json, await File.ReadAllTextAsync(SettingsFile));
        Assert.False(Directory.Exists(Path.Combine(Paths.ConfigDirectory, "backups")));
    }

    [Fact]
    public async Task ImportFailure_RollsBackSchemaAndLeavesOriginalAndLegacyStateIntact()
    {
        const string original = """{"scheduledTasks":{"arrDataSync":{"intervalValue":"invalid"}}}""";
        await WriteSettingsAsync(original);
        await ExecuteSqlAsync("""
            CREATE TABLE task_run_state(task_key TEXT PRIMARY KEY,last_run_at TEXT,next_run_at TEXT);
            INSERT INTO task_run_state VALUES('arrDataSync',NULL,NULL);
            """);
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => CreateStore().InitializeAsync(CancellationToken.None));
        Assert.Equal(original, await File.ReadAllTextAsync(SettingsFile));
        await using SqliteConnection database = new($"Data Source={Paths.DatabasePath}");
        await database.OpenAsync();
        using SqliteCommand command = database.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='schema_migrations' OR name='scheduled_tasks'";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT COUNT(*) FROM task_run_state";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task NewDefinition_IsSeededOnce_UnknownRowsAndOtherTasksArePreserved()
    {
        SqliteScheduledTaskStore initial = CreateStore();
        await initial.InitializeAsync(CancellationToken.None);
        await ExecuteSqlAsync("""
            INSERT INTO scheduled_tasks SELECT 'unknown',enabled,schedule_mode,interval_unit,interval_value,
              time_hour,time_minute,every_n_days,last_run_at,next_run_at,revision FROM scheduled_tasks WHERE task_key='cleanup';
            """);
        ScheduledTaskCatalog catalog = CreateCatalog(new ScheduledTaskDefinition("thirdTask", false, true, () => new() { IntervalValue = 5 }));
        SqliteScheduledTaskStore upgraded = CreateStore(catalog);
        ScheduledTaskService service = new(upgraded, catalog, clock);
        Assert.Equal(5, (await service.GetAllAsync(CancellationToken.None)).Count);
        Assert.Equal(6, (await upgraded.GetAllAsync(CancellationToken.None)).Count);
        ScheduledTask third = (await upgraded.GetAsync("thirdTask", CancellationToken.None))!;
        third.IntervalValue = 9;
        Assert.True(await upgraded.UpdateConfigurationAsync(third, 0, CancellationToken.None));
        Assert.Equal(9, (await CreateStore(catalog).GetAsync("thirdTask", CancellationToken.None))!.IntervalValue);
        Assert.NotNull(await upgraded.GetAsync("unknown", CancellationToken.None));
        Assert.Equal(1, (await upgraded.GetAsync("arrDataSync", CancellationToken.None))!.IntervalValue);
    }

    [Fact]
    public async Task ConfigurationUpdate_IsNarrowAndRejectsStaleRevision()
    {
        await WriteSettingsAsync("""{"language":"De","custom":42}""");
        SqliteScheduledTaskStore store = CreateStore();
        ScheduledTaskService service = new(store, CreateCatalog(), clock);
        ScheduledTask before = await service.GetAsync("arrDataSync", CancellationToken.None);
        ScheduledTask cleanup = await service.GetAsync("cleanup", CancellationToken.None);
        string original = await File.ReadAllTextAsync(SettingsFile);
        ScheduledTask updated = await service.UpdateAsync("arrDataSync", Request(before, true), CancellationToken.None);
        Assert.Equal(clock.GetUtcNow().AddHours(1), updated.NextRunAt);
        Assert.Equal(1, updated.Revision);
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(updated.NextRunAt, (await service.GetAsync("arrDataSync", CancellationToken.None)).NextRunAt);
        ScheduledTaskException conflict = await Assert.ThrowsAsync<ScheduledTaskException>(() => service.UpdateAsync("arrDataSync", Request(before, false), CancellationToken.None));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal(original, await File.ReadAllTextAsync(SettingsFile));
        Assert.Equal(cleanup.NextRunAt, (await service.GetAsync("cleanup", CancellationToken.None)).NextRunAt);
        Assert.False(await store.UpdateRunAsync("arrDataSync", 0, clock.GetUtcNow(), null, CancellationToken.None));
        Assert.True((await service.GetAsync("arrDataSync", CancellationToken.None)).Enabled);
    }

    [Fact]
    public async Task MarkDueNow_PullsTheFutureRunToThisInstantWithoutTouchingConfiguration()
    {
        SqliteScheduledTaskStore store = CreateStore();
        ScheduledTaskService service = new(store, CreateCatalog(), clock);
        ScheduledTask before = await service.GetAsync("releaseCheck", CancellationToken.None);
        Assert.True(before.NextRunAt > clock.GetUtcNow());

        await service.MarkDueNowAsync("releaseCheck", CancellationToken.None);

        ScheduledTask after = await service.GetAsync("releaseCheck", CancellationToken.None);
        Assert.Equal(clock.GetUtcNow(), after.NextRunAt);
        Assert.Equal(before.IntervalValue, after.IntervalValue);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Null(after.LastRunAt);
    }

    [Fact]
    public async Task MarkDueNow_LeavesDisabledTasksAndAlreadyDueTasksAlone()
    {
        SqliteScheduledTaskStore store = CreateStore();
        ScheduledTaskService service = new(store, CreateCatalog(), clock);
        await service.MarkDueNowAsync("releaseCheck", CancellationToken.None);
        DateTimeOffset dueAt = clock.GetUtcNow();
        // A second startup while the task already sits due must not push it forward again.
        clock.Advance(TimeSpan.FromMinutes(30));
        await service.MarkDueNowAsync("releaseCheck", CancellationToken.None);
        Assert.Equal(dueAt, (await service.GetAsync("releaseCheck", CancellationToken.None)).NextRunAt);

        await ExecuteSqlAsync("UPDATE scheduled_tasks SET enabled=0, next_run_at=NULL WHERE task_key='releaseCheck';");
        await service.MarkDueNowAsync("releaseCheck", CancellationToken.None);
        ScheduledTask disabled = (await store.GetAsync("releaseCheck", CancellationToken.None))!;
        Assert.False(disabled.Enabled);
        Assert.Null(disabled.NextRunAt);
    }

    internal static ScheduledTaskUpdateRequest Request(ScheduledTask task, bool enabled, int interval = 1) => new()
    {
        Revision = task.Revision, Enabled = enabled, ScheduleMode = task.ScheduleMode,
        IntervalUnit = task.IntervalUnit, IntervalValue = interval, TimeHour = task.TimeHour,
        TimeMinute = task.TimeMinute, EveryNDays = task.EveryNDays
    };

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        environment.Dispose();
    }
}
