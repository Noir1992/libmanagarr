using System.Net;
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
        group.MapGet("{id:int}/edit-options", GetEditOptionsAsync);
        group.MapPost("{id:int}/approve", ApproveRequestAsync);
        group.MapPost("{id:int}/decline", DeclineRequestAsync);
        group.MapPut("{id:int}", UpdateRequestAsync);
        group.MapDelete("{id:int}", DeleteRequestAsync);

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
            return NotConfiguredProblem();
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

    // The write routes mirror the Seerr contract proven against the live instance: approve and
    // decline are status-suffix POSTs, deletion uses the DELETE verb and edits are a PUT whose
    // mediaType is resolved from the request detail so the frontend never sends Seerr fields.
    private static async Task<IResult> ApproveRequestAsync(
        int id,
        ISettingsService settingsService = null!,
        SeerrClient seerrClient = null!,
        CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        if (settings.Seerr is not SeerrInstanceSettings seerrInstance)
        {
            return NotConfiguredProblem();
        }

        try
        {
            SeerrRequestResource? detail = await seerrClient.GetRequestDetailAsync(seerrInstance, id, cancellationToken);
            string? mediaType = MapDetailMediaType(detail);
            if (detail is null || mediaType is null)
            {
                return RequestNotFoundProblem();
            }

            if (mediaType == "movie")
            {
                // Direct approval needs the same current Radarr values as saving the edit dialog.
                // Old requests can still reference profiles and folders that no longer exist.
                var (options, serverId) = await ResolveEditOptionsAsync(
                    seerrInstance, seerrClient, detail, mediaType, cancellationToken);
                if (options.ProfileId is null || options.RootFolder is null || serverId is null)
                {
                    return TypedResults.Problem(new ProblemDetails
                    {
                        Title = "Radarr request cannot be approved.",
                        Detail = "No usable Radarr server, quality profile or root folder is configured in Seerr.",
                        Status = StatusCodes.Status409Conflict
                    });
                }

                await seerrClient.UpdateRequestAsync(seerrInstance, id, new SeerrRequestUpdateResource
                {
                    MediaType = mediaType,
                    ServerId = serverId,
                    ProfileId = options.ProfileId,
                    RootFolder = options.RootFolder,
                    LanguageProfileId = detail.LanguageProfileId,
                    Is4k = detail.Is4k ?? detail.Is4kRequest,
                }, cancellationToken);
            }

            await seerrClient.ApproveRequestAsync(seerrInstance, id, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (HttpRequestException exception)
        {
            return SeerrWriteProblem(exception, "approve");
        }
    }

    private static async Task<IResult> DeclineRequestAsync(
        int id,
        SeerrRequestDeclineBody? body = null,
        ISettingsService settingsService = null!,
        SeerrClient seerrClient = null!,
        CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        if (settings.Seerr is not SeerrInstanceSettings seerrInstance)
        {
            return NotConfiguredProblem();
        }

        try
        {
            await seerrClient.DeclineRequestAsync(seerrInstance, id, body?.Reason, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (HttpRequestException exception)
        {
            return SeerrWriteProblem(exception, "decline");
        }
    }

    private static async Task<IResult> DeleteRequestAsync(
        int id,
        ISettingsService settingsService = null!,
        SeerrClient seerrClient = null!,
        CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        if (settings.Seerr is not SeerrInstanceSettings seerrInstance)
        {
            return NotConfiguredProblem();
        }

        try
        {
            await seerrClient.DeleteRequestAsync(seerrInstance, id, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (HttpRequestException exception)
        {
            return SeerrWriteProblem(exception, "delete");
        }
    }

    private static async Task<IResult> UpdateRequestAsync(
        int id,
        SeerrRequestUpdateBody body,
        ISettingsService settingsService = null!,
        SeerrClient seerrClient = null!,
        CancellationToken cancellationToken = default)
    {
        if (body.ProfileId is null
            && string.IsNullOrWhiteSpace(body.RootFolder)
            && body.Seasons is null
            && body.Is4kRequest is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = ["At least one of profileId, rootFolder, seasons or is4kRequest must be provided."],
            });
        }

        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        if (settings.Seerr is not SeerrInstanceSettings seerrInstance)
        {
            return NotConfiguredProblem();
        }

        SeerrRequestResource? detail;
        try
        {
            detail = await seerrClient.GetRequestDetailAsync(seerrInstance, id, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return SeerrWriteProblem(exception, "request detail");
        }

        string? mediaType = MapDetailMediaType(detail);
        if (detail is null || mediaType is null)
        {
            return RequestNotFoundProblem();
        }

        try
        {
            // Unedited fields are echoed from the detail so the PUT never resets them.
            await seerrClient.UpdateRequestAsync(
                seerrInstance,
                id,
                new SeerrRequestUpdateResource
                {
                    MediaType = mediaType,
                    // The server id is not editable; echo the detail value so the PUT never resets it.
                    ServerId = detail.ServerId,
                    ProfileId = body.ProfileId ?? detail.ProfileId,
                    RootFolder = string.IsNullOrWhiteSpace(body.RootFolder) ? detail.RootFolder : body.RootFolder.Trim(),
                    Is4k = body.Is4kRequest,
                    LanguageProfileId = detail.LanguageProfileId,
                    Seasons = ResolveUpdateSeasons(body, detail),
                },
                cancellationToken);

            return TypedResults.NoContent();
        }
        catch (HttpRequestException exception)
        {
            return SeerrWriteProblem(exception, "update");
        }
    }

    // Edit choices are resolved per request: the service route of the request's arr server
    // supplies the profiles and the real root folder list for both media types, and tv edits
    // additionally offer the show's seasons from the metadata proxy. Pending requests carry no
    // serverId, profileId or rootFolder yet, so the preselection falls back to the server
    // defaults; anime media fall back to the server anime defaults like in Seerr's dialogs.
    // A value that the arr no longer offers never preselects, so the dialog always shows a
    // choice it can also save back.
    private static async Task<IResult> GetEditOptionsAsync(
        int id,
        ISettingsService settingsService = null!,
        SeerrClient seerrClient = null!,
        CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        if (settings.Seerr is not SeerrInstanceSettings seerrInstance)
        {
            return NotConfiguredProblem();
        }

        SeerrRequestResource? detail;
        try
        {
            detail = await seerrClient.GetRequestDetailAsync(seerrInstance, id, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return SeerrWriteProblem(exception, "request detail");
        }

        string? mediaType = MapDetailMediaType(detail);
        if (detail is null || mediaType is null)
        {
            return RequestNotFoundProblem();
        }

        try
        {
            var (options, _) = await ResolveEditOptionsAsync(
                seerrInstance, seerrClient, detail, mediaType, cancellationToken);
            return TypedResults.Ok(options);
        }
        catch (HttpRequestException)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Seerr is not reachable.",
                Detail = "The configured Seerr instance did not answer the edit options query.",
                Status = StatusCodes.Status502BadGateway
            });
        }
    }

    private static async Task<(SeerrRequestEditOptions Options, int? ServerId)> ResolveEditOptionsAsync(
        SeerrInstanceSettings seerrInstance,
        SeerrClient seerrClient,
        SeerrRequestResource detail,
        string mediaType,
        CancellationToken cancellationToken)
    {
        string application = mediaType == "tv" ? "sonarr" : "radarr";
        int? serverId = detail.ServerId;
        if (serverId is null)
        {
            IReadOnlyList<SeerrServerResource> servers = await seerrClient.GetServersAsync(
                seerrInstance,
                application,
                cancellationToken);

            serverId = (servers.FirstOrDefault(server => server.IsDefault == true)
                ?? servers.FirstOrDefault())?.Id;
        }

        SeerrServiceResource? service = serverId is int resolvedServerId
            ? await seerrClient.GetServiceInfoAsync(seerrInstance, application, resolvedServerId, cancellationToken)
            : null;

        List<SeerrEditOption> qualityProfiles = [.. (service?.Profiles ?? [])
            .Select(profile => profile?.Id is int profileIdOption
                ? new SeerrEditOption(profileIdOption, profile!.Name ?? $"Profile {profileIdOption}")
                : null)
            .OfType<SeerrEditOption>()];

        List<string> rootFolders = [.. (service?.RootFolders ?? [])
            .Select(folder => folder?.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!.Trim())];

        SeerrMediaMetadataResource? tvMetadata = null;
        if (mediaType == "tv" && int.TryParse(detail.Media?.TmdbId, out int tmdbId))
        {
            tvMetadata = await GetTvMetadataAsync(seerrInstance, seerrClient, tmdbId, cancellationToken);
        }

        bool isAnime = IsAnimeMedia(tvMetadata);
        SeerrServerResource? server = service?.Server;
        int? defaultProfileId = isAnime
            ? server?.ActiveAnimeProfileId ?? server?.ActiveProfileId
            : server?.ActiveProfileId;
        string? defaultRootFolder = isAnime
            ? FirstNonBlank(server?.ActiveAnimeDirectory, server?.ActiveDirectory)
            : server?.ActiveDirectory;

        return (new SeerrRequestEditOptions
        {
            QualityProfiles = qualityProfiles,
            RootFolders = rootFolders,
            Seasons = mediaType == "tv" ? MapSeasonOptions(detail, tvMetadata) : [],
            ProfileId = ResolveOfferedProfile(qualityProfiles, detail.ProfileId, defaultProfileId),
            RootFolder = ResolveOfferedFolder(rootFolders, detail.RootFolder, defaultRootFolder),
        }, serverId);
    }

    // The metadata proxy answers unknown media ids with an upstream error; the dialog then
    // runs without season list and without anime signal instead of failing the whole query.
    private static async Task<SeerrMediaMetadataResource?> GetTvMetadataAsync(
        SeerrInstanceSettings seerrInstance,
        SeerrClient seerrClient,
        int tmdbId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await seerrClient.GetMediaMetadataAsync(seerrInstance, "tv", tmdbId, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    // Seerr's own dialogs pick the server anime defaults for media carrying the exact TMDB
    // keyword "anime"; close variants like "anime inspired" do not qualify (probe 2026-10-30).
    private static bool IsAnimeMedia(SeerrMediaMetadataResource? metadata)
    {
        return metadata?.Keywords?.Any(
            keyword => string.Equals(keyword?.Name?.Trim(), "anime", StringComparison.OrdinalIgnoreCase)) == true;
    }

    // Distinct ascending season numbers; entries without a number are dropped.
    private static IReadOnlyList<int> MapSeasonNumbers(List<SeerrSeasonResource>? seasons)
    {
        return [.. (seasons ?? [])
            .Select(season => season?.SeasonNumber)
            .OfType<int>()
            .Distinct()
            .Order()];
    }

    // One table row per season of the show. The metadata proxy supplies every season together
    // with its episode count; where it stayed silent the media object's seasons take over, so a
    // show still gets its table when the metadata call fails. The state only reports what the
    // dialog cannot change: the media download status 5 is available and 2 to 4 are processing.
    // The seasons this request covers stay pending because the dialog edits that very request,
    // and everything else stays none so the row reads as not requested.
    private static IReadOnlyList<SeerrSeasonOption> MapSeasonOptions(
        SeerrRequestResource detail,
        SeerrMediaMetadataResource? tvMetadata)
    {
        Dictionary<int, int?> episodeCounts = [];
        foreach (SeerrSeasonResource? season in tvMetadata?.Seasons ?? [])
        {
            if (season?.SeasonNumber is int seasonNumber)
            {
                episodeCounts.TryAdd(seasonNumber, season.EpisodeCount);
            }
        }

        Dictionary<int, int> downloadStatuses = [];
        foreach (SeerrSeasonResource? season in detail.Media?.Seasons ?? [])
        {
            if (season?.SeasonNumber is int seasonNumber && season.Status is int status)
            {
                downloadStatuses[seasonNumber] = status;
                episodeCounts.TryAdd(seasonNumber, null);
            }
        }

        foreach (int ownedSeason in MapSeasonNumbers(detail.Seasons))
        {
            episodeCounts.TryAdd(ownedSeason, null);
        }

        HashSet<int> ownedSeasons = [.. MapSeasonNumbers(detail.Seasons)];

        return [.. episodeCounts.Keys
            .Order()
            .Select(number => new SeerrSeasonOption(
                number,
                episodeCounts[number],
                MapSeasonState(number, ownedSeasons.Contains(number), downloadStatuses)))];
    }

    private static string MapSeasonState(
        int number,
        bool belongsToThisRequest,
        IReadOnlyDictionary<int, int> downloadStatuses)
    {
        if (belongsToThisRequest)
        {
            return "pending";
        }

        if (!downloadStatuses.TryGetValue(number, out int status))
        {
            return "none";
        }

        return status switch
        {
            5 => "available",
            >= 2 and <= 4 => "processing",
            _ => "none",
        };
    }

    // Requested seasons win; otherwise the detail's seasons are echoed so the PUT never
    // resets them. An empty result stays null and is then omitted from the body.
    private static List<int>? ResolveUpdateSeasons(SeerrRequestUpdateBody body, SeerrRequestResource detail)
    {
        if (body.Seasons is { Count: > 0 } requestedSeasons)
        {
            return [.. requestedSeasons];
        }

        List<int> mirroredSeasons = [.. MapSeasonNumbers(detail.Seasons)];
        return mirroredSeasons.Count > 0 ? mirroredSeasons : null;
    }

    // Seerr keeps the profile and the folder a request was created with even after the arr
    // dropped that profile or moved its library, and the dialog selects cannot render a value
    // that is not one of their options (live probe 2026-10-30: a movie request still carries
    // profileId 9 and the folder "/media/movies (anime)" while radarr offers ids 15 to 18 under
    // "/media/videos/movies"). A candidate therefore only wins when the service route really
    // offers it, so the preselection falls back to the server default and then to the first
    // option instead of showing an empty field that would save an invalid value back.
    private static int? ResolveOfferedProfile(IReadOnlyList<SeerrEditOption> options, params int?[] candidates)
    {
        foreach (int? candidate in candidates)
        {
            if (candidate is int id && options.Any(option => option.Id == id))
            {
                return candidate;
            }
        }

        return options.FirstOrDefault()?.Id;
    }

    private static string? ResolveOfferedFolder(IReadOnlyList<string> folders, params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            string? path = candidate?.Trim();
            if (!string.IsNullOrWhiteSpace(path) && folders.Contains(path))
            {
                return path;
            }
        }

        return folders.FirstOrDefault();
    }

    private static string? FirstNonBlank(string? preferred, string? fallback)
    {
        string? value = preferred?.Trim();
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        value = fallback?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    // Seerr's request detail sends the type as a numeric enum (1 movie, 2 tv) or as text.
    private static string? MapDetailMediaType(SeerrRequestResource? detail)
    {
        if (detail is null)
        {
            return null;
        }

        return detail.Type?.Trim() switch
        {
            "movie" or "1" => "movie",
            "tv" or "2" => "tv",
            _ => detail.MediaType?.Trim().ToLowerInvariant() switch
            {
                "movie" => "movie",
                "tv" => "tv",
                _ => null,
            },
        };
    }

    private static ProblemHttpResult NotConfiguredProblem()
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

    // Seerr answers write operations for vanished requests with HTTP 404, and that case must
    // stay distinguishable from an unreachable instance for the frontend reload.
    private static ProblemHttpResult SeerrWriteProblem(HttpRequestException exception, string operation)
    {
        if (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return RequestNotFoundProblem();
        }

        return TypedResults.Problem(new ProblemDetails
        {
            Title = "Seerr is not reachable.",
            Detail = $"The configured Seerr instance did not answer the {operation} operation.",
            Status = StatusCodes.Status502BadGateway
        });
    }

    private static ProblemHttpResult RequestNotFoundProblem()
    {
        return TypedResults.Problem(new ProblemDetails
        {
            Title = "Request not found.",
            Detail = "The request no longer exists in Seerr.",
            Status = StatusCodes.Status404NotFound
        });
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
