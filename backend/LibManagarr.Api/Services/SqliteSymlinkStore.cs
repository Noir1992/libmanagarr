using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services.Backup;

namespace LibManagarr.Api.Services;

/// <summary>
/// SQLite backing for the symlink register, its suggestions, its declines and the probe results. Rows
/// live in the same database as the library history, so every write joins the backup writer
/// coordination through <see cref="BackupWriteGate"/> and an export never captures a half written row.
/// </summary>
public sealed class SqliteSymlinkStore(
    StoragePaths paths, TimeProvider clock, BackupOperationCoordinator writes, ILogger<SqliteSymlinkStore> logger) : ISymlinkStore
{
    private const string LinkColumns = """
        id,revision,radarr_instance_id,radarr_movie_id,radarr_file_id,sonarr_instance_id,sonarr_series_id,
        sonarr_episode_id,season_number,episode_number,storage_area_id,link_path,link_text,source_path,
        operation_status,setup_step,suggestion_id,sonarr_file_id,sonarr_scan_command_id,monitoring_original,
        monitoring_disabled,auto_follow_up,desired_filename,applied_filename,naming_revision,source_version,
        update_revision,expected_metadata,confirmed_metadata,created_at,updated_at,last_checked_at,
        last_successful_check_at,findings,sonarr_rename_command_id,pending_update,naming_move
        """;

    /// <summary>
    /// The five pair columns stay together because <see cref="ReadSuggestion"/> hands them to
    /// <see cref="ReadPair"/> as one run; a column wedged into the middle would be read as an episode id.
    /// </summary>
    private const string SuggestionColumns = """
        id,revision,matcher_version,status,radarr_instance_id,radarr_movie_id,sonarr_instance_id,
        sonarr_series_id,sonarr_episode_id,radarr_file_id,season_number,episode_number,reasons,
        alternatives,detail,config_revision,link_id,created_at,updated_at,decided_at
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim initializationGate = new(1, 1);
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
            await new SymlinkMigration(clock, logger).ApplyAsync(connection, cancellationToken);
            initialized = true;
        }
        finally { initializationGate.Release(); }
    }

    public async Task<SymlinkCapabilityCheck?> GetCapabilityCheckAsync(string storageAreaId, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT storage_area_id,status,detail,checked_at,config_revision,process_id FROM symlink_capability_checks WHERE storage_area_id=$id";
        command.Parameters.AddWithValue("$id", storageAreaId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCapability(reader) : null;
    }

    public async Task<IReadOnlyList<SymlinkCapabilityCheck>> GetCapabilityChecksAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT storage_area_id,status,detail,checked_at,config_revision,process_id FROM symlink_capability_checks ORDER BY storage_area_id";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<SymlinkCapabilityCheck> result = [];
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadCapability(reader));
        return result;
    }

    public async Task SaveCapabilityCheckAsync(SymlinkCapabilityCheck check, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);
        await InitializeAsync(cancellationToken);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenAsync(cancellationToken);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO symlink_capability_checks (storage_area_id,status,detail,checked_at,config_revision,process_id)
                VALUES ($id,$status,$detail,$at,$revision,$process)
                ON CONFLICT(storage_area_id) DO UPDATE SET status=$status,detail=$detail,checked_at=$at,config_revision=$revision,process_id=$process
                """;
            command.Parameters.AddWithValue("$id", check.StorageAreaId);
            command.Parameters.AddWithValue("$status", (int)check.Status);
            command.Parameters.AddWithValue("$detail", (object?)check.Detail ?? DBNull.Value);
            // The column is not null by design: a row without a moment it was observed would let an
            // unknown result look as recent as a fresh one.
            command.Parameters.AddWithValue("$at", SqliteScheduledTaskStore.Timestamp(check.CheckedAt ?? clock.GetUtcNow()));
            command.Parameters.AddWithValue("$revision", check.ConfigRevision);
            command.Parameters.AddWithValue("$process", (object?)check.ProcessId ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<SymlinkLink>> GetLinksAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {LinkColumns} FROM symlink_links ORDER BY created_at,id";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<SymlinkLink> result = [];
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadLink(reader));
        return result;
    }

    public async Task<SymlinkLink?> GetLinkAsync(string id, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {LinkColumns} FROM symlink_links WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadLink(reader) : null;
    }

    public async Task InsertLinkAsync(SymlinkLink link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        await InitializeAsync(cancellationToken);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenAsync(cancellationToken);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO symlink_links ({LinkColumns}) VALUES ({InsertValues(LinkColumns)})";
            AddLinkParameters(command, link);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task<bool> UpdateLinkAsync(SymlinkLink link, long expectedRevision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        await InitializeAsync(cancellationToken);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenAsync(cancellationToken);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"""
                UPDATE symlink_links SET
                    radarr_instance_id=$radarr_instance_id,radarr_movie_id=$radarr_movie_id,radarr_file_id=$radarr_file_id,
                    sonarr_instance_id=$sonarr_instance_id,sonarr_series_id=$sonarr_series_id,sonarr_episode_id=$sonarr_episode_id,
                    season_number=$season_number,episode_number=$episode_number,storage_area_id=$storage_area_id,
                    link_path=$link_path,link_text=$link_text,source_path=$source_path,operation_status=$operation_status,
                    setup_step=$setup_step,suggestion_id=$suggestion_id,sonarr_file_id=$sonarr_file_id,
                    sonarr_scan_command_id=$sonarr_scan_command_id,monitoring_original=$monitoring_original,
                    monitoring_disabled=$monitoring_disabled,auto_follow_up=$auto_follow_up,desired_filename=$desired_filename,
                    applied_filename=$applied_filename,naming_revision=$naming_revision,source_version=$source_version,
                    update_revision=$update_revision,expected_metadata=$expected_metadata,confirmed_metadata=$confirmed_metadata,
                    created_at=$created_at,updated_at=$updated_at,last_checked_at=$last_checked_at,
                    last_successful_check_at=$last_successful_check_at,findings=$findings,
                    sonarr_rename_command_id=$sonarr_rename_command_id,pending_update=$pending_update,naming_move=$naming_move,revision=revision+1
                WHERE id=$id AND revision=$expected
                """;
            AddLinkParameters(command, link);
            command.Parameters.AddWithValue("$expected", expectedRevision);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task<bool> DeleteLinkAsync(string id, long expectedRevision, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenAsync(cancellationToken);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM symlink_links WHERE id=$id AND revision=$expected";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$expected", expectedRevision);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<SymlinkSuggestion>> GetSuggestionsAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {SuggestionColumns} FROM symlink_suggestions ORDER BY created_at,id";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<SymlinkSuggestion> result = [];
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadSuggestion(reader));
        return result;
    }

    public async Task<SymlinkSuggestion?> GetSuggestionAsync(string id, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {SuggestionColumns} FROM symlink_suggestions WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSuggestion(reader) : null;
    }

    public async Task UpsertSuggestionAsync(SymlinkSuggestion suggestion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        await InitializeAsync(cancellationToken);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenAsync(cancellationToken);
            // The pair stays one row while a decision is still open, so a later sync updates the candidate
            // data instead of adding a second thing to decide about. CreatedAt keeps the first sighting.
            using (SqliteCommand existing = connection.CreateCommand())
            {
                existing.CommandText = """
                    SELECT id FROM symlink_suggestions
                    WHERE radarr_instance_id=$radarr_instance_id AND radarr_movie_id=$radarr_movie_id
                      AND sonarr_instance_id=$sonarr_instance_id AND sonarr_series_id=$sonarr_series_id
                      AND sonarr_episode_id=$sonarr_episode_id AND status IN ($pending,$creating)
                    """;
                AddPair(existing, suggestion.Pair);
                existing.Parameters.AddWithValue("$pending", (int)SymlinkSuggestionStatus.Pending);
                existing.Parameters.AddWithValue("$creating", (int)SymlinkSuggestionStatus.Creating);
                object? found = await existing.ExecuteScalarAsync(cancellationToken);
                if (found is string existingId)
                {
                    suggestion.Id = existingId;
                    using SqliteCommand update = connection.CreateCommand();
                    update.CommandText = $"""
                        UPDATE symlink_suggestions SET
                            matcher_version=$matcher_version,status=$status,radarr_file_id=$radarr_file_id,
                            season_number=$season_number,episode_number=$episode_number,reasons=$reasons,
                            alternatives=$alternatives,detail=$detail,config_revision=$config_revision,
                            link_id=$link_id,updated_at=$updated_at,decided_at=$decided_at,revision=revision+1
                        WHERE id=$id
                        """;
                    AddSuggestionParameters(update, suggestion);
                    await update.ExecuteNonQueryAsync(cancellationToken);
                    return;
                }
            }

            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = $"INSERT INTO symlink_suggestions ({SuggestionColumns}) VALUES ({InsertValues(SuggestionColumns)})";
            AddSuggestionParameters(insert, suggestion);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task<bool> UpdateSuggestionAsync(SymlinkSuggestion suggestion, long expectedRevision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        await InitializeAsync(cancellationToken);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenAsync(cancellationToken);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"""
                UPDATE symlink_suggestions SET
                    matcher_version=$matcher_version,status=$status,radarr_instance_id=$radarr_instance_id,
                    radarr_movie_id=$radarr_movie_id,radarr_file_id=$radarr_file_id,sonarr_instance_id=$sonarr_instance_id,
                    sonarr_series_id=$sonarr_series_id,sonarr_episode_id=$sonarr_episode_id,season_number=$season_number,
                    episode_number=$episode_number,reasons=$reasons,alternatives=$alternatives,detail=$detail,
                    config_revision=$config_revision,link_id=$link_id,created_at=$created_at,updated_at=$updated_at,
                    decided_at=$decided_at,revision=revision+1
                WHERE id=$id AND revision=$expected
                """;
            AddSuggestionParameters(command, suggestion);
            command.Parameters.AddWithValue("$expected", expectedRevision);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task<SymlinkSuggestion?> FindActiveSuggestionAsync(SpecialMoviePair pair, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SuggestionColumns} FROM symlink_suggestions
            WHERE radarr_instance_id=$radarr_instance_id AND radarr_movie_id=$radarr_movie_id
              AND sonarr_instance_id=$sonarr_instance_id AND sonarr_series_id=$sonarr_series_id
              AND sonarr_episode_id=$sonarr_episode_id AND status IN ($pending,$creating)
            LIMIT 1
            """;
        AddPair(command, pair);
        command.Parameters.AddWithValue("$pending", (int)SymlinkSuggestionStatus.Pending);
        command.Parameters.AddWithValue("$creating", (int)SymlinkSuggestionStatus.Creating);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSuggestion(reader) : null;
    }

    public async Task<IReadOnlyList<SymlinkRejection>> GetRejectionsAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT radarr_instance_id,radarr_movie_id,sonarr_instance_id,sonarr_series_id,sonarr_episode_id,detail,created_at
            FROM symlink_rejections ORDER BY created_at,radarr_instance_id,radarr_movie_id
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<SymlinkRejection> result = [];
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadRejection(reader));
        return result;
    }

    public async Task<bool> IsRejectedAsync(SpecialMoviePair pair, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM symlink_rejections WHERE " + PairPredicate;
        AddPair(command, pair);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    public async Task RejectAsync(SpecialMoviePair pair, string? detail, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenAsync(cancellationToken);
            using SqliteCommand command = connection.CreateCommand();
            // Ignored on conflict on purpose: the first decline owns the date, and a repeated decline of
            // the same pair must not look like a newer decision.
            command.CommandText = $"INSERT OR IGNORE INTO symlink_rejections ({PairColumns},detail,created_at) VALUES ({InsertValues(PairColumns)},$detail,$at)";
            AddPair(command, pair);
            command.Parameters.AddWithValue("$detail", (object?)detail ?? DBNull.Value);
            command.Parameters.AddWithValue("$at", SqliteScheduledTaskStore.Timestamp(at));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task<bool> RemoveRejectionAsync(SpecialMoviePair pair, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenAsync(cancellationToken);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM symlink_rejections WHERE {PairPredicate}";
            AddPair(command, pair);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task<SymlinkMatchingState?> GetMatchingStateAsync(string instanceId, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {MatchingStateColumns} FROM symlink_matching_state WHERE instance_id=$id";
        command.Parameters.AddWithValue("$id", instanceId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadMatchingState(reader) : null;
    }

    public async Task<IReadOnlyList<SymlinkMatchingState>> GetMatchingStatesAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {MatchingStateColumns} FROM symlink_matching_state ORDER BY instance_id";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<SymlinkMatchingState> result = [];
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadMatchingState(reader));
        return result;
    }

    public async Task SaveMatchingStateAsync(SymlinkMatchingState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await InitializeAsync(cancellationToken);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenAsync(cancellationToken);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO symlink_matching_state (instance_id,kind,signature,matcher_version,config_revision,checked_at)
                VALUES ($id,$kind,$signature,$matcher_version,$config_revision,$checked_at)
                ON CONFLICT(instance_id) DO UPDATE SET
                    kind=$kind,signature=$signature,matcher_version=$matcher_version,
                    config_revision=$config_revision,checked_at=$checked_at
                """;
            command.Parameters.AddWithValue("$id", state.InstanceId);
            command.Parameters.AddWithValue("$kind", (int)state.Kind);
            command.Parameters.AddWithValue("$signature", state.Signature);
            command.Parameters.AddWithValue("$matcher_version", state.MatcherVersion);
            command.Parameters.AddWithValue("$config_revision", state.ConfigRevision);
            command.Parameters.AddWithValue("$checked_at", SqliteScheduledTaskStore.Timestamp(state.CheckedAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private const string MatchingStateColumns = "instance_id,kind,signature,matcher_version,config_revision,checked_at";

    private static SymlinkMatchingState ReadMatchingState(SqliteDataReader reader) => new()
    {
        InstanceId = reader.GetString(0),
        Kind = (ArrApplicationKind)reader.GetInt32(1),
        Signature = reader.GetString(2),
        MatcherVersion = reader.GetInt32(3),
        ConfigRevision = reader.GetInt32(4),
        CheckedAt = SqliteScheduledTaskStore.ReadTimestamp(reader, 5)
    };

    private const string PairColumns = "radarr_instance_id,radarr_movie_id,sonarr_instance_id,sonarr_series_id,sonarr_episode_id";
    private const string PairPredicate = """
        radarr_instance_id=$radarr_instance_id AND radarr_movie_id=$radarr_movie_id
        AND sonarr_instance_id=$sonarr_instance_id AND sonarr_series_id=$sonarr_series_id
        AND sonarr_episode_id=$sonarr_episode_id
        """;

    private static string InsertValues(string columns) =>
        string.Join(',', columns.Split(',').Select(name => "$" + name.Trim()));

    private static void AddPair(SqliteCommand command, SpecialMoviePair pair)
    {
        command.Parameters.AddWithValue("$radarr_instance_id", pair.RadarrInstanceId);
        command.Parameters.AddWithValue("$radarr_movie_id", pair.RadarrMovieId);
        command.Parameters.AddWithValue("$sonarr_instance_id", pair.SonarrInstanceId);
        command.Parameters.AddWithValue("$sonarr_series_id", pair.SonarrSeriesId);
        command.Parameters.AddWithValue("$sonarr_episode_id", pair.SonarrEpisodeId);
    }

    private static void AddLinkParameters(SqliteCommand command, SymlinkLink link)
    {
        command.Parameters.AddWithValue("$id", link.Id);
        command.Parameters.AddWithValue("$revision", link.Revision);
        command.Parameters.AddWithValue("$radarr_instance_id", link.RadarrInstanceId);
        command.Parameters.AddWithValue("$radarr_movie_id", link.RadarrMovieId);
        command.Parameters.AddWithValue("$radarr_file_id", link.RadarrFileId);
        command.Parameters.AddWithValue("$sonarr_instance_id", link.SonarrInstanceId);
        command.Parameters.AddWithValue("$sonarr_series_id", link.SonarrSeriesId);
        command.Parameters.AddWithValue("$sonarr_episode_id", link.SonarrEpisodeId);
        command.Parameters.AddWithValue("$season_number", link.SeasonNumber);
        command.Parameters.AddWithValue("$episode_number", link.EpisodeNumber);
        command.Parameters.AddWithValue("$storage_area_id", link.StorageAreaId);
        command.Parameters.AddWithValue("$link_path", link.LinkPath);
        command.Parameters.AddWithValue("$link_text", link.LinkText);
        command.Parameters.AddWithValue("$source_path", link.SourcePath);
        command.Parameters.AddWithValue("$operation_status", (int)link.OperationStatus);
        command.Parameters.AddWithValue("$setup_step", (object?)link.SetupStep ?? DBNull.Value);
        command.Parameters.AddWithValue("$suggestion_id", (object?)link.SuggestionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$sonarr_file_id", (object?)link.SonarrFileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$sonarr_scan_command_id", (object?)link.SonarrScanCommandId ?? DBNull.Value);
        command.Parameters.AddWithValue("$monitoring_original", (object?)link.MonitoringOriginal ?? DBNull.Value);
        command.Parameters.AddWithValue("$monitoring_disabled", link.MonitoringDisabled ? 1 : 0);
        command.Parameters.AddWithValue("$auto_follow_up", link.AutoFollowUp ? 1 : 0);
        command.Parameters.AddWithValue("$desired_filename", (object?)link.DesiredFilename ?? DBNull.Value);
        command.Parameters.AddWithValue("$applied_filename", (object?)link.AppliedFilename ?? DBNull.Value);
        command.Parameters.AddWithValue("$naming_revision", (object?)link.NamingRevision ?? DBNull.Value);
        command.Parameters.AddWithValue("$source_version", (object?)link.SourceVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$update_revision", link.UpdateRevision);
        command.Parameters.AddWithValue("$expected_metadata", WriteJson(link.ExpectedMetadata));
        command.Parameters.AddWithValue("$confirmed_metadata", WriteJson(link.ConfirmedMetadata));
        command.Parameters.AddWithValue("$created_at", SqliteScheduledTaskStore.Timestamp(link.CreatedAt));
        command.Parameters.AddWithValue("$updated_at", SqliteScheduledTaskStore.Timestamp(link.UpdatedAt));
        command.Parameters.AddWithValue("$last_checked_at", SqliteScheduledTaskStore.Timestamp(link.LastCheckedAt));
        command.Parameters.AddWithValue("$last_successful_check_at", SqliteScheduledTaskStore.Timestamp(link.LastSuccessfulCheckAt));
        command.Parameters.AddWithValue("$findings", link.Findings.Count == 0 ? DBNull.Value : JsonSerializer.Serialize(link.Findings, Json));
        command.Parameters.AddWithValue("$sonarr_rename_command_id", (object?)link.SonarrRenameCommandId ?? DBNull.Value);
        command.Parameters.AddWithValue("$pending_update", link.PendingUpdate is null
            ? DBNull.Value : JsonSerializer.Serialize(link.PendingUpdate, Json));
        command.Parameters.AddWithValue("$naming_move", WriteJson(link.NamingMove));
    }

    private static void AddSuggestionParameters(SqliteCommand command, SymlinkSuggestion suggestion)
    {
        command.Parameters.AddWithValue("$id", suggestion.Id);
        command.Parameters.AddWithValue("$revision", suggestion.Revision);
        command.Parameters.AddWithValue("$matcher_version", suggestion.MatcherVersion);
        command.Parameters.AddWithValue("$status", (int)suggestion.Status);
        AddPair(command, suggestion.Pair);
        command.Parameters.AddWithValue("$radarr_file_id", (object?)suggestion.RadarrFileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$season_number", suggestion.SeasonNumber);
        command.Parameters.AddWithValue("$episode_number", suggestion.EpisodeNumber);
        command.Parameters.AddWithValue("$reasons", JsonSerializer.Serialize(suggestion.Reasons ?? [], Json));
        command.Parameters.AddWithValue("$alternatives", suggestion.Alternatives.Count == 0
            ? DBNull.Value
            : JsonSerializer.Serialize(suggestion.Alternatives, Json));
        command.Parameters.AddWithValue("$detail", (object?)suggestion.Detail ?? DBNull.Value);
        command.Parameters.AddWithValue("$config_revision", suggestion.ConfigRevision);
        command.Parameters.AddWithValue("$link_id", (object?)suggestion.LinkId ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", SqliteScheduledTaskStore.Timestamp(suggestion.CreatedAt));
        command.Parameters.AddWithValue("$updated_at", SqliteScheduledTaskStore.Timestamp(suggestion.UpdatedAt));
        command.Parameters.AddWithValue("$decided_at", SqliteScheduledTaskStore.Timestamp(suggestion.DecidedAt));
    }

    private static SymlinkCapabilityCheck ReadCapability(SqliteDataReader reader) => new()
    {
        StorageAreaId = reader.GetString(0),
        Status = (SymlinkCapabilityStatus)reader.GetInt32(1),
        Detail = reader.IsDBNull(2) ? null : reader.GetString(2),
        CheckedAt = SqliteScheduledTaskStore.ReadTimestamp(reader, 3),
        ConfigRevision = reader.GetInt32(4),
        ProcessId = reader.IsDBNull(5) ? null : reader.GetString(5)
    };

    /// <summary>
    /// Rows are read positionally, so every helper below advances the cursor itself. A reader that
    /// forgets to move would otherwise shift the rest of the row by one column and store a path in the
    /// field of a timestamp.
    /// </summary>
    private SymlinkLink ReadLink(SqliteDataReader reader)
    {
        int at = 0;
        return new SymlinkLink
        {
            Id = ReadString(reader, ref at),
            Revision = ReadLong(reader, ref at),
            RadarrInstanceId = ReadString(reader, ref at),
            RadarrMovieId = ReadInt(reader, ref at),
            RadarrFileId = ReadInt(reader, ref at),
            SonarrInstanceId = ReadString(reader, ref at),
            SonarrSeriesId = ReadInt(reader, ref at),
            SonarrEpisodeId = ReadInt(reader, ref at),
            SeasonNumber = ReadInt(reader, ref at),
            EpisodeNumber = ReadInt(reader, ref at),
            StorageAreaId = ReadString(reader, ref at),
            LinkPath = ReadString(reader, ref at),
            LinkText = ReadString(reader, ref at),
            SourcePath = ReadString(reader, ref at),
            OperationStatus = (SymlinkOperationStatus)ReadInt(reader, ref at),
            SetupStep = ReadNullableSetupStep(reader, ref at),
            SuggestionId = ReadNullableString(reader, ref at),
            SonarrFileId = ReadNullableInt(reader, ref at),
            SonarrScanCommandId = ReadNullableString(reader, ref at),
            MonitoringOriginal = ReadNullableBoolean(reader, ref at),
            MonitoringDisabled = ReadBoolean(reader, ref at),
            AutoFollowUp = ReadBoolean(reader, ref at),
            DesiredFilename = ReadNullableString(reader, ref at),
            AppliedFilename = ReadNullableString(reader, ref at),
            NamingRevision = ReadNullableString(reader, ref at),
            SourceVersion = ReadNullableString(reader, ref at),
            UpdateRevision = ReadLong(reader, ref at),
            ExpectedMetadata = ReadJson<SymlinkFileMetadata>(reader, ref at),
            ConfirmedMetadata = ReadJson<SymlinkFileMetadata>(reader, ref at),
            CreatedAt = ReadDate(reader, ref at),
            UpdatedAt = ReadDate(reader, ref at),
            LastCheckedAt = ReadNullableDate(reader, ref at),
            LastSuccessfulCheckAt = ReadNullableDate(reader, ref at),
            Findings = ReadJson<List<SymlinkFinding>>(reader, ref at) ?? [],
            SonarrRenameCommandId = ReadNullableString(reader, ref at),
            PendingUpdate = ReadJson<SymlinkSourceUpdate>(reader, ref at),
            NamingMove = ReadJson<SymlinkNameMove>(reader, ref at)
        };
    }

    private SymlinkSuggestion ReadSuggestion(SqliteDataReader reader)
    {
        int at = 0;
        return new SymlinkSuggestion
        {
            Id = ReadString(reader, ref at),
            Revision = ReadLong(reader, ref at),
            MatcherVersion = ReadString(reader, ref at),
            Status = (SymlinkSuggestionStatus)ReadInt(reader, ref at),
            Pair = ReadPair(reader, ref at),
            RadarrFileId = ReadNullableInt(reader, ref at),
            SeasonNumber = ReadInt(reader, ref at),
            EpisodeNumber = ReadInt(reader, ref at),
            Reasons = ReadJson<List<SymlinkSuggestionReason>>(reader, ref at) ?? [],
            Alternatives = ReadJson<List<SymlinkSuggestionAlternative>>(reader, ref at) ?? [],
            Detail = ReadNullableString(reader, ref at),
            ConfigRevision = ReadInt(reader, ref at),
            LinkId = ReadNullableString(reader, ref at),
            CreatedAt = ReadDate(reader, ref at),
            UpdatedAt = ReadDate(reader, ref at),
            DecidedAt = ReadNullableDate(reader, ref at)
        };
    }

    private static SymlinkRejection ReadRejection(SqliteDataReader reader)
    {
        int at = 0;
        return new SymlinkRejection
        {
            Pair = ReadPair(reader, ref at),
            Detail = ReadNullableString(reader, ref at),
            CreatedAt = ReadDate(reader, ref at)
        };
    }

    private static SpecialMoviePair ReadPair(SqliteDataReader reader, ref int at) => new(
        ReadString(reader, ref at), ReadInt(reader, ref at),
        ReadString(reader, ref at), ReadInt(reader, ref at), ReadInt(reader, ref at));

    private static string ReadString(SqliteDataReader reader, ref int at) => reader.GetString(at++);

    private static int ReadInt(SqliteDataReader reader, ref int at) => reader.GetInt32(at++);

    private static long ReadLong(SqliteDataReader reader, ref int at) => reader.GetInt64(at++);

    private static bool ReadBoolean(SqliteDataReader reader, ref int at) => reader.GetBoolean(at++);

    private static string? ReadNullableString(SqliteDataReader reader, ref int at)
    {
        int column = at++;
        return reader.IsDBNull(column) ? null : reader.GetString(column);
    }

    private static int? ReadNullableInt(SqliteDataReader reader, ref int at)
    {
        int column = at++;
        return reader.IsDBNull(column) ? null : reader.GetInt32(column);
    }

    private static bool? ReadNullableBoolean(SqliteDataReader reader, ref int at)
    {
        int column = at++;
        return reader.IsDBNull(column) ? null : reader.GetBoolean(column);
    }

    private static SymlinkSetupStep? ReadNullableSetupStep(SqliteDataReader reader, ref int at)
    {
        int column = at++;
        return reader.IsDBNull(column) ? null : (SymlinkSetupStep?)reader.GetInt32(column);
    }

    private static DateTimeOffset ReadDate(SqliteDataReader reader, ref int at) =>
        SqliteScheduledTaskStore.ReadTimestamp(reader, at++)!.Value;

    private static DateTimeOffset? ReadNullableDate(SqliteDataReader reader, ref int at) =>
        SqliteScheduledTaskStore.ReadTimestamp(reader, at++);

    private static T? ReadJson<T>(SqliteDataReader reader, ref int at)
    {
        int column = at++;
        return reader.IsDBNull(column) ? default : JsonSerializer.Deserialize<T>(reader.GetString(column), Json);
    }

    private static object WriteJson(object? value) =>
        value is null ? DBNull.Value : JsonSerializer.Serialize(value, Json);

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
