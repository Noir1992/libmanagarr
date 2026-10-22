namespace LibManagarr.Api.Models;

public sealed class RadarrMovieSnapshot
{
    public int MovieId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string TitleSlug { get; init; } = string.Empty;

    public string? PosterRemoteUrl { get; init; }

    public MediaProfileSnapshot Profile { get; init; } = new();

    public DateTimeOffset? AddedAt { get; init; }

    public string Status { get; init; } = string.Empty;

    public IReadOnlyList<string> Genres { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    public bool Monitored { get; init; }

    public DateTimeOffset? AirDateUtc { get; init; }

    public bool HasAired { get; init; }

    // Older snapshots contain only entries with files.
    public bool HasFile { get; set; } = true;

    public MediaFileSnapshot File { get; init; } = new();
}
