namespace LibManagarr.Api.Models;

// Flattened request DTO exposed to the frontend; title, poster and year are resolved
// from the nested "media" object and the API key never reaches this model.
public sealed class SeerrRequest
{
    public string Id { get; set; } = string.Empty;

    // One of: pending, approved, declined, processing, available, failed, unknown.
    public string Status { get; set; } = "unknown";

    public bool Is4kRequest { get; set; }

    // One of: movie, tv; null when the response carries no media type.
    public string? Type { get; set; }

    public string? RequestedBy { get; set; }

    public DateTimeOffset? RequestedAt { get; set; }

    public string? Title { get; set; }

    public string? PosterPath { get; set; }

    public int? Year { get; set; }

    // Sorted distinct season numbers of tv requests; null for movies and requests without seasons.
    public IReadOnlyList<int>? Seasons { get; set; }
}
