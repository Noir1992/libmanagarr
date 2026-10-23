using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using LibManagarr.Api.Models.Auth;

namespace LibManagarr.Api.Services;

/// <summary>
/// Validates the antiforgery token of every mutating /api request before the endpoint runs,
/// including the anonymous Setup/Login/Logout/Password calls. A missing or invalid token
/// answers with a plain 400 problem and never reaches the handler.
/// </summary>
public sealed class ApiAntiforgeryMiddleware(RequestDelegate next, IAntiforgery antiforgery)
{
    private static readonly HashSet<string> UnsafeHttpMethods = new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    public async Task InvokeAsync(HttpContext context)
    {
        if (UnsafeHttpMethods.Contains(context.Request.Method) &&
            context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            bool isValid = await antiforgery.IsRequestValidAsync(context);
            if (!isValid)
            {
                ProblemDetails problem = new() { Title = "The request verification token is missing or invalid.", Status = StatusCodes.Status400BadRequest };
                problem.Extensions["code"] = AuthErrorCodes.InvalidCsrfToken;
                await TypedResults.Problem(problem).ExecuteAsync(context);
                return;
            }
        }

        await next(context);
    }
}
