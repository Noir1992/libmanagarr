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

    // The request detail additionally carries the editable target fields; the list omits them.
    [JsonPropertyName("serverId")]
    public int? ServerId { get; set; }

    [JsonPropertyName("profileId")]
    public int? ProfileId { get; set; }

    [JsonPropertyName("rootFolder")]
    public string? RootFolder { get; set; }

    // Sent as a number; Seerr's PUT rejects a non-numeric languageProfileId, so it is echoed as int.
    [JsonPropertyName("languageProfileId")]
    public int? LanguageProfileId { get; set; }

    // Tv requests list the requested seasons; the mapper keeps only their season numbers.
    [JsonPropertyName("seasons")]
    public List<SeerrSeasonResource>? Seasons { get; set; }
}

// Shared season shape: the request detail sends it twice (the request's own seasons carry the
// request status, the media object lists every season of the show with its download status) and
// the tv metadata proxy uses it for the season list including the episode count.
public sealed class SeerrSeasonResource
{
    [JsonPropertyName("seasonNumber")]
    public int? SeasonNumber { get; set; }

    // Only the tv metadata seasons carry the episode count.
    [JsonPropertyName("episodeCount")]
    public int? EpisodeCount { get; set; }

    // Numeric status; media seasons use the download enum (5 = on disk, 2-4 = arriving,
    // 1 = untouched), the request's own seasons the request enum (1 = pending, 5 = available).
    [JsonPropertyName("status")]
    public int? Status { get; set; }
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

    // The request detail lists every season of the show together with its download status, so
    // the edit dialog can tell an already-available season from an untouched one. Pending
    // requests do not have it yet, so the season list stays empty and the metadata fills the gap.
    [JsonPropertyName("seasons")]
    public List<SeerrSeasonResource>? Seasons { get; set; }
}
