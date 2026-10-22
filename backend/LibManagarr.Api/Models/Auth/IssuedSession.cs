using System.Security.Claims;

namespace LibManagarr.Api.Models.Auth;

/// <summary>
/// A freshly issued session: the principal to sign in with, the plaintext random session
/// token for the cookie and the absolute expiry shared by cookie and database row.
/// </summary>
public sealed record IssuedSession(ClaimsPrincipal Principal, string SessionToken, DateTimeOffset ExpiresAt);
