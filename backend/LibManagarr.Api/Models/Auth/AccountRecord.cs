namespace LibManagarr.Api.Models.Auth;

/// <summary>
/// Persisted singleton account row from auth.db. The password hash never leaves the auth
/// storage layer and must not be exposed through any settings or account API.
/// </summary>
public sealed class AccountRecord
{
    public required int Id { get; init; }

    public required string Username { get; init; }

    public required string NormalizedUsername { get; init; }

    public required string PasswordHash { get; init; }

    public required int SecurityVersion { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required int FailedAccessCount { get; init; }

    public DateTimeOffset? LockoutUntil { get; init; }

    /// <summary>Data Protection encrypted TOTP secret; never returned through any API.</summary>
    public string? TotpSecret { get; init; }

    public bool TwoFactorEnabled { get; init; }

    /// <summary>Highest TOTP time step already accepted for this account (replay marker).</summary>
    public long? LastUsedStep { get; init; }

    public bool IsLockedOut(DateTimeOffset now) => LockoutUntil.HasValue && LockoutUntil.Value > now;
}
