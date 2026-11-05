using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using LibManagarr.Api.Models.Auth;

namespace LibManagarr.Api.Endpoints;

/// <summary>
/// ProblemDetails factories shared by the auth, account and backup endpoints. Every failure carries a
/// stable machine-readable code extension so the UI can translate without parsing text. Failures that
/// describe a proof carry the state behind them as typed extensions too: which credential was wrong,
/// how many attempts are left and, when an action is blocked, until when. A waiting answer writes its
/// header and its body here together, so the two can never name two different times.
/// </summary>
internal static class AuthProblems
{
    // Naming the attempts left is a warning, not a running counter: it starts with the last two
    // attempts before a lock. Earlier answers would only tell a guessing caller how far it still is.
    private const int WarnFromRemainingAttempts = 2;

    internal static ProblemHttpResult Problem(int statusCode, string title, string code)
    {
        ProblemDetails details = new() { Title = title, Status = statusCode };
        details.Extensions["code"] = code;
        return TypedResults.Problem(details);
    }

    internal static ProblemHttpResult Validation(IReadOnlyList<AccountFieldError> errors)
    {
        ValidationProblemDetails details = new(errors
            .GroupBy((error) => error.Field)
            .ToDictionary((group) => group.Key, (group) => group.Select((error) => error.Code).ToArray()))
        {
            Title = "Validation failed.",
            Status = StatusCodes.Status400BadRequest
        };
        details.Extensions["code"] = "validation_failed";
        return TypedResults.Problem(details);
    }

    /// <summary>
    /// The shared shape of a failed identity proof: the code of the failing action, the credential
    /// that was wrong and the attempts the account really has left. An unknown username reaches it
    /// without a count, which is how it stays indistinguishable from a wrong password.
    /// </summary>
    internal static ProblemHttpResult ProofFailure(
        int statusCode, string title, string code, string failureKind, int? remainingAttempts)
    {
        ProblemDetails details = new() { Title = title, Status = statusCode };
        details.Extensions["code"] = code;
        details.Extensions["failureKind"] = failureKind;
        if (remainingAttempts is { } remaining && remaining <= WarnFromRemainingAttempts)
        {
            details.Extensions["remainingAttempts"] = remaining;
        }

        return TypedResults.Problem(details);
    }

    internal static ProblemHttpResult StorageUnavailable() =>
        Problem(StatusCodes.Status503ServiceUnavailable, "The account storage is unavailable.", AuthErrorCodes.AuthStorageUnavailable);

    internal static ProblemHttpResult InvalidCredentials(int? remainingAttempts = null) =>
        ProofFailure(
            StatusCodes.Status401Unauthorized,
            "The username or password is incorrect.",
            AuthErrorCodes.InvalidCredentials,
            AuthFailureKinds.Password,
            remainingAttempts);

    internal static ProblemHttpResult NoAccount() =>
        Problem(StatusCodes.Status401Unauthorized, "No account is attached to this session.", AuthErrorCodes.NoAccount);

    /// <summary>
    /// Answers a locked account. The remaining time is read from the same clock that decided the
    /// lockout, so the header and the stored end can never drift apart. While the account is locked
    /// every attempt is refused, whatever the caller types, so the answer says none are left.
    /// </summary>
    internal static ProblemHttpResult LockedOut(DateTimeOffset lockedUntil, HttpContext httpContext, DateTimeOffset now)
    {
        ProblemDetails details = WaitingTime(
            StatusCodes.Status429TooManyRequests,
            "The account is temporarily locked after repeated failed attempts.",
            AuthErrorCodes.AccountLocked,
            lockedUntil,
            httpContext,
            now);
        details.Extensions["lockedUntil"] = lockedUntil;
        details.Extensions["remainingAttempts"] = 0;
        return TypedResults.Problem(details);
    }

    /// <summary>
    /// Answers the shared per-address request limit with the time its window really has left. It says
    /// nothing about the credentials of the request, because no proof was examined to reach this answer.
    /// </summary>
    internal static ProblemHttpResult TooManyRequests(TimeSpan remaining, HttpContext httpContext, DateTimeOffset now) =>
        TypedResults.Problem(WaitingTime(
            StatusCodes.Status429TooManyRequests,
            "Too many requests, please try again later.",
            AuthErrorCodes.TooManyRequests,
            now + remaining,
            httpContext,
            now));

    // One place writes the Retry-After header and the extensions describing the same wait, so an
    // answer cannot promise a time its own header denies.
    private static ProblemDetails WaitingTime(
        int statusCode, string title, string code, DateTimeOffset retryAt, HttpContext httpContext, DateTimeOffset now)
    {
        long retryAfterSeconds = Math.Max(1L, (long)Math.Ceiling((retryAt - now).TotalSeconds));
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        ProblemDetails details = new() { Title = title, Status = statusCode };
        details.Extensions["code"] = code;
        details.Extensions["retryAt"] = retryAt;
        details.Extensions["retryAfterSeconds"] = retryAfterSeconds;
        details.Extensions["serverTime"] = now;
        return details;
    }
}
