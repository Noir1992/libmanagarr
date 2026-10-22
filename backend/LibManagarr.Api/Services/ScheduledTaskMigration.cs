using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

internal sealed class ScheduledTaskMigration(StoragePaths paths, ScheduledTaskCatalog catalog, TimeProvider clock, ILogger logger)
{
    private const string MigrationId = "scheduled-tasks-v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }, WriteIndented = true
    };

    public async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        bool migrated = await IsMigratedAsync(connection, cancellationToken);
        // Parse the original file before any schema or settings mutation. A committed import
        // never reads legacy configuration again, even if file cleanup failed last time.
        JsonObject? original = await ReadSettingsAsync(cancellationToken);
        if (!migrated) await BackupAsync(connection, cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        using (SqliteCommand schema = connection.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (migration_id TEXT PRIMARY KEY, applied_at TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS scheduled_tasks (
                    task_key TEXT NOT NULL PRIMARY KEY,
                    enabled INTEGER NOT NULL CHECK(enabled IN (0,1)),
                    schedule_mode INTEGER NOT NULL CHECK(schedule_mode BETWEEN 0 AND 2),
                    interval_unit INTEGER NOT NULL CHECK(interval_unit BETWEEN 0 AND 5),
                    interval_value INTEGER NOT NULL CHECK(interval_value BETWEEN 1 AND 1000),
                    time_hour INTEGER NOT NULL CHECK(time_hour BETWEEN 0 AND 23),
                    time_minute INTEGER NOT NULL CHECK(time_minute BETWEEN 0 AND 59),
                    every_n_days INTEGER NOT NULL CHECK(every_n_days BETWEEN 1 AND 3650),
                    last_run_at TEXT, next_run_at TEXT,
                    revision INTEGER NOT NULL DEFAULT 0 CHECK(revision >= 0),
                    CHECK(enabled = 1 OR next_run_at IS NULL)
                );
                """;
            await schema.ExecuteNonQueryAsync(cancellationToken);
        }

        JsonObject? legacy = migrated ? null : FindProperty(original, "scheduledTasks")?.AsObject();
        foreach (ScheduledTaskDefinition definition in catalog.Definitions)
        {
            if (await InitializeExistingAsync(connection, transaction, definition.TaskKey, cancellationToken)) continue;
            ScheduledTaskSettings settings = definition.CreateDefaults();
            if (!migrated)
            {
                JsonNode? node = FindProperty(legacy, definition.TaskKey);
                if (node is not null)
                {
                    LegacyTaskConfiguration imported = node.Deserialize<LegacyTaskConfiguration>(JsonOptions)
                        ?? throw new InvalidDataException("Invalid legacy scheduled task configuration.");
                    // Cleanup's schedule was always fixed by the previous normalizer.
                    if (definition.ReadOnly)
                    {
                        settings.LastRunAt = imported.LastRunAt;
                        settings.NextRunAt = imported.NextRunAt;
                    }
                    else settings = Normalize(imported);
                }
                await ImportRunStateAsync(connection, transaction, definition.TaskKey, settings, cancellationToken);
            }
            ScheduledTask task = new()
            {
                TaskKey = definition.TaskKey, Enabled = settings.Enabled, ScheduleMode = settings.ScheduleMode,
                IntervalUnit = settings.IntervalUnit, IntervalValue = settings.IntervalValue,
                TimeHour = settings.TimeHour, TimeMinute = settings.TimeMinute, EveryNDays = settings.EveryNDays,
                LastRunAt = settings.LastRunAt,
                NextRunAt = settings.Enabled ? settings.NextRunAt ?? ScheduleCalculator.GetNextRunAt(settings, clock.GetUtcNow(), clock.LocalTimeZone) : null
            };
            using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO scheduled_tasks (task_key,enabled,schedule_mode,interval_unit,interval_value,
                    time_hour,time_minute,every_n_days,last_run_at,next_run_at)
                VALUES ($key,$enabled,$mode,$unit,$value,$hour,$minute,$days,$last,$next)
                """;
            SqliteScheduledTaskStore.AddConfiguration(insert, task);
            insert.Parameters.AddWithValue("$last", SqliteScheduledTaskStore.Timestamp(task.LastRunAt));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        using (SqliteCommand marker = connection.CreateCommand())
        {
            marker.Transaction = transaction;
            marker.CommandText = "INSERT OR IGNORE INTO schema_migrations VALUES ($id,$at)";
            marker.Parameters.AddWithValue("$id", MigrationId);
            marker.Parameters.AddWithValue("$at", SqliteScheduledTaskStore.Timestamp(clock.GetUtcNow()));
            await marker.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        await CleanupSettingsAsync(original, cancellationToken);
    }

    private async Task<JsonObject?> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        string file = Path.Combine(paths.ConfigDirectory, "settings.json");
        if (!File.Exists(file)) return null;
        try
        {
            return JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken)) as JsonObject
                ?? throw new InvalidDataException("settings.json must contain a JSON object; task migration stopped.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("settings.json is damaged; task migration stopped. Restore the original configuration before restarting.", exception);
        }
    }

    private async Task BackupAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(paths.ConfigDirectory, "backups", "scheduled-tasks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string settings = Path.Combine(paths.ConfigDirectory, "settings.json");
        if (File.Exists(settings))
        {
            string destination = Path.Combine(directory, "settings.json");
            await File.WriteAllBytesAsync(destination, await File.ReadAllBytesAsync(settings, cancellationToken), cancellationToken);
            ProtectFile(destination);
        }
        string database = Path.Combine(directory, "libmanagarr.db");
        await using SqliteConnection backup = new(new SqliteConnectionStringBuilder { DataSource = database }.ToString());
        await backup.OpenAsync(cancellationToken);
        connection.BackupDatabase(backup);
        ProtectFile(database);
    }

    private static void ProtectFile(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private async Task CleanupSettingsAsync(JsonObject? original, CancellationToken cancellationToken)
    {
        if (original is null) return;
        string[] keys = original.Select(pair => pair.Key).Where(key => key.Equals("scheduledTasks", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (keys.Length == 0) return;
        foreach (string key in keys) original.Remove(key);
        try
        {
            await AtomicSettingsFile.WriteAsync(Path.Combine(paths.ConfigDirectory, "settings.json"),
                JsonSerializer.SerializeToUtf8Bytes(original, JsonOptions), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Task migration committed; settings.json cleanup will be retried at the next start.");
        }
    }

    private static JsonNode? FindProperty(JsonObject? obj, string name) => obj?
        .FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    // This import contract is deliberately separate from AppSettings and the current API.
    private sealed record LegacyTaskConfiguration(
        bool? Enabled = null, ScheduleMode? ScheduleMode = null, ScheduleIntervalUnit? IntervalUnit = null,
        int? IntervalValue = null, int? TimeHour = null, int? TimeMinute = null, int? EveryNDays = null,
        DateTimeOffset? LastRunAt = null, DateTimeOffset? NextRunAt = null);

    private static ScheduledTaskSettings Normalize(LegacyTaskConfiguration task) => new()
    {
        Enabled = task.Enabled ?? false,
        ScheduleMode = task.ScheduleMode is { } mode && Enum.IsDefined(mode) ? mode : ScheduleMode.Interval,
        IntervalUnit = task.IntervalUnit is { } unit && Enum.IsDefined(unit) ? unit : ScheduleIntervalUnit.Hour,
        IntervalValue = Math.Clamp(task.IntervalValue ?? 1, 1, 1000),
        TimeHour = Math.Clamp(task.TimeHour ?? 0, 0, 23),
        TimeMinute = Math.Clamp(task.TimeMinute ?? 0, 0, 59),
        EveryNDays = Math.Clamp(task.EveryNDays ?? 1, 1, 3650),
        LastRunAt = task.LastRunAt,
        NextRunAt = task.NextRunAt
    };

    private static async Task<bool> IsMigratedAsync(SqliteConnection connection, CancellationToken token)
    {
        if (!await HasTableAsync(connection, null, "schema_migrations", token)) return false;
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE migration_id=$id";
        command.Parameters.AddWithValue("$id", MigrationId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token)) > 0;
    }

    private async Task<bool> InitializeExistingAsync(SqliteConnection connection, SqliteTransaction transaction, string key, CancellationToken token)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM scheduled_tasks WHERE task_key=$key";
        command.Parameters.AddWithValue("$key", key);
        ScheduledTask existing;
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) return false;
            existing = SqliteScheduledTaskStore.Read(reader);
        }
        if (existing.Enabled && existing.NextRunAt is null)
        {
            command.CommandText = "UPDATE scheduled_tasks SET next_run_at=$next WHERE task_key=$key AND next_run_at IS NULL";
            command.Parameters.AddWithValue("$next", SqliteScheduledTaskStore.Timestamp(
                ScheduleCalculator.GetNextRunAt(existing, clock.GetUtcNow(), clock.LocalTimeZone)));
            await command.ExecuteNonQueryAsync(token);
        }
        return true;
    }

    private static async Task<bool> HasTableAsync(SqliteConnection connection, SqliteTransaction? transaction, string table, CancellationToken token)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";
        command.Parameters.AddWithValue("$table", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token)) > 0;
    }

    private static async Task ImportRunStateAsync(SqliteConnection connection, SqliteTransaction transaction,
        string key, ScheduledTaskSettings settings, CancellationToken token)
    {
        if (!await HasTableAsync(connection, transaction, "task_run_state", token)) return;
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT last_run_at,next_run_at FROM task_run_state WHERE task_key=$key";
        command.Parameters.AddWithValue("$key", key);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return;
        settings.LastRunAt = SqliteScheduledTaskStore.ReadTimestamp(reader, 0);
        settings.NextRunAt = SqliteScheduledTaskStore.ReadTimestamp(reader, 1);
    }
}
