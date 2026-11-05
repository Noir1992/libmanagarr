using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using LibManagarr.Api.Models.Auth;
using LibManagarr.Api.Models;
using LibManagarr.Api.Options;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Endpoints;

/// <summary>
/// Public authentication endpoints: state probe, CSRF token issuance, initial setup, login and
/// logout. They are the only anonymous API surface; the antiforgery middleware still guards
/// every mutating call before it reaches these handlers, and login/setup additionally run
/// behind the per-source-IP rate limit policy.
/// </summary>
public static class AuthEndpoints
{
    public const string CsrfCookieName = "XSRF-TOKEN";
    public const string ChallengeCookieName = "LibManagarr.TfaChallenge";
    private const string ChallengeCookiePath = "/api/auth";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/auth");
        group.AllowAnonymous();
        group.AddEndpointFilter(static async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });

        group.MapGet("/state", GetStateAsync);
        group.MapGet("/csrf", IssueCsrfToken);
        group.MapPost("/setup", SetupAsync).RequireRateLimiting("auth");
        group.MapPost("/login", LoginAsync).RequireRateLimiting("auth");
        group.MapPost("/logout", LogoutAsync);
        group.MapGet("/two-factor/state", TwoFactorStateAsync);
        group.MapPost("/two-factor/verify", VerifyTwoFactorAsync).RequireRateLimiting("auth");

        return endpoints;
    }

    private static async Task<Results<Ok<AuthStateResponse>, ProblemHttpResult>> GetStateAsync(
        AccountService accountService,
        ISettingsService settingsService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        AccountRecord? account;
        try
        {
            account = await accountService.GetAccountAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or IOException)
        {
            return AuthProblems.StorageUnavailable();
        }

        // Before the first account exists nobody has chosen a language yet, so the auth
        // pages keep translating in the browser language and the probe reports null here.
        AppLanguage? language = account is null ? null : (await settingsService.GetAsync(cancellationToken)).Language;

        return TypedResults.Ok(new AuthStateResponse
        {
            SetupRequired = account is null,
            Authenticated = user.Identity?.IsAuthenticated == true,
            Language = language
        });
    }

    private static IResult IssueCsrfToken(HttpContext httpContext, IAntiforgery antiforgery, IOptions<AuthOptions> options)
    {
        httpContext.Response.Headers.CacheControl = "no-cache, no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(httpContext);
        httpContext.Response.Cookies.Append(
            CsrfCookieName,
            tokens.RequestToken ?? string.Empty,
            new CookieOptions
            {
                HttpOnly = false,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Secure = options.Value.RequireSecureCookies || httpContext.Request.IsHttps
            });

        return TypedResults.NoContent();
    }

    private static async Task<Results<StatusCodeHttpResult, ProblemHttpResult>> SetupAsync(
        SetupRequest request,
        HttpContext httpContext,
        AccountService accountService,
        AuthSessionService authSessionService,
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        AppLanguage language = request.Language ?? AppLanguage.En;
        if (!Enum.IsDefined(language))
            return AuthProblems.Validation([new AccountFieldError("language", "invalid_language")]);
        AccountOperationResult result = await accountService.SetupAsync(request.Username, request.Password, request.ConfirmPassword, cancellationToken);
        if (result.Status == AccountOperationStatus.Success)
        {
            await settingsService.UpdateAsync(settings => settings.Language = language, cancellationToken);
        }
        return result.Status switch
        {
            AccountOperationStatus.Success => await SignInWithNewSessionAsync(httpContext, result.Account!, StatusCodes.Status201Created, authSessionService, cancellationToken),
            AccountOperationStatus.ValidationFailed => AuthProblems.Validation(result.Errors!),
            AccountOperationStatus.AlreadyExists => AuthProblems.Problem(StatusCodes.Status409Conflict, "The initial setup is not available anymore.", AuthErrorCodes.SetupNotAvailable),
            AccountOperationStatus.LockedOut => AuthProblems.LockedOut(result.LockedUntil!.Value, httpContext, accountService.GetUtcNow()),
            _ => AuthProblems.StorageUnavailable()
        };
    }

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        IOptions<AuthOptions> options,
        AccountService accountService,
        AuthSessionService authSessionService,
        TwoFactorService twoFactorService,
        CancellationToken cancellationToken)
    {
        AccountOperationResult result = await accountService.ValidateCredentialsAsync(request.Username, request.Password, cancellationToken);
        if (result.Status == AccountOperationStatus.Success)
        {
            AccountRecord account = result.Account!;
            if (account.TwoFactorEnabled)
            {
                IssuedChallenge challenge = await twoFactorService.CreateLoginChallengeAsync(account, cancellationToken);
                AppendChallengeCookie(httpContext, challenge.Token, challenge.ExpiresAt, options.Value);
                return TypedResults.Ok(new LoginResponse { RequiresTwoFactor = true });
            }

            await IssueAndSignInAsync(httpContext, account, authSessionService, request.Remember, cancellationToken);
            return TypedResults.Ok(new LoginResponse { RequiresTwoFactor = false });
        }

        return result.Status == AccountOperationStatus.LockedOut
            ? AuthProblems.LockedOut(result.LockedUntil!.Value, httpContext, accountService.GetUtcNow())
            : AuthProblems.InvalidCredentials(result.RemainingAttempts);
    }

    private static async Task<Ok<TwoFactorStateResponse>> TwoFactorStateAsync(
        HttpContext httpContext,
        AccountService accountService,
        TwoFactorService twoFactorService,
        CancellationToken cancellationToken)
    {
        TwoFactorChallengeRecord? challenge = await twoFactorService.FindChallengeAsync(
            httpContext.Request.Cookies[ChallengeCookieName],
            cancellationToken);
        if (challenge is null || !challenge.IsUsable(accountService.GetUtcNow()))
        {
            return TypedResults.Ok(new TwoFactorStateResponse { Pending = false });
        }

        return TypedResults.Ok(new TwoFactorStateResponse { Pending = true, ExpiresAt = challenge.ExpiresAt });
    }

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> VerifyTwoFactorAsync(
        TwoFactorVerifyRequest request,
        HttpContext httpContext,
        IOptions<AuthOptions> options,
        AccountService accountService,
        AuthSessionService authSessionService,
        TwoFactorService twoFactorService,
        CancellationToken cancellationToken)
    {
        TwoFactorChallengeRecord? challenge = await twoFactorService.FindChallengeAsync(
            httpContext.Request.Cookies[ChallengeCookieName],
            cancellationToken);
        AccountRecord? account = await accountService.GetAccountAsync(cancellationToken);
        if (challenge is null || !challenge.IsUsable(accountService.GetUtcNow())
            || account is null || account.Id != challenge.AccountId
            || !account.TwoFactorEnabled || account.SecurityVersion != challenge.SecurityVersion)
        {
            ClearChallengeCookie(httpContext, options.Value);
            return AuthProblems.Problem(
                StatusCodes.Status410Gone,
                "The two-factor challenge is invalid, expired or already used. Please sign in again.",
                AuthErrorCodes.TwoFactorChallengeInvalid);
        }

        if (account.IsLockedOut(accountService.GetUtcNow()))
        {
            return AuthProblems.LockedOut(account.LockoutUntil!.Value, httpContext, accountService.GetUtcNow());
        }

        SecondFactorProbe proof = await twoFactorService.ProbeSecondFactorAsync(account, request.Code, request.UseRecoveryCode, cancellationToken);
        if (proof.Status != SecondFactorProofStatus.Success)
        {
            // The recorder answers with the state the counter really holds afterwards, so the
            // request reaching the limit is answered by the lock instead of another wrong code.
            AccountOperationResult failure = await twoFactorService.RecordChallengeFailureAsync(challenge, cancellationToken);
            if (failure.Status == AccountOperationStatus.LockedOut)
            {
                return AuthProblems.LockedOut(failure.LockedUntil!.Value, httpContext, accountService.GetUtcNow());
            }

            return AuthProblems.ProofFailure(
                StatusCodes.Status400BadRequest,
                "The two-factor code is incorrect.",
                AuthErrorCodes.InvalidTwoFactorCode,
                AuthFailureKinds.SecondFactor,
                failure.RemainingAttempts);
        }

        if (!await twoFactorService.CompleteLoginAsync(account, challenge, proof, cancellationToken))
        {
            ClearChallengeCookie(httpContext, options.Value);
            return AuthProblems.Problem(
                StatusCodes.Status410Gone,
                "The two-factor challenge is invalid, expired or already used. Please sign in again.",
                AuthErrorCodes.TwoFactorChallengeInvalid);
        }

        ClearChallengeCookie(httpContext, options.Value);
        await IssueAndSignInAsync(httpContext, account, authSessionService, request.Remember, cancellationToken);
        return TypedResults.Ok(new LoginResponse { RequiresTwoFactor = false });
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        AuthSessionService authSessionService,
        CancellationToken cancellationToken)
    {
        await authSessionService.RevokeCurrentSessionAsync(user, cancellationToken);
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }

    private static async Task<StatusCodeHttpResult> SignInWithNewSessionAsync(
        HttpContext httpContext,
        AccountRecord account,
        int statusCode,
        AuthSessionService authSessionService,
        CancellationToken cancellationToken)
    {
        // The initial setup signs the new account owner in for the full session lifetime.
        await IssueAndSignInAsync(httpContext, account, authSessionService, remember: true, cancellationToken);
        return TypedResults.StatusCode(statusCode);
    }

    private static async Task IssueAndSignInAsync(
        HttpContext httpContext,
        AccountRecord account,
        AuthSessionService authSessionService,
        bool remember,
        CancellationToken cancellationToken)
    {
        IssuedSession session = await authSessionService.IssueSessionAsync(account, cancellationToken);
        // IsPersistent only decides whether the browser keeps the cookie across restarts;
        // ExpiresUtc still caps the ticket at the absolute session lifetime either way.
        AuthenticationProperties properties = new() { IsPersistent = remember, ExpiresUtc = session.ExpiresAt };
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, session.Principal, properties);
    }

    // The challenge cookie only ever rides along with the /api/auth two-factor calls, is
    // HttpOnly and dies with the challenge itself; it is never an application credential.
    private static void AppendChallengeCookie(HttpContext httpContext, string token, DateTimeOffset expiresAt, AuthOptions options)
    {
        httpContext.Response.Cookies.Append(
            ChallengeCookieName,
            token,
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Path = ChallengeCookiePath,
                Expires = expiresAt.UtcDateTime,
                Secure = options.RequireSecureCookies || httpContext.Request.IsHttps
            });
    }

    private static void ClearChallengeCookie(HttpContext httpContext, AuthOptions options)
    {
        httpContext.Response.Cookies.Delete(
            ChallengeCookieName,
            new CookieOptions
            {
                Path = ChallengeCookiePath,
                Secure = options.RequireSecureCookies || httpContext.Request.IsHttps
            });
    }
}
