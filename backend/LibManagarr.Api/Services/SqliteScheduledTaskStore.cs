using System.Globalization;
using Microsoft.Data.Sqlite;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services.Backup;

namespace LibManagarr.Api.Services;

public sealed class SqliteScheduledTaskStore(
    StoragePaths paths, ScheduledTaskCatalog catalog, TimeProvider clock, BackupOperationCoordinator writes,
    ILogger<SqliteScheduledTaskStore> logger) : IScheduledTaskStore
{
    private readonly SemaphoreSlim initializationGate = new(1, 1);

    /// <summary>
    /// Task rows live in the same database as the library history, so their updates join the writer
    /// coordination here instead of in every caller. An export that waits for open writers therefore
    /// also waits for a task state that is about to be written.
    /// </summary>
    private readonly BackupWriteGate writeGate = new(writes);
    private bool initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (initialized) return;
            Directory.CreateDirectory(paths.ConfigDirectory);
            await using SqliteConnection connection = await OpenAsync(cancellationToken);
            ScheduledTaskMigration migration = new(paths, catalog, clock, logger);
            await migration.ApplyAsync(connection, cancellationToken);
            initialized = true;
        }
        finally { initializationGate.Release(); }
    }

    public async Task<IReadOnlyList<ScheduledTask>> GetAllAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM scheduled_tasks ORDER BY task_key";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<ScheduledTask> result = [];
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    public async Task<ScheduledTask?> GetAsync(string taskKey, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM scheduled_tasks WHERE task_key = $key";
        command.Parameters.AddWithValue("$key", taskKey);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<bool> UpdateConfigurationAsync(ScheduledTask task, long expectedRevision, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenAsync(cancellationToken);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE scheduled_tasks SET enabled=$enabled, schedule_mode=$mode, interval_unit=$unit,
                    interval_value=$value, time_hour=$hour, time_minute=$minute, every_n_days=$days,
                    next_run_at=$next, revision=revision+1
                WHERE task_key=$key AND revision=$revision
                """;
            AddConfiguration(command, task);
            command.Parameters.AddWithValue("$revision", expectedRevision);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task<bool> UpdateRunAsync(string taskKey, long expectedRevision, DateTimeOffset? lastRunAt,
        DateTimeOffset? nextRunAt, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenAsync(cancellationToken);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE scheduled_tasks SET last_run_at=COALESCE($last, last_run_at), next_run_at=$next
                WHERE task_key=$key AND revision=$revision
                """;
            command.Parameters.AddWithValue("$key", taskKey);
            command.Parameters.AddWithValue("$revision", expectedRevision);
            command.Parameters.AddWithValue("$last", Timestamp(lastRunAt));
            command.Parameters.AddWithValue("$next", Timestamp(nextRunAt));
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        finally
        {
            writeGate.Release();
        }
    }

    internal static void AddConfiguration(SqliteCommand command, ScheduledTask task)
    {
        command.Parameters.AddWithValue("$key", task.TaskKey);
        command.Parameters.AddWithValue("$enabled", task.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$mode", (int)task.ScheduleMode);
        command.Parameters.AddWithValue("$unit", (int)task.IntervalUnit);
        command.Parameters.AddWithValue("$value", task.IntervalValue);
        command.Parameters.AddWithValue("$hour", task.TimeHour);
        command.Parameters.AddWithValue("$minute", task.TimeMinute);
        command.Parameters.AddWithValue("$days", task.EveryNDays);
        command.Parameters.AddWithValue("$next", Timestamp(task.NextRunAt));
    }

    internal static object Timestamp(DateTimeOffset? date) =>
        (object?)date?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value;

    internal static DateTimeOffset? ReadTimestamp(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal)
        ? null : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture).ToUniversalTime();

    internal static ScheduledTask Read(SqliteDataReader reader) => new()
    {
        TaskKey = reader.GetString(0), Enabled = reader.GetBoolean(1),
        ScheduleMode = (ScheduleMode)reader.GetInt32(2), IntervalUnit = (ScheduleIntervalUnit)reader.GetInt32(3),
        IntervalValue = reader.GetInt32(4), TimeHour = reader.GetInt32(5), TimeMinute = reader.GetInt32(6),
        EveryNDays = reader.GetInt32(7), LastRunAt = ReadTimestamp(reader, 8), NextRunAt = ReadTimestamp(reader, 9),
        Revision = reader.GetInt64(10)
    };

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
