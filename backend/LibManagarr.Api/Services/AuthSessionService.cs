using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using LibManagarr.Api.Models.Auth;

namespace LibManagarr.Api.Services;

/// <summary>
/// Issues and validates persistent auth sessions. Every session is backed by an auth.db row
/// that holds only the hash of a cryptographically random token; the plaintext token lives in
/// the cookie claim and expires absolutely 30 days after issuance without sliding renewal.
/// </summary>
public sealed class AuthSessionService
{
    public const string SessionTokenClaimType = "libmanagarr:session-token";
    public const string SecurityVersionClaimType = "libmanagarr:security-version";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    private readonly IAccountStore accountStore;
    private readonly TimeProvider timeProvider;

    public AuthSessionService(IAccountStore accountStore, TimeProvider timeProvider)
    {
        this.accountStore = accountStore;
        this.timeProvider = timeProvider;
    }

    public async Task<IssuedSession> IssueSessionAsync(AccountRecord account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        string sessionToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset expiresAt = now + SessionLifetime;

        await accountStore.AddSessionAsync(
            new SessionRecord
            {
                Id = UuidV7.Create(),
                AccountId = account.Id,
                TokenHash = HashToken(sessionToken),
                SecurityVersion = account.SecurityVersion,
                IssuedAt = now,
                ExpiresAt = expiresAt
            },
            cancellationToken);

        ClaimsIdentity identity = new(
            [
                new Claim(ClaimTypes.NameIdentifier, account.Id.ToString(CultureInfo.InvariantCulture)),
                new Claim(SessionTokenClaimType, sessionToken),
                new Claim(SecurityVersionClaimType, account.SecurityVersion.ToString(CultureInfo.InvariantCulture))
            ],
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);

        return new IssuedSession(new ClaimsPrincipal(identity), sessionToken, expiresAt);
    }

    public async Task<bool> IsSessionValidAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken)
    {
        string? sessionToken = principal?.FindFirstValue(SessionTokenClaimType);
        string? securityVersionClaim = principal?.FindFirstValue(SecurityVersionClaimType);
        if (string.IsNullOrEmpty(sessionToken) || string.IsNullOrEmpty(securityVersionClaim))
        {
            return false;
        }

        SessionRecord? session = await accountStore.FindSessionByTokenHashAsync(HashToken(sessionToken), cancellationToken);
        if (session is null || !session.IsUsable(timeProvider.GetUtcNow()))
        {
            return false;
        }

        AccountRecord? account = await accountStore.GetAccountAsync(cancellationToken);
        if (account is null || account.Id != session.AccountId)
        {
            return false;
        }

        return session.SecurityVersion == account.SecurityVersion &&
            securityVersionClaim == account.SecurityVersion.ToString(CultureInfo.InvariantCulture);
    }

    public async Task RevokeCurrentSessionAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken)
    {
        string? sessionToken = principal?.FindFirstValue(SessionTokenClaimType);
        if (string.IsNullOrEmpty(sessionToken))
        {
            return;
        }

        await accountStore.RevokeSessionByTokenHashAsync(HashToken(sessionToken), timeProvider.GetUtcNow(), cancellationToken);
    }

    /// <summary>
    /// Hash of the session token of a request, the only form it may be compared or handed to another
    /// service in. Backup tickets bind to this value, so a stolen ticket cannot be replayed from
    /// another browser.
    /// </summary>
    public static string HashSessionToken(HttpContext httpContext) =>
        HashToken(httpContext.User.FindFirstValue(SessionTokenClaimType) ?? string.Empty);

    internal static string HashToken(string sessionToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionToken)));
}
