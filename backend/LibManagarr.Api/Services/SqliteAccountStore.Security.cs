using Microsoft.Data.Sqlite;
using LibManagarr.Api.Models.Auth;

namespace LibManagarr.Api.Services;

public sealed partial class SqliteAccountStore
{
    public Task<bool> TryChangePasswordAsync(AccountRecord account, string sessionTokenHash, string passwordHash,
        SecondFactorProbe? proof, DateTimeOffset now, CancellationToken cancellationToken) =>
        CommitSecurityChangeAsync(account, sessionTokenHash, proof, now, async (connection, transaction) =>
        {
            await ExecuteSecuritySqlAsync(connection, transaction,
                """
                UPDATE accounts SET password_hash = $passwordHash, security_version = security_version + 1,
                    failed_access_count = 0, lockout_until = NULL, updated_at = $now WHERE id = $accountId
                """, cancellationToken, ("$passwordHash", passwordHash), ("$now", FormatDate(now)), ("$accountId", account.Id));
            await RevokeSessionsChallengesAndSetupsAsync(connection, transaction, account.Id, now, cancellationToken);
            return true;
        }, cancellationToken);

    public Task<bool> TryEnableTwoFactorAsync(AccountRecord account, TwoFactorSetupRecord setup, long firstStep,
        IReadOnlyList<RecoveryCodeRecord> codes, DateTimeOffset now, CancellationToken cancellationToken) =>
        CommitSecurityChangeAsync(account, setup.SessionTokenHash, null, now, async (connection, transaction) =>
        {
            if (account.TwoFactorEnabled || setup.AccountId != account.Id) return false;
            int completed = await ExecuteSecuritySqlAsync(connection, transaction,
                """
                UPDATE two_factor_setups SET completed_at = $now
                WHERE id = $id AND account_id = $accountId AND session_token_hash = $sessionHash
                    AND protected_secret = $secret AND completed_at IS NULL AND revoked_at IS NULL
                    AND expires_at > $now AND attempt_count < $maxAttempts
                """, cancellationToken, ("$now", FormatDate(now)), ("$id", setup.Id), ("$accountId", account.Id),
                ("$sessionHash", setup.SessionTokenHash), ("$secret", setup.ProtectedSecret), ("$maxAttempts", TwoFactorService.MaxSetupAttempts));
            if (completed != 1) return false;
            await ExecuteSecuritySqlAsync(connection, transaction,
                """
                UPDATE accounts SET totp_secret = $secret, two_factor_enabled = 1, last_used_step = $step,
                    security_version = security_version + 1, failed_access_count = 0, lockout_until = NULL,
                    updated_at = $now WHERE id = $accountId
                """, cancellationToken, ("$secret", setup.ProtectedSecret), ("$step", firstStep),
                ("$now", FormatDate(now)), ("$accountId", account.Id));
            await ReplaceSecurityRecoveryCodesAsync(connection, transaction, account.Id, codes, now, cancellationToken);
            await RevokeSessionsChallengesAndSetupsAsync(connection, transaction, account.Id, now, cancellationToken);
            return true;
        }, cancellationToken);

    public Task<bool> TryDisableTwoFactorAsync(AccountRecord account, string sessionTokenHash, SecondFactorProbe proof,
        DateTimeOffset now, CancellationToken cancellationToken) =>
        CommitSecurityChangeAsync(account, sessionTokenHash, proof, now, async (connection, transaction) =>
        {
            if (!account.TwoFactorEnabled) return false;
            await ExecuteSecuritySqlAsync(connection, transaction,
                """
                UPDATE accounts SET totp_secret = NULL, two_factor_enabled = 0, last_used_step = NULL,
                    security_version = security_version + 1, updated_at = $now WHERE id = $accountId
                """, cancellationToken, ("$now", FormatDate(now)), ("$accountId", account.Id));
            await ExecuteSecuritySqlAsync(connection, transaction, "DELETE FROM recovery_codes WHERE account_id = $accountId",
                cancellationToken, ("$accountId", account.Id));
            await RevokeSessionsChallengesAndSetupsAsync(connection, transaction, account.Id, now, cancellationToken);
            return true;
        }, cancellationToken);

    public Task<bool> TryRotateRecoveryCodesAsync(AccountRecord account, string sessionTokenHash, SecondFactorProbe proof,
        IReadOnlyList<RecoveryCodeRecord> codes, DateTimeOffset now, CancellationToken cancellationToken) =>
        CommitSecurityChangeAsync(account, sessionTokenHash, proof, now, async (connection, transaction) =>
        {
            if (!account.TwoFactorEnabled) return false;
            await ReplaceSecurityRecoveryCodesAsync(connection, transaction, account.Id, codes, now, cancellationToken);
            return true;
        }, cancellationToken);

    public Task<bool> TryCompleteLoginChallengeAsync(AccountRecord account, TwoFactorChallengeRecord challenge,
        SecondFactorProbe proof, DateTimeOffset now, CancellationToken cancellationToken) =>
        CommitSecurityChangeAsync(account, null, proof, now, async (connection, transaction) =>
        {
            if (!account.TwoFactorEnabled || challenge.AccountId != account.Id) return false;
            int completed = await ExecuteSecuritySqlAsync(connection, transaction,
                """
                UPDATE two_factor_challenges SET completed_at = $now
                WHERE token_hash = $tokenHash AND account_id = $accountId AND security_version = $version
                    AND completed_at IS NULL AND revoked_at IS NULL AND expires_at > $now
                    AND wrong_attempt_count < $maxAttempts
                """, cancellationToken, ("$now", FormatDate(now)), ("$tokenHash", challenge.TokenHash),
                ("$accountId", account.Id), ("$version", account.SecurityVersion), ("$maxAttempts", TwoFactorService.MaxWrongChallengeAttempts));
            if (completed != 1) return false;
            await ExecuteSecuritySqlAsync(connection, transaction,
                "UPDATE accounts SET failed_access_count = 0, lockout_until = NULL WHERE id = $accountId",
                cancellationToken, ("$accountId", account.Id));
            return true;
        }, cancellationToken);

    private async Task<bool> CommitSecurityChangeAsync(AccountRecord expected, string? sessionTokenHash,
        SecondFactorProbe? proof, DateTimeOffset now, Func<SqliteConnection, SqliteTransaction, Task<bool>> change,
        CancellationToken cancellationToken)
    {
        await storageGate.WaitAsync(cancellationToken);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            if (!await IsSecurityContextValidAsync(connection, transaction, expected, sessionTokenHash, now, cancellationToken)
                || !await ConsumeSecurityProofAsync(connection, transaction, expected, proof, now, cancellationToken)
                || !await change(connection, transaction))
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        finally
        {
            storageGate.Release();
        }
    }

    private static async Task<bool> IsSecurityContextValidAsync(SqliteConnection connection, SqliteTransaction transaction,
        AccountRecord expected, string? sessionTokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT security_version, password_hash, two_factor_enabled, lockout_until FROM accounts WHERE id = $id
            """;
        command.Parameters.AddWithValue("$id", expected.Id);
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)
                || reader.GetInt32(0) != expected.SecurityVersion
                || reader.GetString(1) != expected.PasswordHash
                || (reader.GetInt32(2) != 0) != expected.TwoFactorEnabled
                || (!await reader.IsDBNullAsync(3, cancellationToken) && ParseDate(reader.GetString(3)) > now)) return false;
        }
        if (sessionTokenHash is null) return true;
        command.CommandText = """
            SELECT 1 FROM sessions WHERE account_id = $id AND token_hash = $hash
                AND security_version = $version AND revoked_at IS NULL AND expires_at > $now
            """;
        command.Parameters.AddWithValue("$hash", sessionTokenHash);
        command.Parameters.AddWithValue("$version", expected.SecurityVersion);
        command.Parameters.AddWithValue("$now", FormatDate(now));
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<bool> ConsumeSecurityProofAsync(SqliteConnection connection, SqliteTransaction transaction,
        AccountRecord account, SecondFactorProbe? proof, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!account.TwoFactorEnabled) return proof is null;
        if (proof?.Status != SecondFactorProofStatus.Success) return false;
        if (proof.Step.HasValue)
        {
            return await ExecuteSecuritySqlAsync(connection, transaction,
                "UPDATE accounts SET last_used_step = $step WHERE id = $id AND (last_used_step IS NULL OR last_used_step < $step)",
                cancellationToken, ("$step", proof.Step.Value), ("$id", account.Id)) == 1;
        }
        if (proof.RecoveryCodeHash is null) return false;
        return await ExecuteSecuritySqlAsync(connection, transaction,
            "UPDATE recovery_codes SET used_at = $now WHERE account_id = $id AND code_hash = $hash AND used_at IS NULL",
            cancellationToken, ("$now", FormatDate(now)), ("$id", account.Id), ("$hash", proof.RecoveryCodeHash)) == 1;
    }

    private static async Task ReplaceSecurityRecoveryCodesAsync(SqliteConnection connection, SqliteTransaction transaction,
        int accountId, IReadOnlyList<RecoveryCodeRecord> codes, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await ExecuteSecuritySqlAsync(connection, transaction, "DELETE FROM recovery_codes WHERE account_id = $id",
            cancellationToken, ("$id", accountId));
        foreach (RecoveryCodeRecord code in codes)
        {
            await ExecuteSecuritySqlAsync(connection, transaction,
                "INSERT INTO recovery_codes (id, account_id, code_hash, created_at) VALUES ($codeId, $id, $hash, $now)",
                cancellationToken, ("$codeId", code.Id), ("$id", accountId), ("$hash", code.CodeHash), ("$now", FormatDate(now)));
        }
    }

    private static async Task<int> ExecuteSecuritySqlAsync(SqliteConnection connection, SqliteTransaction transaction,
        string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters) command.Parameters.AddWithValue(name, value);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
