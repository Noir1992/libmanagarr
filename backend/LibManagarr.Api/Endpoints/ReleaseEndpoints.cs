using Microsoft.AspNetCore.Http.HttpResults;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Endpoints;

// Published releases of the project repository plus the version this instance runs on. This read
// only answers from the stored snapshot; the releaseCheck scheduled task is the single caller
// that asks GitHub, on its twelve-hour rhythm and once at every start. An unreachable upstream
// answers with an empty overview instead of an error status, so the navigation entry stays fast
// and cheap in every network situation.
public static class ReleaseEndpoints
{
    public static IEndpointRouteBuilder MapReleaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/releases");
        group.RequireAuthorization();
        group.MapGet("/", GetOverviewAsync);
        return endpoints;
    }

    private static async Task<Ok<ReleaseOverview>> GetOverviewAsync(
        ReleaseOverviewService releaseOverviewService,
        CancellationToken cancellationToken)
    {
        return TypedResults.Ok(await releaseOverviewService.GetOverviewAsync(cancellationToken));
    }
}
