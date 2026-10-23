namespace LibManagarr.Api.Models;

public sealed class SonarrEpisodeResponse
{
    public int EpisodeId { get; init; }
    public bool HasAired { get; init; }
    public LibraryMediaDetailsResponse? MediaDetails { get; init; }

    public int Number { get; init; }

    public string Title { get; init; } = string.Empty;

    public DateTimeOffset? AirDateUtc { get; init; }

    public bool HasFile { get; init; }
    public bool Monitored { get; init; }

    public int? CustomFormatScore { get; init; }
}
