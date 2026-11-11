namespace LibManagarr.Api.Models;

public sealed class SonarrSeriesSnapshot
{
    public int SeriesId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string TitleSlug { get; init; } = string.Empty;

    public string? Path { get; init; }

    public string? PosterRemoteUrl { get; init; }

    public MediaProfileSnapshot Profile { get; init; } = new();

    public DateTimeOffset? AddedAt { get; init; }

    public string Status { get; init; } = string.Empty;

    public IReadOnlyList<string> Genres { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    public bool Monitored { get; init; }

    public DateTimeOffset? NextEpisodeAirDate { get; init; }

    public bool HasAiredEpisodes { get; init; }

    public List<SonarrSeasonSnapshot> Seasons { get; init; } = [];
}
