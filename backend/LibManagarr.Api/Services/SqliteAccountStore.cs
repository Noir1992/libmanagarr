using System.Globalization;
using Microsoft.Data.Sqlite;
using LibManagarr.Api.Models.Auth;
using LibManagarr.Api.Services.Backup;

namespace LibManagarr.Api.Services;

/// <summary>
/// Stores the singleton account and its auth sessions in a dedicated SQLite file (auth.db)
/// inside the configured config directory, kept separate from libmanagarr.db so cleanup and
/// settings persistence never touch auth data. Writes are serialized through a single gate;
/// the schema is versioned via PRAGMA user_version and applied lazily on first use. A newer
/// or unreadable database fails loudly and never falls back to a fresh setup.
/// </summary>
public sealed partial class SqliteAccountStore : IAccountStore
{
    private const string DatabaseFileName = "auth.db";
    private const int SchemaVersion = 2;

    private static readonly string[] SchemaStatements =
    [
        "PRAGMA journal_mode = WAL",
        """
        CREATE TABLE IF NOT EXISTS accounts (
            id INTEGER PRIMARY KEY,
            username TEXT NOT NULL,
            normalized_username TEXT NOT NULL UNIQUE,
            password_hash TEXT NOT NULL,
            security_version INTEGER NOT NULL DEFAULT 1,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            failed_access_count INTEGER NOT NULL DEFAULT 0,
            lockout_until TEXT,
            CONSTRAINT single_account CHECK (id = 1)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS sessions (
            id TEXT PRIMARY KEY,
            account_id INTEGER NOT NULL REFERENCES accounts (id),
            token_hash TEXT NOT NULL UNIQUE,
            security_version INTEGER NOT NULL,
            issued_at TEXT NOT NULL,
            expires_at TEXT NOT NULL,
            revoked_at TEXT
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_sessions_account_id ON sessions (account_id)"
    ];

    // Applied once when auth.db was written by a step-1 build (user_version 1) or is brand new.
    private static readonly string[] SchemaUpgradeToV2Statements =
    [
        "ALTER TABLE accounts ADD COLUMN totp_secret TEXT",
        "ALTER TABLE accounts ADD COLUMN two_factor_enabled INTEGER NOT NULL DEFAULT 0",
        "ALTER TABLE accounts ADD COLUMN last_used_step INTEGER",
        """
        CREATE TABLE IF NOT EXISTS recovery_codes (
            id TEXT PRIMARY KEY,
            account_id INTEGER NOT NULL REFERENCES accounts (id),
            code_hash TEXT NOT NULL UNIQUE,
            created_at TEXT NOT NULL,
            used_at TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS two_factor_setups (
            id TEXT PRIMARY KEY,
            account_id INTEGER NOT NULL REFERENCES accounts (id),
            session_token_hash TEXT NOT NULL,
            protected_secret TEXT NOT NULL,
            created_at TEXT NOT NULL,
            expires_at TEXT NOT NULL,
            attempt_count INTEGER NOT NULL DEFAULT 0,
            completed_at TEXT,
            revoked_at TEXT
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_two_factor_setups_session_token_hash ON two_factor_setups (session_token_hash)",
        """
        CREATE TABLE IF NOT EXISTS two_factor_challenges (
            token_hash TEXT PRIMARY KEY,
            account_id INTEGER NOT NULL REFERENCES accounts (id),
            security_version INTEGER NOT NULL,
            created_at TEXT NOT NULL,
            expires_at TEXT NOT NULL,
            wrong_attempt_count INTEGER NOT NULL DEFAULT 0,
            completed_at TEXT,
            revoked_at TEXT
        )
        """
    ];

    private readonly string connectionString;
    private readonly BackupWriteGate storageGate;
    private bool schemaApplied;

    public SqliteAccountStore(StoragePaths storagePaths, BackupOperationCoordinator writes)
    {
        Directory.CreateDirectory(storagePaths.ConfigDirectory);
        storageGate = new BackupWriteGate(writes);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(storagePaths.ConfigDirectory, DatabaseFileName)
        }.ToString();
    }

    public async Task<AccountRecord?> GetAccountAsync(CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, username, normalized_username, password_hash, security_version,
                    created_at, updated_at, failed_access_count, lockout_until,
                    totp_secret, two_factor_enabled, last_used_step
                FROM accounts
                WHERE id = 1
                """;
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadAccount(reader);
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task<bool> TryCreateAccountAsync(string username, string normalizedUsername, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO accounts (id, username, normalized_username, password_hash,
                    security_version, created_at, updated_at, failed_access_count)
                VALUES (1, $username, $normalizedUsername, $passwordHash, 1, $now, $now, 0)
                ON CONFLICT DO NOTHING
                """;
            command.Parameters.AddWithValue("$username", username);
            command.Parameters.AddWithValue("$normalizedUsername", normalizedUsername);
            command.Parameters.AddWithValue("$passwordHash", passwordHash);
            command.Parameters.AddWithValue("$now", FormatDate(now));
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task<bool> UpdatePasswordHashOnlyAsync(AccountRecord account, string newPasswordHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE accounts
                SET password_hash = $passwordHash, updated_at = $now
                WHERE id = $accountId AND security_version = $version AND password_hash = $expectedHash
                """;
            command.Parameters.AddWithValue("$passwordHash", newPasswordHash);
            command.Parameters.AddWithValue("$now", FormatDate(now));
            command.Parameters.AddWithValue("$accountId", account.Id);
            command.Parameters.AddWithValue("$version", account.SecurityVersion);
            command.Parameters.AddWithValue("$expectedHash", account.PasswordHash);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task RegisterFailedAccessAttemptAsync(int accountId, int maxFailedAttempts, TimeSpan lockoutDuration, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE accounts
                SET failed_access_count = CASE
                        WHEN failed_access_count + 1 >= $maxFailedAttempts THEN 0
                        ELSE failed_access_count + 1
                    END,
                    lockout_until = CASE
                        WHEN failed_access_count + 1 >= $maxFailedAttempts THEN $lockoutUntil
                        ELSE lockout_until
                    END,
                    updated_at = $now
                WHERE id = $accountId
                """;
            command.Parameters.AddWithValue("$maxFailedAttempts", maxFailedAttempts);
            command.Parameters.AddWithValue("$lockoutUntil", FormatDate(now + lockoutDuration));
            command.Parameters.AddWithValue("$now", FormatDate(now));
            command.Parameters.AddWithValue("$accountId", accountId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task ResetAccessFailuresAsync(int accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE accounts
                SET failed_access_count = 0, lockout_until = NULL, updated_at = $now
                WHERE id = $accountId
                """;
            command.Parameters.AddWithValue("$now", FormatDate(now));
            command.Parameters.AddWithValue("$accountId", accountId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task AddSessionAsync(SessionRecord session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO sessions (id, account_id, token_hash, security_version,
                    issued_at, expires_at, revoked_at)
                VALUES ($id, $accountId, $tokenHash, $securityVersion, $issuedAt, $expiresAt, NULL)
                """;
            command.Parameters.AddWithValue("$id", session.Id);
            command.Parameters.AddWithValue("$accountId", session.AccountId);
            command.Parameters.AddWithValue("$tokenHash", session.TokenHash);
            command.Parameters.AddWithValue("$securityVersion", session.SecurityVersion);
            command.Parameters.AddWithValue("$issuedAt", FormatDate(session.IssuedAt));
            command.Parameters.AddWithValue("$expiresAt", FormatDate(session.ExpiresAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task<SessionRecord?> FindSessionByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, account_id, token_hash, security_version, issued_at, expires_at, revoked_at
                FROM sessions
                WHERE token_hash = $tokenHash
                """;
            command.Parameters.AddWithValue("$tokenHash", tokenHash);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadSession(reader);
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task<bool> RevokeSessionByTokenHashAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE sessions
                SET revoked_at = $now
                WHERE token_hash = $tokenHash AND revoked_at IS NULL
                """;
            command.Parameters.AddWithValue("$now", FormatDate(now));
            command.Parameters.AddWithValue("$tokenHash", tokenHash);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task RevokeAllSessionsForAccountAsync(int accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE sessions
                SET revoked_at = $now
                WHERE account_id = $accountId AND revoked_at IS NULL
                """;
            command.Parameters.AddWithValue("$now", FormatDate(now));
            command.Parameters.AddWithValue("$accountId", accountId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task<bool> HasUnusedRecoveryCodeAsync(int accountId, string codeHash, CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT 1
                FROM recovery_codes
                WHERE account_id = $accountId AND code_hash = $codeHash AND used_at IS NULL
                LIMIT 1
                """;
            command.Parameters.AddWithValue("$accountId", accountId);
            command.Parameters.AddWithValue("$codeHash", codeHash);
            return await command.ExecuteScalarAsync(cancellationToken) is not null;
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task<int> CountUnusedRecoveryCodesAsync(int accountId, CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM recovery_codes WHERE account_id = $accountId AND used_at IS NULL";
            command.Parameters.AddWithValue("$accountId", accountId);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture);
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task CreateSetupAsync(TwoFactorSetupRecord setup, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setup);

        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await using (SqliteCommand revokePrevious = connection.CreateCommand())
            {
                revokePrevious.Transaction = transaction;
                revokePrevious.CommandText = """
                    UPDATE two_factor_setups
                    SET revoked_at = $now
                    WHERE session_token_hash = $sessionTokenHash AND completed_at IS NULL AND revoked_at IS NULL
                    """;
                revokePrevious.Parameters.AddWithValue("$now", FormatDate(now));
                revokePrevious.Parameters.AddWithValue("$sessionTokenHash", setup.SessionTokenHash);
                await revokePrevious.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (SqliteCommand insertSetup = connection.CreateCommand())
            {
                insertSetup.Transaction = transaction;
                insertSetup.CommandText = """
                    INSERT INTO two_factor_setups (id, account_id, session_token_hash, protected_secret,
                        created_at, expires_at, attempt_count, completed_at, revoked_at)
                    VALUES ($id, $accountId, $sessionTokenHash, $protectedSecret, $createdAt, $expiresAt, 0, NULL, NULL)
                    """;
                insertSetup.Parameters.AddWithValue("$id", setup.Id);
                insertSetup.Parameters.AddWithValue("$accountId", setup.AccountId);
                insertSetup.Parameters.AddWithValue("$sessionTokenHash", setup.SessionTokenHash);
                insertSetup.Parameters.AddWithValue("$protectedSecret", setup.ProtectedSecret);
                insertSetup.Parameters.AddWithValue("$createdAt", FormatDate(setup.CreatedAt));
                insertSetup.Parameters.AddWithValue("$expiresAt", FormatDate(setup.ExpiresAt));
                await insertSetup.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task<TwoFactorSetupRecord?> FindActiveSetupForSessionAsync(string sessionTokenHash, CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, account_id, session_token_hash, protected_secret, created_at, expires_at, attempt_count
                FROM two_factor_setups
                WHERE session_token_hash = $sessionTokenHash AND completed_at IS NULL AND revoked_at IS NULL
                """;
            command.Parameters.AddWithValue("$sessionTokenHash", sessionTokenHash);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadSetup(reader);
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task IncrementSetupAttemptAsync(string setupId, int maxAttempts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE two_factor_setups
                SET attempt_count = CASE WHEN attempt_count + 1 >= $maxAttempts THEN attempt_count ELSE attempt_count + 1 END,
                    revoked_at = CASE WHEN attempt_count + 1 >= $maxAttempts THEN $now ELSE revoked_at END
                WHERE id = $setupId AND completed_at IS NULL AND revoked_at IS NULL
                """;
            command.Parameters.AddWithValue("$maxAttempts", maxAttempts);
            command.Parameters.AddWithValue("$now", FormatDate(now));
            command.Parameters.AddWithValue("$setupId", setupId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task RevokeSetupsForSessionAsync(string sessionTokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE two_factor_setups
                SET revoked_at = $now
                WHERE session_token_hash = $sessionTokenHash AND completed_at IS NULL AND revoked_at IS NULL
                """;
            command.Parameters.AddWithValue("$now", FormatDate(now));
            command.Parameters.AddWithValue("$sessionTokenHash", sessionTokenHash);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task AddChallengeAsync(TwoFactorChallengeRecord challenge, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO two_factor_challenges (token_hash, account_id, security_version,
                    created_at, expires_at, wrong_attempt_count, completed_at, revoked_at)
                VALUES ($tokenHash, $accountId, $securityVersion, $createdAt, $expiresAt, 0, NULL, NULL)
                """;
            command.Parameters.AddWithValue("$tokenHash", challenge.TokenHash);
            command.Parameters.AddWithValue("$accountId", challenge.AccountId);
            command.Parameters.AddWithValue("$securityVersion", challenge.SecurityVersion);
            command.Parameters.AddWithValue("$createdAt", FormatDate(challenge.CreatedAt));
            command.Parameters.AddWithValue("$expiresAt", FormatDate(challenge.ExpiresAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task<TwoFactorChallengeRecord?> FindChallengeByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT token_hash, account_id, security_version, created_at, expires_at, wrong_attempt_count
                FROM two_factor_challenges
                WHERE token_hash = $tokenHash AND completed_at IS NULL AND revoked_at IS NULL
                """;
            command.Parameters.AddWithValue("$tokenHash", tokenHash);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadChallenge(reader);
        }
        finally
        {
            storageGate.Release();
        }
    }

    public async Task IncrementChallengeAttemptsAsync(string tokenHash, int maxWrongAttempts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE two_factor_challenges
                SET wrong_attempt_count = wrong_attempt_count + 1,
                    revoked_at = CASE WHEN wrong_attempt_count + 1 >= $maxWrongAttempts THEN $now ELSE revoked_at END
                WHERE token_hash = $tokenHash AND completed_at IS NULL AND revoked_at IS NULL
                """;
            command.Parameters.AddWithValue("$maxWrongAttempts", maxWrongAttempts);
            command.Parameters.AddWithValue("$now", FormatDate(now));
            command.Parameters.AddWithValue("$tokenHash", tokenHash);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            storageGate.Release();
        }
    }

    private static async Task RevokeSessionsChallengesAndSetupsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using (SqliteCommand revokeSessions = connection.CreateCommand())
        {
            revokeSessions.Transaction = transaction;
            revokeSessions.CommandText = """
                UPDATE sessions
                SET revoked_at = $now
                WHERE account_id = $accountId AND revoked_at IS NULL
                """;
            revokeSessions.Parameters.AddWithValue("$now", FormatDate(now));
            revokeSessions.Parameters.AddWithValue("$accountId", accountId);
            await revokeSessions.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (SqliteCommand revokeChallenges = connection.CreateCommand())
        {
            revokeChallenges.Transaction = transaction;
            revokeChallenges.CommandText = """
                UPDATE two_factor_challenges
                SET revoked_at = $now
                WHERE account_id = $accountId AND completed_at IS NULL AND revoked_at IS NULL
                """;
            revokeChallenges.Parameters.AddWithValue("$now", FormatDate(now));
            revokeChallenges.Parameters.AddWithValue("$accountId", accountId);
            await revokeChallenges.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (SqliteCommand revokeSetups = connection.CreateCommand())
        {
            revokeSetups.Transaction = transaction;
            revokeSetups.CommandText = """
                UPDATE two_factor_setups
                SET revoked_at = $now
                WHERE account_id = $accountId AND completed_at IS NULL AND revoked_at IS NULL
                """;
            revokeSetups.Parameters.AddWithValue("$now", FormatDate(now));
            revokeSetups.Parameters.AddWithValue("$accountId", accountId);
            await revokeSetups.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static AccountRecord ReadAccount(SqliteDataReader reader)
    {
        return new AccountRecord
        {
            Id = reader.GetInt32(0),
            Username = reader.GetString(1),
            NormalizedUsername = reader.GetString(2),
            PasswordHash = reader.GetString(3),
            SecurityVersion = reader.GetInt32(4),
            CreatedAt = ParseDate(reader.GetString(5)),
            UpdatedAt = ParseDate(reader.GetString(6)),
            FailedAccessCount = reader.GetInt32(7),
            LockoutUntil = reader.IsDBNull(8) ? null : ParseDate(reader.GetString(8)),
            TotpSecret = reader.IsDBNull(9) ? null : reader.GetString(9),
            TwoFactorEnabled = reader.GetInt32(10) != 0,
            LastUsedStep = reader.IsDBNull(11) ? null : reader.GetInt64(11)
        };
    }

    private static SessionRecord ReadSession(SqliteDataReader reader)
    {
        return new SessionRecord
        {
            Id = reader.GetString(0),
            AccountId = reader.GetInt32(1),
            TokenHash = reader.GetString(2),
            SecurityVersion = reader.GetInt32(3),
            IssuedAt = ParseDate(reader.GetString(4)),
            ExpiresAt = ParseDate(reader.GetString(5)),
            RevokedAt = reader.IsDBNull(6) ? null : ParseDate(reader.GetString(6))
        };
    }

    private static TwoFactorSetupRecord ReadSetup(SqliteDataReader reader)
    {
        return new TwoFactorSetupRecord
        {
            Id = reader.GetString(0),
            AccountId = reader.GetInt32(1),
            SessionTokenHash = reader.GetString(2),
            ProtectedSecret = reader.GetString(3),
            CreatedAt = ParseDate(reader.GetString(4)),
            ExpiresAt = ParseDate(reader.GetString(5)),
            AttemptCount = reader.GetInt32(6)
        };
    }

    private static TwoFactorChallengeRecord ReadChallenge(SqliteDataReader reader)
    {
        return new TwoFactorChallengeRecord
        {
            TokenHash = reader.GetString(0),
            AccountId = reader.GetInt32(1),
            SecurityVersion = reader.GetInt32(2),
            CreatedAt = ParseDate(reader.GetString(3)),
            ExpiresAt = ParseDate(reader.GetString(4)),
            WrongAttemptCount = reader.GetInt32(5)
        };
    }

    private static string FormatDate(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

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
                await EnsureSchemaAppliedAsync(connection, cancellationToken);
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

    private static async Task EnsureSchemaAppliedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (SqliteCommand journal = connection.CreateCommand())
        {
            journal.CommandText = "PRAGMA journal_mode = WAL";
            await journal.ExecuteNonQueryAsync(cancellationToken);
        }
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        long storedVersion;
        await using (SqliteCommand versionCommand = connection.CreateCommand())
        {
            versionCommand.Transaction = transaction;
            versionCommand.CommandText = "PRAGMA user_version";
            storedVersion = Convert.ToInt64(await versionCommand.ExecuteScalarAsync(cancellationToken) ?? 0L, CultureInfo.InvariantCulture);
            if (storedVersion > SchemaVersion)
                throw new InvalidOperationException($"auth.db schema version {storedVersion} is newer than the supported version {SchemaVersion}.");
        }
        foreach (string statement in SchemaStatements.Skip(1))
        {
            await using SqliteCommand schema = connection.CreateCommand();
            schema.Transaction = transaction;
            schema.CommandText = statement;
            await schema.ExecuteNonQueryAsync(cancellationToken);
        }
        if (storedVersion < 2)
        {
            foreach (string statement in SchemaUpgradeToV2Statements)
            {
                await using SqliteCommand upgrade = connection.CreateCommand();
                upgrade.Transaction = transaction;
                upgrade.CommandText = statement;
                await upgrade.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        await using SqliteCommand versionUpdate = connection.CreateCommand();
        versionUpdate.Transaction = transaction;
        versionUpdate.CommandText = $"PRAGMA user_version = {SchemaVersion}";
        await versionUpdate.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
