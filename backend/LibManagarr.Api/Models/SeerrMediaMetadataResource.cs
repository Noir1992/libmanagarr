using System.Text.Json.Serialization;

namespace LibManagarr.Api.Models;

// Wire model for Seerr's per-media TMDB metadata proxy ("GET /api/v1/movie/{tmdbId}" and
// "GET /api/v1/tv/{tmdbId}"). Movies send the name as "title", series as "name".
public sealed class SeerrMediaMetadataResource
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    // TMDB-relative poster path like "/mbUbTCc5oc5chSfVmJEAfcz19mn.jpg"; it resolves
    // against the TMDB image host, not against the Seerr base url.
    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("firstAirDate")]
    public string? FirstAirDate { get; set; }

    // Tv metadata lists every season of the show; the edit dialog offers them for selection.
    [JsonPropertyName("seasons")]
    public List<SeerrSeasonResource>? Seasons { get; set; }

    // TMDB keywords; the exact keyword "anime" selects the server anime defaults in the edit
    // dialog. Keyword objects only carry an id and a localized name here.
    [JsonPropertyName("keywords")]
    public List<SeerrKeywordResource>? Keywords { get; set; }
}

public sealed class SeerrKeywordResource
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
