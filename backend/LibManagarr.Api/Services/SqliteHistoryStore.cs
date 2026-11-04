using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services.Backup;

namespace LibManagarr.Api.Services;

/// <summary>
/// Stores the sync data (histories, instance snapshots and library filter values) in a SQLite database inside the configured config directory (data/libmanagarr.db
/// below the content root unless LIBMANAGARR_CONFIG_DIR points elsewhere). Writes are
/// serialized through a single gate; the schema is created lazily on first use.
/// </summary>
public sealed class SqliteHistoryStore : IHistoryStore
{

    private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] SchemaStatements =
    [
        "PRAGMA journal_mode = WAL",
        """
        CREATE TABLE IF NOT EXISTS snapshot_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            kind TEXT NOT NULL,
            instance_id TEXT NOT NULL,
            instance_name TEXT NOT NULL,
            synced_at TEXT NOT NULL,
            payload TEXT NOT NULL
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_snapshot_history_kind_synced_at ON snapshot_history (kind, synced_at)",
        """
        CREATE TABLE IF NOT EXISTS library_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            source_key TEXT NOT NULL,
            kind TEXT NOT NULL,
            instance_id TEXT NOT NULL,
            instance_name TEXT NOT NULL,
            occurred_at TEXT NOT NULL,
            media_key TEXT NOT NULL,
            change_type TEXT NOT NULL,
            entity_title TEXT NOT NULL,
            series_title TEXT,
            file_path TEXT,
            size_bytes INTEGER,
            delta_size_bytes INTEGER NOT NULL
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_library_history_kind ON library_history (kind)",
        "CREATE INDEX IF NOT EXISTS ix_library_history_kind_instance_id ON library_history (kind, instance_id)",
        """
        CREATE TABLE IF NOT EXISTS instance_snapshots (
            kind TEXT NOT NULL,
            instance_id TEXT NOT NULL,
            synced_at TEXT NOT NULL,
            payload TEXT NOT NULL,
            PRIMARY KEY (kind, instance_id)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS library_filter_values (
            instance_id TEXT NOT NULL PRIMARY KEY,
            payload TEXT NOT NULL
        )
        """
    ];

    private readonly string connectionString;
    private readonly BackupWriteGate storageGate;
    private bool schemaApplied;

    public SqliteHistoryStore(StoragePaths storagePaths, BackupOperationCoordinator writes)
    {
        Directory.CreateDirectory(storagePaths.ConfigDirectory);
        storageGate = new BackupWriteGate(writes);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = storagePaths.DatabasePath
        }.ToString();
    }

    public async Task AppendSnapshotHistoryAsync(ArrApplicationKind kind, string instanceId, string instanceName, DateTimeOffset syncedAt, object payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        string payloadJson = JsonSerializer.Serialize(payload, payload.GetType(), PayloadSerializerOptions);

        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO snapshot_history (kind, instance_id, instance_name, synced_at, payload)
                VALUES ($kind, $instanceId, $instanceName, $syncedAt, $payload)
                """;
            command.Parameters.AddWithValue("$kind", KindToText(kind));
            command.Parameters.AddWithValue("$instanceId", instanceId);
            command.Parameters.AddWithValue("$instanceName", instanceName);
            command.Parameters.AddWithValue("$syncedAt", syncedAt.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$payload", payloadJson);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task ReplaceLibraryHistoryAsync(ArrApplicationKind kind, IReadOnlyList<LibraryChangeHistoryEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await using (SqliteCommand delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM library_history WHERE kind = $kind";
                delete.Parameters.AddWithValue("$kind", KindToText(kind));
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            await using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO library_history (
                    source_key, kind, instance_id, instance_name, occurred_at, media_key,
                    change_type, entity_title, series_title, file_path, size_bytes, delta_size_bytes)
                VALUES (
                    $sourceKey, $kind, $instanceId, $instanceName, $occurredAt, $mediaKey,
                    $changeType, $entityTitle, $seriesTitle, $filePath, $sizeBytes, $deltaSizeBytes)
                """;
            SqliteParameter sourceKey = insert.Parameters.Add("$sourceKey", SqliteType.Text);
            SqliteParameter kindParameter = insert.Parameters.Add("$kind", SqliteType.Text);
            SqliteParameter instanceId = insert.Parameters.Add("$instanceId", SqliteType.Text);
            SqliteParameter instanceName = insert.Parameters.Add("$instanceName", SqliteType.Text);
            SqliteParameter occurredAt = insert.Parameters.Add("$occurredAt", SqliteType.Text);
            SqliteParameter mediaKey = insert.Parameters.Add("$mediaKey", SqliteType.Text);
            SqliteParameter changeType = insert.Parameters.Add("$changeType", SqliteType.Text);
            SqliteParameter entityTitle = insert.Parameters.Add("$entityTitle", SqliteType.Text);
            SqliteParameter seriesTitle = insert.Parameters.Add("$seriesTitle", SqliteType.Text);
            SqliteParameter filePath = insert.Parameters.Add("$filePath", SqliteType.Text);
            SqliteParameter sizeBytes = insert.Parameters.Add("$sizeBytes", SqliteType.Integer);
            SqliteParameter deltaSizeBytes = insert.Parameters.Add("$deltaSizeBytes", SqliteType.Integer);

            foreach (LibraryChangeHistoryEntry entry in entries)
            {
                sourceKey.Value = entry.SourceKey;
                kindParameter.Value = KindToText(entry.Kind);
                instanceId.Value = entry.InstanceId;
                instanceName.Value = entry.InstanceName;
                occurredAt.Value = entry.OccurredAt.ToString("O", CultureInfo.InvariantCulture);
                mediaKey.Value = entry.MediaKey;
                changeType.Value = entry.ChangeType.ToString();
                entityTitle.Value = entry.EntityTitle;
                seriesTitle.Value = (object?)entry.SeriesTitle ?? DBNull.Value;
                filePath.Value = (object?)entry.FilePath ?? DBNull.Value;
                sizeBytes.Value = (object?)entry.SizeBytes ?? DBNull.Value;
                deltaSizeBytes.Value = entry.DeltaSizeBytes;
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task<IReadOnlyList<LibraryChangeHistoryEntry>> GetLibraryHistoryAsync(ArrApplicationKind? kind, CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT source_key, kind, instance_id, instance_name, occurred_at, media_key,
                       change_type, entity_title, series_title, file_path, size_bytes, delta_size_bytes
                FROM library_history
                """;
            if (kind is not null)
            {
                command.CommandText += "\nWHERE kind = $kind";
                command.Parameters.AddWithValue("$kind", KindToText(kind.Value));
            }

            command.CommandText += "\nORDER BY occurred_at, source_key";

            List<LibraryChangeHistoryEntry> entries = [];
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                entries.Add(ReadEntry(reader));
            }

            return entries;
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task<bool> HasLibraryHistoryAsync(ArrApplicationKind kind, string instanceId, CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM library_history WHERE kind = $kind AND instance_id = $instanceId LIMIT 1";
            command.Parameters.AddWithValue("$kind", KindToText(kind));
            command.Parameters.AddWithValue("$instanceId", instanceId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken);
        }
        finally
        {
            storageGate.Release();
        }
    }

    public Task ReplaceSonarrInstanceSnapshotsAsync(IReadOnlyList<SonarrInstanceSnapshot> snapshots, CancellationToken cancellationToken) =>
        ReplaceInstanceSnapshotsAsync(ArrApplicationKind.Sonarr, snapshots, cancellationToken);

    public Task ReplaceRadarrInstanceSnapshotsAsync(IReadOnlyList<RadarrInstanceSnapshot> snapshots, CancellationToken cancellationToken) =>
        ReplaceInstanceSnapshotsAsync(ArrApplicationKind.Radarr, snapshots, cancellationToken);

    public Task<IReadOnlyList<SonarrInstanceSnapshot>> GetSonarrInstanceSnapshotsAsync(CancellationToken cancellationToken) =>
        GetInstanceSnapshotsAsync<SonarrInstanceSnapshot>(ArrApplicationKind.Sonarr, cancellationToken);

    public Task<IReadOnlyList<RadarrInstanceSnapshot>> GetRadarrInstanceSnapshotsAsync(CancellationToken cancellationToken) =>
        GetInstanceSnapshotsAsync<RadarrInstanceSnapshot>(ArrApplicationKind.Radarr, cancellationToken);

    private async Task ReplaceInstanceSnapshotsAsync<T>(ArrApplicationKind kind, IReadOnlyList<T> snapshots, CancellationToken cancellationToken)
        where T : IInstanceSnapshot
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await using (SqliteCommand delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM instance_snapshots WHERE kind = $kind";
                delete.Parameters.AddWithValue("$kind", KindToText(kind));
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            await using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO instance_snapshots (kind, instance_id, synced_at, payload)
                VALUES ($kind, $instanceId, $syncedAt, $payload)
                """;
            SqliteParameter kindParameter = insert.Parameters.Add("$kind", SqliteType.Text);
            SqliteParameter instanceId = insert.Parameters.Add("$instanceId", SqliteType.Text);
            SqliteParameter syncedAt = insert.Parameters.Add("$syncedAt", SqliteType.Text);
            SqliteParameter payload = insert.Parameters.Add("$payload", SqliteType.Text);

            foreach (T snapshot in snapshots)
            {
                kindParameter.Value = KindToText(kind);
                instanceId.Value = snapshot.InstanceId;
                syncedAt.Value = snapshot.SyncedAt.ToString("O", CultureInfo.InvariantCulture);
                payload.Value = JsonSerializer.Serialize(snapshot, PayloadSerializerOptions);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            storageGate.Release();
        }
    }

    private async Task<IReadOnlyList<T>> GetInstanceSnapshotsAsync<T>(ArrApplicationKind kind, CancellationToken cancellationToken)
        where T : class
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT payload FROM instance_snapshots
                WHERE kind = $kind
                ORDER BY instance_id
                """;
            command.Parameters.AddWithValue("$kind", KindToText(kind));

            List<T> snapshots = [];
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                snapshots.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), PayloadSerializerOptions)
                    ?? throw new JsonException("The stored instance snapshot payload is not a JSON object."));
            }

            return snapshots;
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task ReplaceLibraryFilterValuesAsync(IReadOnlyList<LibraryFilterValues> values, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);

        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await using (SqliteCommand delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM library_filter_values";
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            await using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO library_filter_values (instance_id, payload) VALUES ($instanceId, $payload)";
            SqliteParameter instanceId = insert.Parameters.Add("$instanceId", SqliteType.Text);
            SqliteParameter payload = insert.Parameters.Add("$payload", SqliteType.Text);

            foreach (LibraryFilterValues value in values)
            {
                instanceId.Value = value.InstanceId;
                payload.Value = JsonSerializer.Serialize(value, PayloadSerializerOptions);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task<IReadOnlyList<LibraryFilterValues>> GetLibraryFilterValuesAsync(CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT payload FROM library_filter_values ORDER BY instance_id";

            List<LibraryFilterValues> values = [];
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                values.Add(JsonSerializer.Deserialize<LibraryFilterValues>(reader.GetString(0), PayloadSerializerOptions)
                    ?? throw new JsonException("The stored library filter values payload is not a JSON object."));
            }

            return values;
        }
        finally
        {
            storageGate.Release();
        }
    }

    private static LibraryChangeHistoryEntry ReadEntry(SqliteDataReader reader)
    {
        return new LibraryChangeHistoryEntry
        {
            SourceKey = reader.GetString(0),
            Kind = ParseKind(reader.GetString(1)),
            InstanceId = reader.GetString(2),
            InstanceName = reader.GetString(3),
            OccurredAt = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            MediaKey = reader.GetString(5),
            ChangeType = Enum.Parse<LibraryChangeType>(reader.GetString(6), ignoreCase: true),
            EntityTitle = reader.GetString(7),
            SeriesTitle = reader.IsDBNull(8) ? null : reader.GetString(8),
            FilePath = reader.IsDBNull(9) ? null : reader.GetString(9),
            SizeBytes = reader.IsDBNull(10) ? null : reader.GetInt64(10),
            DeltaSizeBytes = reader.GetInt64(11)
        };
    }

    private static string KindToText(ArrApplicationKind kind) => kind.ToString().ToLowerInvariant();

    private static ArrApplicationKind ParseKind(string value) => Enum.Parse<ArrApplicationKind>(value, ignoreCase: true);

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(connectionString);
        bool usable = false;
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using (SqliteCommand pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA busy_timeout = 5000";
                await pragma.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!schemaApplied)
            {
                foreach (string statement in SchemaStatements)
                {
                    await using SqliteCommand schema = connection.CreateCommand();
                    schema.CommandText = statement;
                    await schema.ExecuteNonQueryAsync(cancellationToken);
                }

                schemaApplied = true;
            }

            usable = true;
            return connection;
        }
        finally
        {
            if (!usable)
            {
                await connection.DisposeAsync();
            }
        }
    }
}
