using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using LibManagarr.Api.Models.Auth;

namespace LibManagarr.Api.Services;

/// <summary>
/// Cookie authentication events. Every authenticated request re-checks the persisted session
/// (revocation, absolute expiry and the account security version), and API answers stay plain
/// problem JSON — the browser never receives a login redirect or HTML from a protected API.
/// </summary>
public sealed class AuthCookieEvents(AuthSessionService authSessionService) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        bool isValid = await authSessionService.IsSessionValidAsync(context.Principal, context.HttpContext.RequestAborted);
        if (!isValid)
        {
            context.RejectPrincipal();
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context) =>
        WriteProblemAsync(
            context.HttpContext,
            StatusCodes.Status401Unauthorized,
            "Authentication is required to access this resource.",
            AuthErrorCodes.AuthenticationRequired);

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context) =>
        WriteProblemAsync(
            context.HttpContext,
            StatusCodes.Status403Forbidden,
            "You are not allowed to access this resource.",
            AuthErrorCodes.AccessDenied);

    private static async Task WriteProblemAsync(HttpContext httpContext, int statusCode, string title, string code)
    {
        ProblemDetails problem = new() { Title = title, Status = statusCode };
        problem.Extensions["code"] = code;
        await TypedResults.Problem(problem).ExecuteAsync(httpContext);
    }
}
