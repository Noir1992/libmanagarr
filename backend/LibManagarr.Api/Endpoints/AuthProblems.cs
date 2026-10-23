using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using LibManagarr.Api.Models.Auth;

namespace LibManagarr.Api.Endpoints;

/// <summary>
/// ProblemDetails factories shared by the auth and account endpoints. Every failure carries a
/// stable machine-readable code extension so the UI can translate without parsing text.
/// </summary>
internal static class AuthProblems
{
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

    internal static ProblemHttpResult StorageUnavailable() =>
        Problem(StatusCodes.Status503ServiceUnavailable, "The account storage is unavailable.", AuthErrorCodes.AuthStorageUnavailable);

    internal static ProblemHttpResult InvalidCredentials() =>
        Problem(StatusCodes.Status401Unauthorized, "The username or password is incorrect.", AuthErrorCodes.InvalidCredentials);

    internal static ProblemHttpResult NoAccount() =>
        Problem(StatusCodes.Status401Unauthorized, "No account is attached to this session.", AuthErrorCodes.NoAccount);

    internal static ProblemHttpResult LockedOut(DateTimeOffset lockedUntil, HttpContext httpContext)
    {
        long retryAfterSeconds = Math.Max(1L, (long)Math.Ceiling((lockedUntil - DateTimeOffset.UtcNow).TotalSeconds));
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        return Problem(StatusCodes.Status429TooManyRequests, "The account is temporarily locked after repeated failed attempts.", AuthErrorCodes.AccountLocked);
    }
}
