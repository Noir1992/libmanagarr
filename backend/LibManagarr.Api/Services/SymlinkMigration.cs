using Microsoft.Data.Sqlite;

namespace LibManagarr.Api.Services;

/// <summary>
/// Creates the tables of the symlink feature. Purely additive: no existing row is read or rewritten,
/// so the pre-migration copy that the task migration takes for its restructuring is not needed here.
/// </summary>
internal sealed class SymlinkMigration(TimeProvider clock, ILogger logger)
{
    /// <summary>
    /// Each step is marked on its own, so a table added later does not depend on the ones before it
    /// having been created by the same release.
    /// </summary>
    private static readonly (string Id, string Schema)[] Steps =
    [
        (
            "symlinks-v1",
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (migration_id TEXT PRIMARY KEY, applied_at TEXT NOT NULL);

            CREATE TABLE IF NOT EXISTS symlink_capability_checks (
                storage_area_id TEXT NOT NULL PRIMARY KEY,
                status INTEGER NOT NULL CHECK(status BETWEEN 0 AND 5),
                detail TEXT,
                checked_at TEXT NOT NULL,
                config_revision INTEGER NOT NULL CHECK(config_revision >= 0)
            );

            CREATE TABLE IF NOT EXISTS symlink_links (
                id TEXT NOT NULL PRIMARY KEY,
                revision INTEGER NOT NULL DEFAULT 0 CHECK(revision >= 0),
                radarr_instance_id TEXT NOT NULL,
                radarr_movie_id INTEGER NOT NULL,
                radarr_file_id INTEGER NOT NULL,
                sonarr_instance_id TEXT NOT NULL,
                sonarr_series_id INTEGER NOT NULL,
                sonarr_episode_id INTEGER NOT NULL,
                season_number INTEGER NOT NULL CHECK(season_number >= 0),
                episode_number INTEGER NOT NULL CHECK(episode_number >= 0),
                storage_area_id TEXT NOT NULL,
                link_path TEXT NOT NULL,
                link_text TEXT NOT NULL,
                source_path TEXT NOT NULL,
                operation_status INTEGER NOT NULL CHECK(operation_status BETWEEN 0 AND 3),
                setup_step INTEGER CHECK(setup_step BETWEEN 0 AND 6),
                suggestion_id TEXT,
                sonarr_file_id INTEGER,
                sonarr_scan_command_id TEXT,
                monitoring_original INTEGER CHECK(monitoring_original IN (0,1)),
                monitoring_disabled INTEGER NOT NULL DEFAULT 0 CHECK(monitoring_disabled IN (0,1)),
                auto_follow_up INTEGER NOT NULL DEFAULT 0 CHECK(auto_follow_up IN (0,1)),
                desired_filename TEXT,
                applied_filename TEXT,
                naming_revision TEXT,
                source_version TEXT,
                update_revision INTEGER NOT NULL DEFAULT 0 CHECK(update_revision >= 0),
                expected_metadata TEXT,
                confirmed_metadata TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                last_checked_at TEXT,
                last_successful_check_at TEXT,
                findings TEXT
            );

            -- One managed link per special, unless the row only records a deletion that is still owed.
            CREATE UNIQUE INDEX IF NOT EXISTS symlink_links_one_per_episode
                ON symlink_links (sonarr_instance_id, sonarr_series_id, sonarr_episode_id)
                WHERE operation_status <> 3;
            CREATE INDEX IF NOT EXISTS symlink_links_radarr_source
                ON symlink_links (radarr_instance_id, radarr_movie_id);

            CREATE TABLE IF NOT EXISTS symlink_suggestions (
                id TEXT NOT NULL PRIMARY KEY,
                revision INTEGER NOT NULL DEFAULT 0 CHECK(revision >= 0),
                matcher_version TEXT NOT NULL,
                status INTEGER NOT NULL CHECK(status BETWEEN 0 AND 7),
                radarr_instance_id TEXT NOT NULL,
                radarr_movie_id INTEGER NOT NULL,
                radarr_file_id INTEGER,
                sonarr_instance_id TEXT NOT NULL,
                sonarr_series_id INTEGER NOT NULL,
                sonarr_episode_id INTEGER NOT NULL,
                season_number INTEGER NOT NULL CHECK(season_number >= 0),
                episode_number INTEGER NOT NULL CHECK(episode_number >= 0),
                reasons TEXT NOT NULL,
                alternatives TEXT,
                detail TEXT,
                config_revision INTEGER NOT NULL CHECK(config_revision >= 0),
                link_id TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                decided_at TEXT
            );

            -- A pair is reported once. Rejected, completed, stale, conflicting and failed rows are
            -- history and may pile up, so the guard covers only the states that still expect a decision.
            CREATE UNIQUE INDEX IF NOT EXISTS symlink_suggestions_one_active_per_pair
                ON symlink_suggestions (radarr_instance_id, radarr_movie_id, sonarr_instance_id, sonarr_series_id, sonarr_episode_id)
                WHERE status IN (0,1,3);
            CREATE INDEX IF NOT EXISTS symlink_suggestions_status ON symlink_suggestions (status);

            CREATE TABLE IF NOT EXISTS symlink_rejections (
                radarr_instance_id TEXT NOT NULL,
                radarr_movie_id INTEGER NOT NULL,
                sonarr_instance_id TEXT NOT NULL,
                sonarr_series_id INTEGER NOT NULL,
                sonarr_episode_id INTEGER NOT NULL,
                detail TEXT,
                created_at TEXT NOT NULL,
                PRIMARY KEY (radarr_instance_id, radarr_movie_id, sonarr_instance_id, sonarr_series_id, sonarr_episode_id)
            );
            """
        ),
        (
            "symlinks-v2",
            """
            CREATE TABLE IF NOT EXISTS symlink_matching_state (
                instance_id TEXT NOT NULL PRIMARY KEY,
                kind INTEGER NOT NULL CHECK(kind BETWEEN 0 AND 1),
                signature TEXT NOT NULL,
                matcher_version INTEGER NOT NULL CHECK(matcher_version >= 0),
                config_revision INTEGER NOT NULL CHECK(config_revision >= 0),
                checked_at TEXT NOT NULL
            );
            """
        ),
        (
            "symlinks-v3",
            """
            -- A rename, like a scan, is a command the instance finishes later. Without its id a run that
            -- resumes between queueing it and its end would queue it again on every pass.
            ALTER TABLE symlink_links ADD COLUMN sonarr_rename_command_id TEXT;
            """
        )
    ];

    public async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach ((string id, string schema) in Steps)
        {
            if (await IsMigratedAsync(connection, id, cancellationToken)) continue;
            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            using SqliteCommand schemaCommand = connection.CreateCommand();
            schemaCommand.Transaction = transaction;
            schemaCommand.CommandText = schema;
            await schemaCommand.ExecuteNonQueryAsync(cancellationToken);

            using SqliteCommand marker = connection.CreateCommand();
            marker.Transaction = transaction;
            marker.CommandText = "INSERT OR IGNORE INTO schema_migrations VALUES ($id,$at)";
            marker.Parameters.AddWithValue("$id", id);
            marker.Parameters.AddWithValue("$at", SqliteScheduledTaskStore.Timestamp(clock.GetUtcNow()));
            await marker.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Symlink tables created ({MigrationId})", id);
        }
    }

    private static async Task<bool> IsMigratedAsync(SqliteConnection connection, string migrationId, CancellationToken cancellationToken)
    {
        using SqliteCommand probe = connection.CreateCommand();
        probe.CommandText = """
            SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_migrations'
            """;
        if (Convert.ToInt64(await probe.ExecuteScalarAsync(cancellationToken)) == 0) return false;
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE migration_id=$id";
        command.Parameters.AddWithValue("$id", migrationId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }
}
