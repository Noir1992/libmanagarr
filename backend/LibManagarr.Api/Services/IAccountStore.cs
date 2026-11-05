using LibManagarr.Api.Models.Auth;

namespace LibManagarr.Api.Services;

/// <summary>
/// Transactional persistence for the singleton account and its auth sessions. Backed by a
/// dedicated SQLite file (auth.db) inside the configured config directory so auth data stays
/// untouched by history cleanup and settings persistence.
/// </summary>
public interface IAccountStore
{
    Task<bool> TryChangePasswordAsync(AccountRecord account, string sessionTokenHash, string passwordHash,
        SecondFactorProbe? proof, DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> TryEnableTwoFactorAsync(AccountRecord account, TwoFactorSetupRecord setup, long firstStep,
        IReadOnlyList<RecoveryCodeRecord> codes, DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> TryDisableTwoFactorAsync(AccountRecord account, string sessionTokenHash, SecondFactorProbe proof,
        DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> TryRotateRecoveryCodesAsync(AccountRecord account, string sessionTokenHash, SecondFactorProbe proof,
        IReadOnlyList<RecoveryCodeRecord> codes, DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> TryCompleteLoginChallengeAsync(AccountRecord account, TwoFactorChallengeRecord challenge,
        SecondFactorProbe proof, DateTimeOffset now, CancellationToken cancellationToken);

    Task<AccountRecord?> GetAccountAsync(CancellationToken cancellationToken);

    Task<bool> TryCreateAccountAsync(string username, string normalizedUsername, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> UpdatePasswordHashOnlyAsync(AccountRecord account, string newPasswordHash, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Counts one failed identity proof and answers with the account row exactly as it stands
    /// afterwards, so the caller reports the state the store really produced instead of one it
    /// read before. A row that is already locked is left untouched: a request racing with the one
    /// that locked the account neither counts again nor moves the stored end.
    /// </summary>
    Task<AccountRecord?> RegisterFailedAccessAttemptAsync(int accountId, int maxFailedAttempts, TimeSpan lockoutDuration, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Clears the failure counter of a proven identity; an active lockout survives it.</summary>
    Task ResetAccessFailuresAsync(int accountId, DateTimeOffset now, CancellationToken cancellationToken);

    Task AddSessionAsync(SessionRecord session, CancellationToken cancellationToken);

    Task<SessionRecord?> FindSessionByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task<bool> RevokeSessionByTokenHashAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken);

    Task RevokeAllSessionsForAccountAsync(int accountId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Non-consuming check used before a multi-step change so a later failure never burns a code.</summary>
    Task<bool> HasUnusedRecoveryCodeAsync(int accountId, string codeHash, CancellationToken cancellationToken);

    Task<int> CountUnusedRecoveryCodesAsync(int accountId, CancellationToken cancellationToken);

    /// <summary>Revokes the open setup of the same session and inserts the new one atomically.</summary>
    Task CreateSetupAsync(TwoFactorSetupRecord setup, DateTimeOffset now, CancellationToken cancellationToken);

    Task<TwoFactorSetupRecord?> FindActiveSetupForSessionAsync(string sessionTokenHash, CancellationToken cancellationToken);

    /// <summary>Counts one failed confirm attempt; the setup is revoked once the limit is hit.</summary>
    Task IncrementSetupAttemptAsync(string setupId, int maxAttempts, DateTimeOffset now, CancellationToken cancellationToken);

    Task RevokeSetupsForSessionAsync(string sessionTokenHash, DateTimeOffset now, CancellationToken cancellationToken);

    Task AddChallengeAsync(TwoFactorChallengeRecord challenge, CancellationToken cancellationToken);

    /// <summary>Returns the challenge only while it is unexpired, unconsumed and unrevoked.</summary>
    Task<TwoFactorChallengeRecord?> FindChallengeByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Counts one wrong code; the challenge is dropped once the limit is hit.</summary>
    Task IncrementChallengeAttemptsAsync(string tokenHash, int maxWrongAttempts, DateTimeOffset now, CancellationToken cancellationToken);

}
