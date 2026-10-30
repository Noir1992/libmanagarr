using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Clients;

public sealed class SeerrClient(HttpClient httpClient)
{
    // Bounded parallelism for the per-media metadata calls that enrich one page of requests.
    private const int MetadataConcurrency = 8;

    // Overseerr sends the request status as a numeric enum; the plan's names map by position.
    private static readonly IReadOnlyDictionary<string, string> StatusByWireValue = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["1"] = "pending",
        ["2"] = "approved",
        ["3"] = "declined",
        ["4"] = "processing",
        ["5"] = "available",
        ["6"] = "failed",
    };

    private static readonly IReadOnlySet<string> StatusNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "approved",
        "declined",
        "processing",
        "available",
        "failed",
    };

    public async Task TestConnectionAsync(
        SeerrInstanceSettings instance,
        CancellationToken cancellationToken)
    {
        _ = await GetAsync<JsonElement>(instance, "/api/v1/auth/me", cancellationToken);
    }

    public async Task<SeerrRequestsResponse> GetRequestsAsync(
        SeerrInstanceSettings instance,
        int page,
        int limit,
        string sortField,
        string sortOrder,
        string? statusFilter,
        CancellationToken cancellationToken)
    {
        // Seerr rejects unknown query parameters and has no server-side page parameter, so the
        // window is fetched with take = page * limit and sliced in memory below.
        List<string> queryParts =
        [
            $"take={page * limit}",
            $"sort={MapSortField(sortField)}",
            $"sortDirection={Uri.EscapeDataString(sortOrder.ToLowerInvariant())}",
        ];
        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            queryParts.Add($"filter={Uri.EscapeDataString(statusFilter.ToLowerInvariant())}");
        }

        SeerrRequestsResource? resource = await GetAsync<SeerrRequestsResource>(
            instance,
            $"/api/v1/request?{string.Join('&', queryParts)}",
            cancellationToken);

        // The server always answers with page 1 of the filtered result set, so the requested
        // window is cut from the fetched head and the reported total comes from pageInfo.results.
        List<SeerrRequestResource> window = [.. (resource?.Results ?? [])
            .Skip((page - 1) * limit)
            .Take(limit)];
        List<SeerrRequest> requests = [.. window.Select(MapRequest)];

        await EnrichWithMediaMetadataAsync(instance, window, requests, cancellationToken);

        return new SeerrRequestsResponse
        {
            Requests = requests,
            Total = resource?.PageInfo?.TotalResults ?? (page - 1) * limit + requests.Count,
            Page = page,
            Limit = limit,
        };
    }

    // Write operations verified against the live Seerr instance: approve and decline are POSTs
    // to a status suffix, deletion uses the DELETE verb, and edits use a PUT whose body must
    // carry mediaType. All of them answer 404 "Request not found." for unknown request ids.
    public async Task ApproveRequestAsync(
        SeerrInstanceSettings instance,
        int requestId,
        CancellationToken cancellationToken)
    {
        await SendAsync(HttpMethod.Post, instance, $"/api/v1/request/{requestId}/approve", null, cancellationToken);
    }

    public async Task DeclineRequestAsync(
        SeerrInstanceSettings instance,
        int requestId,
        string? reason,
        CancellationToken cancellationToken)
    {
        SeerrDeclineResource? body = string.IsNullOrWhiteSpace(reason)
            ? null
            : new SeerrDeclineResource { Reason = reason.Trim() };

        await SendAsync(HttpMethod.Post, instance, $"/api/v1/request/{requestId}/decline", body, cancellationToken);
    }

    public async Task DeleteRequestAsync(
        SeerrInstanceSettings instance,
        int requestId,
        CancellationToken cancellationToken)
    {
        await SendAsync(HttpMethod.Delete, instance, $"/api/v1/request/{requestId}", null, cancellationToken);
    }

    public async Task UpdateRequestAsync(
        SeerrInstanceSettings instance,
        int requestId,
        SeerrRequestUpdateResource body,
        CancellationToken cancellationToken)
    {
        await SendAsync(HttpMethod.Put, instance, $"/api/v1/request/{requestId}", body, cancellationToken);
    }

    // The detail carries the editable fields (serverId, profileId, rootFolder, languageProfileId)
    // that the list omits; the edit flow reads it before sending the PUT.
    public async Task<SeerrRequestResource?> GetRequestDetailAsync(
        SeerrInstanceSettings instance,
        int requestId,
        CancellationToken cancellationToken)
    {
        return await GetAsync<SeerrRequestResource>(instance, $"/api/v1/request/{requestId}", cancellationToken);
    }

    // Server settings embed the arr api keys in plaintext; SeerrServerResource maps only the
    // display fields, so this projection is all the api ever forwards.
    public async Task<IReadOnlyList<SeerrServerResource>> GetServersAsync(
        SeerrInstanceSettings instance,
        string application,
        CancellationToken cancellationToken)
    {
        return await GetAsync<List<SeerrServerResource>>(
            instance,
            $"/api/v1/settings/{application}",
            cancellationToken) ?? [];
    }

    // The service route is what Seerr's own dialogs use: it answers the quality profiles of
    // both applications and the arr's real root folder list for one server, next to the
    // server's default profile and directory used as the edit preselection.
    public async Task<SeerrServiceResource?> GetServiceInfoAsync(
        SeerrInstanceSettings instance,
        string application,
        int serverId,
        CancellationToken cancellationToken)
    {
        return await GetAsync<SeerrServiceResource>(
            instance,
            $"/api/v1/service/{application}/{serverId}",
            cancellationToken);
    }

    public async Task<SeerrMediaMetadataResource?> GetMediaMetadataAsync(
        SeerrInstanceSettings instance,
        string type,
        int tmdbId,
        CancellationToken cancellationToken)
    {
        return await GetAsync<SeerrMediaMetadataResource>(
            instance,
            type == "tv" ? $"/api/v1/tv/{tmdbId}" : $"/api/v1/movie/{tmdbId}",
            cancellationToken);
    }

    // The request list carries no titles, posters or dates; Seerr's TMDB metadata proxy
    // supplies them per media id. Failures stay graceful and keep the slug fallback.
    private async Task EnrichWithMediaMetadataAsync(
        SeerrInstanceSettings instance,
        IReadOnlyList<SeerrRequestResource> window,
        IReadOnlyList<SeerrRequest> requests,
        CancellationToken cancellationToken)
    {
        using SemaphoreSlim gate = new(MetadataConcurrency, MetadataConcurrency);

        await Task.WhenAll(Enumerable.Range(0, window.Count).Select(async index =>
        {
            SeerrRequest request = requests[index];
            if (request.Type is not ("movie" or "tv")
                || !int.TryParse(window[index].Media?.TmdbId, out int tmdbId))
            {
                return;
            }

            await gate.WaitAsync(cancellationToken);
            try
            {
                SeerrMediaMetadataResource? metadata = await GetMediaMetadataAsync(
                    instance,
                    request.Type,
                    tmdbId,
                    cancellationToken);

                ApplyMetadata(request, metadata, tmdbId);
            }
            catch (HttpRequestException)
            {
                // Unknown or unreachable media keeps the fallbacks mapped from the list entry.
            }
            finally
            {
                gate.Release();
            }
        }));
    }

    private static void ApplyMetadata(SeerrRequest request, SeerrMediaMetadataResource? metadata, int tmdbId)
    {
        if (metadata is null)
        {
            return;
        }

        string? title = metadata.Title ?? metadata.Name;
        if (!string.IsNullOrWhiteSpace(title))
        {
            request.Title = title;
        }

        if (!string.IsNullOrWhiteSpace(metadata.PosterPath))
        {
            // Posters render through this app's poster cache endpoint instead of TMDB directly.
            request.PosterPath = $"/api/requests/{request.Type}/poster/{tmdbId}";
        }

        if (request.Year is null)
        {
            request.Year = ParseDate(metadata.ReleaseDate ?? metadata.FirstAirDate)?.Year;
        }
    }

    private static SeerrRequest MapRequest(SeerrRequestResource resource)
    {
        SeerrMediaResource? media = resource.Media;

        return new SeerrRequest
        {
            Id = resource.Id ?? string.Empty,
            Status = MapStatus(resource.Status),
            Is4kRequest = resource.Is4k ?? resource.Is4kRequest ?? false,
            Type = MapType(resource.MediaType ?? resource.Type ?? media?.MediaType),
            RequestedBy = resource.RequestedBy?.DisplayName,
            RequestedAt = ParseDate(resource.CreatedAt),
            Title = media?.Title ?? HumanizeSlug(media?.ExternalServiceSlug),
            PosterPath = media?.PosterPath ?? media?.CoverPath,
            Year = ResolveYear(media),
            Seasons = MapSeasons(resource.Seasons),
        };
    }

    // Tv requests list the seasons they cover; keep sorted distinct numbers and
    // report "no seasons" as null so movies and whole-series requests look alike.
    private static IReadOnlyList<int>? MapSeasons(List<SeerrSeasonResource>? seasons)
    {
        if (seasons is null || seasons.Count == 0)
        {
            return null;
        }

        int[] numbers = [.. seasons
            .Select((season) => season.SeasonNumber)
            .Where((number) => number.HasValue)
            .Select((number) => number!.Value)
            .Distinct()
            .Order()];

        return numbers.Length == 0 ? null : numbers;
    }

    // Seerr request lists carry no title, only an external service slug like
    // "a-wild-last-boss-appeared"; turn it into a readable sentence-case fallback.
    // Movie slugs are the bare numeric external id, not a name, and must never render.
    private static string? HumanizeSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        string words = slug.Replace('-', ' ').Trim();
        if (words.Length == 0 || words.All(char.IsDigit))
        {
            return null;
        }

        return char.ToUpper(words[0], CultureInfo.InvariantCulture) + words[1..];
    }

    // Seerr only sorts by "added" or "modified"; the api keeps the createdAt/updatedAt names.
    private static string MapSortField(string sortField)
    {
        return sortField.Trim().ToLowerInvariant() == "updatedat" ? "modified" : "added";
    }

    private static string MapStatus(string? wireStatus)
    {
        if (string.IsNullOrWhiteSpace(wireStatus))
        {
            return "unknown";
        }

        string value = wireStatus.Trim();
        if (StatusByWireValue.TryGetValue(value, out string? name))
        {
            return name;
        }

        return StatusNames.Contains(value) ? value.ToLowerInvariant() : "unknown";
    }

    private static string? MapType(string? rawType)
    {
        return rawType?.Trim().ToLowerInvariant() switch
        {
            "movie" => "movie",
            "tv" or "series" or "tvshow" => "tv",
            _ => null,
        };
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Overseerr sends ISO 8601 timestamps; anything unparsable stays null.
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    private static int? ResolveYear(SeerrMediaResource? media)
    {
        if (media is null)
        {
            return null;
        }

        foreach (string? dateValue in new string?[] { media.ReleaseDate, media.FirstAirDate })
        {
            if (ParseDate(dateValue) is DateTimeOffset parsed)
            {
                return parsed.Year;
            }
        }

        return null;
    }

    private async Task<T?> GetAsync<T>(
        SeerrInstanceSettings instance,
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"{NormalizeBaseUrl(instance.BaseUrl)}{relativeUrl}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Api-Key", instance.ApiKey);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    // EnsureSuccessStatusCode carries the upstream status on the HttpRequestException, so the
    // endpoints can map Seerr's 404 "Request not found." to a distinguishable ProblemDetails.
    private async Task SendAsync(
        HttpMethod method,
        SeerrInstanceSettings instance,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, $"{NormalizeBaseUrl(instance.BaseUrl)}{relativeUrl}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Api-Key", instance.ApiKey);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        return baseUrl.Trim().TrimEnd('/');
    }
}
