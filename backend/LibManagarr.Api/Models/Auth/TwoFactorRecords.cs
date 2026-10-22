namespace LibManagarr.Api.Models.Auth;

/// <summary>
/// Pending two-factor setup bound to the session that started it. The secret is already Data
/// Protection encrypted; the row expires on its own and at most one setup per session stays
/// open. Confirmation flips 2FA on, so a wrong or abandoned setup never changes the account.
/// </summary>
public sealed class TwoFactorSetupRecord
{
    public required string Id { get; init; }

    public required int AccountId { get; init; }

    public required string SessionTokenHash { get; init; }

    public required string ProtectedSecret { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required int AttemptCount { get; init; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;
}

/// <summary>
/// Persistent login challenge issued after a correct password when 2FA is active. It is bound
/// to the account's security version, lives five minutes and is dropped after five wrong
/// codes. It is never an application authentication on its own.
/// </summary>
public sealed class TwoFactorChallengeRecord
{
    public required string TokenHash { get; init; }

    public required int AccountId { get; init; }

    public required int SecurityVersion { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required int WrongAttemptCount { get; init; }

    public bool IsUsable(DateTimeOffset now) => ExpiresAt > now;
}

/// <summary>One stored recovery code; only the SHA-256 hash of the plaintext is persisted.</summary>
public sealed record RecoveryCodeRecord
{
    public required string Id { get; init; }

    public required string CodeHash { get; init; }
}
