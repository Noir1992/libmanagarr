namespace LibManagarr.Api.Models.Auth;

/// <summary>
/// Persisted auth session from auth.db. Only the hash of the cryptographically random session
/// token is stored; the plaintext token lives exclusively inside the issued cookie.
/// </summary>
public sealed class SessionRecord
{
    public required string Id { get; init; }

    public required int AccountId { get; init; }

    public required string TokenHash { get; init; }

    public required int SecurityVersion { get; init; }

    public required DateTimeOffset IssuedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
