using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Endpoints;

// Live request browsing against the single configured Seerr instance; no local state is kept.
public static class RequestEndpoints
{
    // ProblemDetails extension code the frontend uses to distinguish "not configured" from other failures.
    public const string NotConfiguredProblemCode = "seerr-not-configured";

    // Seerr has no server-side page parameter, so each request fetches the first page * limit
    // entries; deeper windows than this would make the upstream query unreasonably large.
    private const int MaxRequestWindow = 1000;

    // Mirrors the values Seerr accepts for its "filter" query parameter; "declined" is not one of them.
    private static readonly IReadOnlySet<string> StatusFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "approved",
        "processing",
        "available",
        "failed",
    };

    private static readonly IReadOnlySet<string> SortFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "createdAt",
        "updatedAt",
    };

    private static readonly IReadOnlySet<string> SortOrders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "asc",
        "desc",
    };

    public static IEndpointRouteBuilder MapRequestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/requests");
        group.RequireAuthorization();

        group.MapGet("/", GetRequestsAsync);
        group.MapGet("{type}/poster/{tmdbId:int}", GetPosterAsync);

        return endpoints;
    }

    private static async Task<Results<Ok<SeerrRequestsResponse>, ValidationProblem, ProblemHttpResult>> GetRequestsAsync(
        int page = 1,
        int limit = 20,
        string? status = null,
        string? sortField = null,
        string? sortOrder = null,
        ISettingsService settingsService = null!,
        SeerrClient seerrClient = null!,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, string[]> validationErrors = ValidateQuery(page, limit, status, sortField, sortOrder);
        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        if (settings.Seerr is not SeerrInstanceSettings seerrInstance)
        {
            ProblemDetails notConfigured = new()
            {
                Title = "Seerr is not configured.",
                Detail = "Configure Seerr on the settings page to browse requests.",
                Status = StatusCodes.Status503ServiceUnavailable
            };
            notConfigured.Extensions["code"] = NotConfiguredProblemCode;
            return TypedResults.Problem(notConfigured);
        }

        try
        {
            SeerrRequestsResponse requests = await seerrClient.GetRequestsAsync(
                seerrInstance,
                page,
                limit,
                sortField ?? "createdAt",
                sortOrder ?? "desc",
                string.IsNullOrWhiteSpace(status) ? null : status.ToLowerInvariant(),
                cancellationToken);

            return TypedResults.Ok(requests);
        }
        catch (HttpRequestException)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Seerr is not reachable.",
                Detail = "The configured Seerr instance did not answer the requests query.",
                Status = StatusCodes.Status502BadGateway
            });
        }
    }

    // Posters come from the TMDB image host referenced by the media metadata; they are served
    // through the shared disk cache, so a cached poster is never downloaded again.
    private static async Task<IResult> GetPosterAsync(
        string type,
        int tmdbId,
        [FromQuery] int width = 342,
        HttpResponse httpResponse = default!,
        ISettingsService settingsService = default!,
        IPosterCacheService posterCache = default!,
        SeerrClient seerrClient = default!,
        CancellationToken cancellationToken = default)
    {
        string mediaType = type.Trim().ToLowerInvariant();
        if (mediaType is not ("movie" or "tv"))
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Unknown media type.",
                Detail = $"No posters are available for media type '{type}'.",
                Status = StatusCodes.Status404NotFound
            });
        }

        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        if (settings.Seerr is not SeerrInstanceSettings seerrInstance)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Seerr is not configured.",
                Detail = "Configure Seerr on the settings page to browse requests.",
                Status = StatusCodes.Status404NotFound
            });
        }

        string? remoteUrl = null;
        try
        {
            SeerrMediaMetadataResource? metadata = await seerrClient.GetMediaMetadataAsync(
                seerrInstance,
                mediaType,
                tmdbId,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(metadata?.PosterPath))
            {
                // Seerr sends TMDB-relative poster paths; the cache step rewrites the size segment.
                remoteUrl = $"https://image.tmdb.org/t/p/w500{metadata.PosterPath}";
            }
        }
        catch (HttpRequestException)
        {
            // Seerr answers unknown media ids with an upstream error; those stay poster-less below.
        }

        PosterImage? poster = await posterCache.GetOrDownloadPosterAsync(
            $"seerr_{mediaType}_{tmdbId}", remoteUrl, width, cancellationToken);

        if (poster is null)
        {
            httpResponse.StatusCode = StatusCodes.Status404NotFound;
            httpResponse.ContentType = "application/json";
            await httpResponse.WriteAsync("{\"error\":\"Poster not found\"}", cancellationToken);
            return Results.Empty;
        }

        httpResponse.ContentType = poster.ContentType;
        httpResponse.Headers.CacheControl = "no-store";
        await httpResponse.Body.WriteAsync(poster.Bytes, 0, poster.Bytes.Length, cancellationToken);
        return Results.Empty;
    }

    private static Dictionary<string, string[]> ValidateQuery(int page, int limit, string? status, string? sortField, string? sortOrder)
    {
        Dictionary<string, string[]> errors = new(StringComparer.OrdinalIgnoreCase);
        if (page < 1)
        {
            errors["page"] = ["The page must be greater than zero."];
        }
        else if (limit >= 1 && (long)page * limit > MaxRequestWindow)
        {
            errors["page"] = [$"The requested page window must not exceed {MaxRequestWindow} entries."];
        }

        if (limit is < 1 or > 100)
        {
            errors["limit"] = ["The limit must be between 1 and 100."];
        }

        if (!string.IsNullOrWhiteSpace(status) && !StatusFilters.Contains(status))
        {
            errors["status"] = ["The status filter must be one of: pending, approved, processing, available, failed."];
        }

        if (!string.IsNullOrWhiteSpace(sortField) && !SortFields.Contains(sortField))
        {
            errors["sortField"] = ["The sort field must be one of: createdAt, updatedAt."];
        }

        if (!string.IsNullOrWhiteSpace(sortOrder) && !SortOrders.Contains(sortOrder))
        {
            errors["sortOrder"] = ["The sort order must be asc or desc."];
        }

        return errors;
    }
}
