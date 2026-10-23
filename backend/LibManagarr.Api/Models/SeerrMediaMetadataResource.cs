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
}
