using System.Text.Json.Serialization;

namespace LibManagarr.Api.Models;

// Wire models for the paged Overseerr-compatible "GET /api/v1/request" response.
// Field names follow the Overseerr/Seerr API; every field is nullable so a response
// with missing "media" or "requestedBy" objects still maps without errors.
public sealed class SeerrRequestsResource
{
    [JsonPropertyName("pageInfo")]
    public SeerrPageInfoResource? PageInfo { get; set; }

    [JsonPropertyName("results")]
    public List<SeerrRequestResource>? Results { get; set; }
}

public sealed class SeerrPageInfoResource
{
    [JsonPropertyName("page")]
    public int? Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int? PageSize { get; set; }

    [JsonPropertyName("results")]
    public int? TotalResults { get; set; }

    [JsonPropertyName("totalPages")]
    public int? TotalPages { get; set; }
}

public sealed class SeerrRequestResource
{
    // Overseerr sends numeric ids; accept both numeric and string values.
    [JsonPropertyName("id")]
    [JsonConverter(typeof(StringOrNumberToStringConverter))]
    public string? Id { get; set; }

    // Overseerr sends the status as a numeric enum; the mapper resolves numbers and names.
    [JsonPropertyName("status")]
    [JsonConverter(typeof(StringOrNumberToStringConverter))]
    public string? Status { get; set; }

    // Seerr sends the 4k flag as "is4k"; the older Overseerr spelling "is4kRequest" is still accepted.
    [JsonPropertyName("is4k")]
    public bool? Is4k { get; set; }

    [JsonPropertyName("is4kRequest")]
    public bool? Is4kRequest { get; set; }

    // Overseerr sends "type" as a numeric request type and "mediaType" as "movie"/"tv".
    [JsonPropertyName("type")]
    [JsonConverter(typeof(StringOrNumberToStringConverter))]
    public string? Type { get; set; }

    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("requestedBy")]
    public SeerrUserResource? RequestedBy { get; set; }

    [JsonPropertyName("media")]
    public SeerrMediaResource? Media { get; set; }
}

public sealed class SeerrUserResource
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(StringOrNumberToStringConverter))]
    public string? Id { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}

public sealed class SeerrMediaResource
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(StringOrNumberToStringConverter))]
    public string? Id { get; set; }

    // The request list embeds the TMDB id; it keys the metadata and poster lookups.
    [JsonPropertyName("tmdbId")]
    [JsonConverter(typeof(StringOrNumberToStringConverter))]
    public string? TmdbId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    // Overseerr stores the cover image path; Seerr may expose a TMDB-style poster path instead.
    [JsonPropertyName("coverPath")]
    public string? CoverPath { get; set; }

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("firstAirDate")]
    public string? FirstAirDate { get; set; }

    // Current Seerr builds omit title and images from the request list; the slug is the only
    // human-readable hint, so the mapper humanizes it as the title fallback.
    [JsonPropertyName("externalServiceSlug")]
    public string? ExternalServiceSlug { get; set; }
}
