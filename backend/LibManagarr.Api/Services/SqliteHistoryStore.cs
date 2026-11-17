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

    public async Task ReplaceLibraryHistoryAsync(ArrApplicationKind kind, IReadOnlyList<LibraryChangeHistoryEntry> entries, CancellationToken cancellationToken,
        IReadOnlyList<LibraryChangeHistoryEntry>? rawEntries = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await WriteLibraryHistoryAsync(connection, transaction, kind, entries, rawEntries, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            storageGate.Release();
        }
    }

    private static async Task WriteLibraryHistoryAsync(SqliteConnection connection, SqliteTransaction transaction,
        ArrApplicationKind kind, IReadOnlyList<LibraryChangeHistoryEntry> entries,
        IReadOnlyList<LibraryChangeHistoryEntry>? rawEntries, CancellationToken cancellationToken)
    {
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
            change_type, entity_title, series_title, file_path, size_bytes, delta_size_bytes, metadata)
        VALUES (
            $sourceKey, $kind, $instanceId, $instanceName, $occurredAt, $mediaKey,
            $changeType, $entityTitle, $seriesTitle, $filePath, $sizeBytes, $deltaSizeBytes, $metadata)
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
        SqliteParameter metadata = insert.Parameters.Add("$metadata", SqliteType.Text);

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
            metadata.Value = JsonSerializer.Serialize(entry, PayloadSerializerOptions);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        if (rawEntries is not null)
        {
            await using SqliteCommand clear = connection.CreateCommand();
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM library_history_events WHERE kind = $kind";
            clear.Parameters.AddWithValue("$kind", KindToText(kind));
            await clear.ExecuteNonQueryAsync(cancellationToken);
            foreach (LibraryChangeHistoryEntry entry in rawEntries)
            {
                await using SqliteCommand raw = connection.CreateCommand();
                raw.Transaction = transaction;
                raw.CommandText = "INSERT INTO library_history_events (kind, instance_id, source_key, payload) VALUES ($kind, $instance, $key, $payload)";
                raw.Parameters.AddWithValue("$kind", KindToText(kind));
                raw.Parameters.AddWithValue("$instance", entry.InstanceId);
                raw.Parameters.AddWithValue("$key", entry.SourceKey);
                raw.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(entry, PayloadSerializerOptions));
                await raw.ExecuteNonQueryAsync(cancellationToken);
            }
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
                       change_type, entity_title, series_title, file_path, size_bytes, delta_size_bytes, metadata
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

    public async Task<IReadOnlyList<LibraryChangeHistoryEntry>> GetLibraryHistoryEventsAsync(ArrApplicationKind kind, CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT payload FROM library_history_events WHERE kind = $kind ORDER BY instance_id, source_key";
            command.Parameters.AddWithValue("$kind", KindToText(kind));
            List<LibraryChangeHistoryEntry> entries = [];
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                entries.Add(JsonSerializer.Deserialize<LibraryChangeHistoryEntry>(reader.GetString(0), PayloadSerializerOptions)!);
            return entries;
        }
        finally { storageGate.Release(); }
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
        if (reader.FieldCount > 12 && !reader.IsDBNull(12))
            return JsonSerializer.Deserialize<LibraryChangeHistoryEntry>(reader.GetString(12), PayloadSerializerOptions)!;
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

    // Additive, transactional and idempotent migration of installations created before raw events
    // were retained. Never invent the original sizes of an already merged legacy update.
    private static async Task MigrateLibraryEventsAsync(SqliteConnection connection, CancellationToken token)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        async Task Execute(string sql)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(token);
        }
        await Execute("CREATE TABLE IF NOT EXISTS history_schema_migrations (version INTEGER PRIMARY KEY)");
        await using (SqliteCommand check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT 1 FROM history_schema_migrations WHERE version = 1";
            if (await check.ExecuteScalarAsync(token) is not null) return;
        }
        await Execute("ALTER TABLE library_history ADD COLUMN metadata TEXT");
        await Execute("CREATE TABLE library_history_events (kind TEXT NOT NULL, instance_id TEXT NOT NULL, source_key TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY (kind, instance_id, source_key))");
        List<(long Id, LibraryChangeHistoryEntry Entry)> legacy = [];
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT source_key, kind, instance_id, instance_name, occurred_at, media_key, change_type, entity_title, series_title, file_path, size_bytes, delta_size_bytes, metadata, id FROM library_history";
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) legacy.Add((reader.GetInt64(13), ReadEntry(reader)));
        }
        foreach (var (_, entry) in legacy)
        {
            entry.IsLegacy = true;
            entry.NeedsHistoryBackfill = true;
            if (entry.ChangeType == LibraryChangeType.Deleted && entry.DeltaSizeBytes < 0)
                entry.SizeBytes = -entry.DeltaSizeBytes;
            if (entry.ChangeType == LibraryChangeType.Updated && entry.DeltaSizeBytes == 0)
                entry.IsSizeDeltaKnown = false;
        }
        List<LibraryHistoryArchiveRepair.Snapshot> archive = [];
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT kind, instance_id, instance_name, synced_at, payload FROM snapshot_history ORDER BY id";
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                archive.Add(new(ParseKind(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
                    DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture), reader.GetString(4)));
        }
        List<LibraryChangeHistoryEntry> recovered = LibraryHistoryArchiveRepair.Repair(archive, legacy.Select(e => e.Entry).ToList(), PayloadSerializerOptions);
        foreach (var (id, entry) in legacy)
        {
            string json = JsonSerializer.Serialize(entry, PayloadSerializerOptions);
            await using SqliteCommand write = connection.CreateCommand();
            write.Transaction = transaction;
            write.CommandText = "INSERT OR IGNORE INTO library_history_events VALUES ($kind, $instance, $key, $payload); UPDATE library_history SET metadata = $payload, size_bytes = $size, delta_size_bytes = $delta WHERE id = $id";
            write.Parameters.AddWithValue("$kind", KindToText(entry.Kind));
            write.Parameters.AddWithValue("$instance", entry.InstanceId);
            write.Parameters.AddWithValue("$key", entry.SourceKey);
            write.Parameters.AddWithValue("$payload", json);
            write.Parameters.AddWithValue("$size", (object?)entry.SizeBytes ?? DBNull.Value);
            write.Parameters.AddWithValue("$id", id);
            write.Parameters.AddWithValue("$delta", entry.DeltaSizeBytes);
            await write.ExecuteNonQueryAsync(token);
        }
        foreach (LibraryChangeHistoryEntry entry in recovered)
        {
            entry.NeedsHistoryBackfill = true;
            string json = JsonSerializer.Serialize(entry, PayloadSerializerOptions);
            await using SqliteCommand write = connection.CreateCommand();
            write.Transaction = transaction;
            write.CommandText = """
                INSERT OR IGNORE INTO library_history_events VALUES ($kind, $instance, $key, $payload);
                INSERT INTO library_history (source_key, kind, instance_id, instance_name, occurred_at,
                    media_key, change_type, entity_title, series_title, file_path, size_bytes, delta_size_bytes, metadata)
                VALUES ($key, $kind, $instance, $name, $at, $media, 'Deleted', $title, $series, $path, $size, $delta, $payload)
                """;
            write.Parameters.AddWithValue("$kind", KindToText(entry.Kind));
            write.Parameters.AddWithValue("$instance", entry.InstanceId);
            write.Parameters.AddWithValue("$key", entry.SourceKey);
            write.Parameters.AddWithValue("$payload", json);
            write.Parameters.AddWithValue("$name", entry.InstanceName);
            write.Parameters.AddWithValue("$at", entry.OccurredAt.ToString("O", CultureInfo.InvariantCulture));
            write.Parameters.AddWithValue("$media", entry.MediaKey);
            write.Parameters.AddWithValue("$title", entry.EntityTitle);
            write.Parameters.AddWithValue("$series", (object?)entry.SeriesTitle ?? DBNull.Value);
            write.Parameters.AddWithValue("$path", (object?)entry.FilePath ?? DBNull.Value);
            write.Parameters.AddWithValue("$size", (object?)entry.SizeBytes ?? DBNull.Value);
            write.Parameters.AddWithValue("$delta", entry.DeltaSizeBytes);
            await write.ExecuteNonQueryAsync(token);
        }
        await Execute("INSERT INTO history_schema_migrations VALUES (1)");
        await transaction.CommitAsync(token);
    }

    private static async Task BackfillMissingDeletionHistoryAsync(SqliteConnection connection, CancellationToken token)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await using (SqliteCommand check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT 1 FROM history_schema_migrations WHERE version = 2";
            if (await check.ExecuteScalarAsync(token) is not null) return;
        }
        List<LibraryChangeHistoryEntry> events = [];
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT payload FROM library_history_events";
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                events.Add(JsonSerializer.Deserialize<LibraryChangeHistoryEntry>(reader.GetString(0), PayloadSerializerOptions)!);
        }
        List<LibraryHistoryArchiveRepair.Snapshot> archive = [];
        await using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT kind, instance_id, instance_name, synced_at, payload FROM snapshot_history
                UNION ALL
                SELECT kind, instance_id, '', synced_at, payload FROM instance_snapshots
                """;
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                archive.Add(new(ParseKind(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
                    DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture), reader.GetString(4)));
        }
        archive = archive.GroupBy(snapshot => (snapshot.Kind, snapshot.InstanceId, snapshot.At))
            .Select(group => group.First()).ToList();
        events = LibraryHistoryReconciler.MergeEvents(events, LibraryHistoryArchiveRepair.Repair(archive, events, PayloadSerializerOptions));
        foreach (var instance in archive.GroupBy(snapshot => (snapshot.Kind, snapshot.InstanceId)))
        {
            var latest = instance.Where(item => item.Kind != ArrApplicationKind.Sonarr
                || JsonSerializer.Deserialize<SonarrInstanceSnapshot>(item.Payload, PayloadSerializerOptions)!.SeriesSyncTimes.Count == 0)
                .MaxBy(snapshot => snapshot.At);
            if (latest is null) continue;
            long size;
            string name;
            if (latest.Kind == ArrApplicationKind.Sonarr)
            {
                var snapshot = JsonSerializer.Deserialize<SonarrInstanceSnapshot>(latest.Payload, PayloadSerializerOptions)!;
                size = snapshot.Series.SelectMany(series => series.Seasons).SelectMany(season => season.Episodes)
                    .Where(episode => episode.HasFile).Sum(episode => episode.File.Size ?? 0);
                name = snapshot.InstanceName;
            }
            else
            {
                var snapshot = JsonSerializer.Deserialize<RadarrInstanceSnapshot>(latest.Payload, PayloadSerializerOptions)!;
                size = snapshot.Movies.Where(movie => movie.HasFile).Sum(movie => movie.File.Size ?? 0);
                name = snapshot.InstanceName;
            }
            events = LibraryInventoryHistoryBuilder.Observe(events, latest.Kind, latest.InstanceId, name, latest.At, size);
        }
        foreach (ArrApplicationKind kind in new[] { ArrApplicationKind.Sonarr, ArrApplicationKind.Radarr })
        {
            var raw = events.Where(entry => entry.Kind == kind).ToList();
            await WriteLibraryHistoryAsync(connection, transaction, kind, LibraryHistoryReconciler.Project(raw), raw, token);
        }
        await using (SqliteCommand mark = connection.CreateCommand())
        {
            mark.Transaction = transaction;
            mark.CommandText = "INSERT INTO history_schema_migrations VALUES (2)";
            await mark.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
    }

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

                await MigrateLibraryEventsAsync(connection, cancellationToken);
                await BackfillMissingDeletionHistoryAsync(connection, cancellationToken);
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
