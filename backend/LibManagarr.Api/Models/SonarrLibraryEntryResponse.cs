namespace LibManagarr.Api.Models;

public sealed class SonarrLibraryEntryResponse
{
    public string InstanceName { get; init; } = string.Empty;

    public int SeriesId { get; init; }

    public string SeriesTitle { get; init; } = string.Empty;

    public string SeriesTitleSlug { get; init; } = string.Empty;

    public string QualityProfileName { get; init; } = string.Empty;
    public int? UpgradeUntilCustomFormatScore { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];

    public DateTimeOffset? AddedAt { get; init; }

    public string Status { get; init; } = string.Empty;

    public string LibraryStatus { get; init; } = "missing";

    public bool Monitored { get; init; }

    public IReadOnlyList<SonarrSeasonResponse> Seasons { get; init; } = [];

    public int LibraryEpisodeCount { get; init; }
    public int FileCount { get; init; }

    public long? TotalSize { get; init; }

    public int TotalEpisodes { get; init; }

    public int AiredEpisodes { get; init; }
}
