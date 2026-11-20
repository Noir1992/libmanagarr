using LibManagarr.Api.Services;

namespace LibManagarr.Api.Endpoints;

public static class MediaTitleEndpoints
{
    public static IEndpointRouteBuilder MapMediaTitleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/media-titles/refresh", async (MediaTitleRefreshService titles, ISettingsService settings, CancellationToken token) =>
        {
            await titles.RefreshAsync(token);
            var snapshot = await settings.GetSnapshotAsync(token);
            string revision = snapshot.RadarrSnapshots.SelectMany(value => value.Movies)
                .SelectMany(value => value.Titles.Translations.Values).Select(value => value.CheckedAt)
                .DefaultIfEmpty().Max().ToString("O");
            return Results.Ok(new { Revision = revision });
        }).RequireAuthorization();
        return endpoints;
    }
}
