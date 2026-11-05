using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using LibManagarr.Api.Models.Account;
using LibManagarr.Api.Models.Auth;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Endpoints;

/// <summary>
/// Authenticated account view, password change and the full 2FA lifecycle. A wrong current
/// password is always answered as a business failure (400 with its own code), never as a 401
/// that would trip the global sign-out handler of the UI. Every proof-consuming flow verifies
/// the password first, so a wrong password can never burn a recovery code. Enabling or
/// disabling 2FA revokes all sessions atomically and hands only this browser a fresh one.
/// </summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/account");
        group.RequireAuthorization();
        group.AddEndpointFilter(static async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
            {
                AccountService service = context.HttpContext.RequestServices.GetRequiredService<AccountService>();
                AccountRecord? account = await service.GetAccountAsync(context.HttpContext.RequestAborted);
                if (account is not null && account.IsLockedOut(service.GetUtcNow()))
                    return AuthProblems.LockedOut(account.LockoutUntil!.Value, context.HttpContext, service.GetUtcNow());
            }
            return await next(context);
        });

        group.MapGet("/", GetAccountAsync);
        group.MapPost("/password", ChangePasswordAsync).RequireRateLimiting("auth");
        group.MapPost("/two-factor/setup", StartTwoFactorSetupAsync).RequireRateLimiting("auth");
        group.MapPost("/two-factor/setup/confirm", ConfirmTwoFactorSetupAsync).RequireRateLimiting("auth");
        group.MapDelete("/two-factor/setup", CancelTwoFactorSetupAsync);
        group.MapPost("/two-factor/disable", DisableTwoFactorAsync).RequireRateLimiting("auth");
        group.MapPost("/two-factor/recovery-codes", RotateRecoveryCodesAsync).RequireRateLimiting("auth");

        return endpoints;
    }

    private static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> GetAccountAsync(
        ClaimsPrincipal user,
        AccountService accountService,
        TwoFactorService twoFactorService,
        CancellationToken cancellationToken)
    {
        if (!TryGetAccountId(user, out int accountId))
        {
            return AuthProblems.NoAccount();
        }

        AccountRecord? account = await accountService.GetAccountAsync(cancellationToken);
        if (account is null || account.Id != accountId)
        {
            return AuthProblems.NoAccount();
        }

        int remaining = account.TwoFactorEnabled
            ? await twoFactorService.GetRecoveryCodesRemainingAsync(account.Id, cancellationToken)
            : 0;

        return TypedResults.Ok(new AccountResponse
        {
            Username = account.Username,
            TwoFactorEnabled = account.TwoFactorEnabled,
            RecoveryCodesRemaining = remaining
        });
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ChangePasswordAsync(
        ChangePasswordRequest request, HttpContext httpContext, ClaimsPrincipal user,
        AccountService accountService, CancellationToken cancellationToken)
    {
        if (!TryGetSession(user, out int accountId, out string sessionToken)) return AuthProblems.NoAccount();
        AccountRecord? account = await accountService.GetAccountAsync(cancellationToken);
        if (account is null || account.Id != accountId) return AuthProblems.NoAccount();
        AccountOperationResult result = await accountService.ChangePasswordAsync(account, sessionToken,
            request.CurrentPassword, request.NewPassword, request.ConfirmPassword, request.Code, request.UseRecoveryCode, cancellationToken);
        if (result.Status == AccountOperationStatus.Success)
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return TypedResults.NoContent();
        }
        return result.Status switch
        {
            AccountOperationStatus.ValidationFailed => AuthProblems.Validation(result.Errors!),
            AccountOperationStatus.InvalidCredentials => CurrentPasswordIncorrectProblem(result.RemainingAttempts),
            AccountOperationStatus.SecondFactorRequired => SecondFactorRequiredProblem(result.RemainingAttempts),
            AccountOperationStatus.InvalidSecondFactor => InvalidTwoFactorCodeProblem(result.RemainingAttempts),
            AccountOperationStatus.LockedOut => AuthProblems.LockedOut(result.LockedUntil!.Value, httpContext, accountService.GetUtcNow()),
            AccountOperationStatus.StateChanged => SecurityStateChangedProblem(),
            _ => AuthProblems.NoAccount()
        };
    }

    private static async Task<Results<Ok<TwoFactorSetupResponse>, ProblemHttpResult>> StartTwoFactorSetupAsync(
        ClaimsPrincipal user,
        AccountService accountService,
        TwoFactorService twoFactorService,
        CancellationToken cancellationToken)
    {
        if (!TryGetSession(user, out int accountId, out string sessionToken))
        {
            return AuthProblems.NoAccount();
        }

        AccountRecord? account = await accountService.GetAccountAsync(cancellationToken);
        if (account is null || account.Id != accountId)
        {
            return AuthProblems.NoAccount();
        }

        if (account.TwoFactorEnabled)
        {
            return TwoFactorAlreadyEnabledProblem();
        }

        byte[] secret = TwoFactorService.GenerateSecret();
        TwoFactorSetupRecord setup = await twoFactorService.CreateSetupAsync(account, sessionToken, secret, cancellationToken);
        string authenticatorUri = TwoFactorService.BuildAuthenticatorUri(account.Username, secret);
        return TypedResults.Ok(new TwoFactorSetupResponse
        {
            SharedKey = TwoFactorService.EncodeSharedKey(secret),
            AuthenticatorUri = authenticatorUri,
            QrCodeSvg = TwoFactorService.BuildQrCodeSvg(authenticatorUri),
            ExpiresAt = setup.ExpiresAt
        });
    }

    private static async Task<Results<Ok<RecoveryCodesResponse>, ProblemHttpResult>> ConfirmTwoFactorSetupAsync(
        TwoFactorConfirmRequest request,
        HttpContext httpContext,
        ClaimsPrincipal user,
        AccountService accountService,
        AuthSessionService authSessionService,
        TwoFactorService twoFactorService,
        CancellationToken cancellationToken)
    {
        if (!TryGetSession(user, out int accountId, out string sessionToken))
        {
            return AuthProblems.NoAccount();
        }

        AccountRecord? account = await accountService.GetAccountAsync(cancellationToken);
        if (account is null || account.Id != accountId)
        {
            return AuthProblems.NoAccount();
        }

        if (account.TwoFactorEnabled)
        {
            return TwoFactorAlreadyEnabledProblem();
        }

        TwoFactorSetupRecord? setup = await twoFactorService.GetActiveSetupForSessionAsync(sessionToken, cancellationToken);
        if (setup is null || setup.IsExpired(accountService.GetUtcNow()))
        {
            return TwoFactorSetupInvalidProblem();
        }

        if (!twoFactorService.TryVerifySetupCode(setup, request.Code, out long firstStep))
        {
            AccountOperationResult failure = await twoFactorService.RecordSetupFailureAsync(setup, cancellationToken);
            if (failure.Status == AccountOperationStatus.LockedOut)
            {
                return AuthProblems.LockedOut(failure.LockedUntil!.Value, httpContext, accountService.GetUtcNow());
            }

            return InvalidTwoFactorCodeProblem(failure.RemainingAttempts);
        }

        IReadOnlyList<string>? recoveryCodes = await twoFactorService.EnableFromSetupAsync(account, setup, firstStep, cancellationToken);
        if (recoveryCodes is null) return TwoFactorSetupInvalidProblem();

        AccountRecord? updated = await accountService.GetAccountAsync(cancellationToken);
        if (updated is null || updated.SecurityVersion != account.SecurityVersion + 1)
        {
            return SecurityStateChangedProblem();
        }

        await IssueAndSignInAsync(httpContext, updated, authSessionService, cancellationToken);
        return TypedResults.Ok(new RecoveryCodesResponse { RecoveryCodes = recoveryCodes });
    }

    private static async Task<NoContent> CancelTwoFactorSetupAsync(
        ClaimsPrincipal user,
        TwoFactorService twoFactorService,
        CancellationToken cancellationToken)
    {
        string sessionToken = user.FindFirstValue(AuthSessionService.SessionTokenClaimType) ?? string.Empty;
        if (sessionToken.Length > 0)
        {
            await twoFactorService.CancelSetupForSessionAsync(sessionToken, cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DisableTwoFactorAsync(
        TwoFactorProofRequest request,
        HttpContext httpContext,
        ClaimsPrincipal user,
        AccountService accountService,
        AuthSessionService authSessionService,
        TwoFactorService twoFactorService,
        CancellationToken cancellationToken)
    {
        if (!TryGetSession(user, out int accountId, out string sessionToken))
        {
            return AuthProblems.NoAccount();
        }

        AccountRecord? account = await accountService.GetAccountAsync(cancellationToken);
        if (account is null || account.Id != accountId)
        {
            return AuthProblems.NoAccount();
        }

        if (!account.TwoFactorEnabled)
        {
            return TwoFactorNotEnabledProblem();
        }

        (SecondFactorProbe? proof, ProblemHttpResult? proofFailure) = await ProveIdentityAsync(
            account,
            request.CurrentPassword,
            request.Code,
            request.UseRecoveryCode,
            accountService,
            httpContext,
            cancellationToken);
        if (proofFailure is not null)
        {
            return proofFailure;
        }

        if (!await twoFactorService.DisableAndRevokeAsync(account, sessionToken, proof!, cancellationToken))
        {
            return SecurityStateChangedProblem();
        }

        AccountRecord? updated = await accountService.GetAccountAsync(cancellationToken);
        if (updated is null || updated.SecurityVersion != account.SecurityVersion + 1)
        {
            return SecurityStateChangedProblem();
        }

        await IssueAndSignInAsync(httpContext, updated, authSessionService, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<RecoveryCodesResponse>, ProblemHttpResult>> RotateRecoveryCodesAsync(
        TwoFactorProofRequest request,
        HttpContext httpContext,
        ClaimsPrincipal user,
        AccountService accountService,
        TwoFactorService twoFactorService,
        CancellationToken cancellationToken)
    {
        if (!TryGetSession(user, out int accountId, out string sessionToken))
        {
            return AuthProblems.NoAccount();
        }

        AccountRecord? account = await accountService.GetAccountAsync(cancellationToken);
        if (account is null || account.Id != accountId)
        {
            return AuthProblems.NoAccount();
        }

        if (!account.TwoFactorEnabled)
        {
            return TwoFactorNotEnabledProblem();
        }

        (SecondFactorProbe? proof, ProblemHttpResult? proofFailure) = await ProveIdentityAsync(
            account,
            request.CurrentPassword,
            request.Code,
            request.UseRecoveryCode,
            accountService,
            httpContext,
            cancellationToken);
        if (proofFailure is not null)
        {
            return proofFailure;
        }

        IReadOnlyList<string>? recoveryCodes = await twoFactorService.RotateRecoveryCodesAsync(account, sessionToken, proof!, cancellationToken);
        if (recoveryCodes is null) return SecurityStateChangedProblem();
        return TypedResults.Ok(new RecoveryCodesResponse { RecoveryCodes = recoveryCodes });
    }

    /// <summary>
    /// Translates the shared identity proof into the problems of this area: a wrong password and a
    /// wrong code keep their own codes, and the request that exhausted the attempts answers the
    /// lockout itself.
    /// </summary>
    private static async Task<(SecondFactorProbe? Proof, ProblemHttpResult? Failure)> ProveIdentityAsync(
        AccountRecord account, string? currentPassword, string? code, bool useRecoveryCode,
        AccountService accountService, HttpContext httpContext, CancellationToken cancellationToken)
    {
        (SecondFactorProbe? proof, AccountOperationResult result) = await accountService.VerifyIdentityProofAsync(
            account, currentPassword, code, useRecoveryCode, cancellationToken);
        if (result.Status == AccountOperationStatus.Success) return (proof, null);
        return result.Status switch
        {
            AccountOperationStatus.LockedOut => (null, AuthProblems.LockedOut(result.LockedUntil!.Value, httpContext, accountService.GetUtcNow())),
            AccountOperationStatus.SecondFactorRequired => (null, SecondFactorRequiredProblem(result.RemainingAttempts)),
            AccountOperationStatus.InvalidSecondFactor => (null, InvalidTwoFactorCodeProblem(result.RemainingAttempts)),
            _ => (null, CurrentPasswordIncorrectProblem(result.RemainingAttempts))
        };
    }

    private static ProblemHttpResult SecondFactorRequiredProblem(int? remainingAttempts) => AuthProblems.ProofFailure(
        StatusCodes.Status400BadRequest,
        "A current two-factor code or recovery code is required.",
        AuthErrorCodes.SecondFactorRequired,
        AuthFailureKinds.SecondFactor,
        remainingAttempts);

    private static ProblemHttpResult SecurityStateChangedProblem() => AuthProblems.Problem(
        StatusCodes.Status409Conflict, "The account state changed. Please sign in again and retry.", AuthErrorCodes.SecurityStateChanged);

    private static async Task IssueAndSignInAsync(
        HttpContext httpContext,
        AccountRecord account,
        AuthSessionService authSessionService,
        CancellationToken cancellationToken)
    {
        IssuedSession session = await authSessionService.IssueSessionAsync(account, cancellationToken);
        AuthenticationProperties properties = new() { IsPersistent = true, ExpiresUtc = session.ExpiresAt };
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, session.Principal, properties);
    }

    private static ProblemHttpResult CurrentPasswordIncorrectProblem(int? remainingAttempts) => AuthProblems.ProofFailure(
        StatusCodes.Status400BadRequest,
        "The current password is incorrect.",
        AuthErrorCodes.CurrentPasswordIncorrect,
        AuthFailureKinds.Password,
        remainingAttempts);

    private static ProblemHttpResult InvalidTwoFactorCodeProblem(int? remainingAttempts) => AuthProblems.ProofFailure(
        StatusCodes.Status400BadRequest,
        "The two-factor code is incorrect or already used.",
        AuthErrorCodes.InvalidTwoFactorCode,
        AuthFailureKinds.SecondFactor,
        remainingAttempts);

    private static ProblemHttpResult TwoFactorAlreadyEnabledProblem() => AuthProblems.Problem(
        StatusCodes.Status409Conflict,
        "Two-factor authentication is already enabled for this account.",
        AuthErrorCodes.TwoFactorAlreadyEnabled);

    private static ProblemHttpResult TwoFactorNotEnabledProblem() => AuthProblems.Problem(
        StatusCodes.Status400BadRequest,
        "Two-factor authentication is not enabled for this account.",
        AuthErrorCodes.TwoFactorNotEnabled);

    private static ProblemHttpResult TwoFactorSetupInvalidProblem() => AuthProblems.Problem(
        StatusCodes.Status410Gone,
        "The two-factor setup is expired, revoked or was started from a different session.",
        AuthErrorCodes.TwoFactorSetupInvalid);

    private static bool TryGetAccountId(ClaimsPrincipal user, out int accountId) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out accountId);

    private static bool TryGetSession(ClaimsPrincipal user, out int accountId, out string sessionToken)
    {
        sessionToken = user.FindFirstValue(AuthSessionService.SessionTokenClaimType) ?? string.Empty;
        return TryGetAccountId(user, out accountId) && sessionToken.Length > 0;
    }
}
