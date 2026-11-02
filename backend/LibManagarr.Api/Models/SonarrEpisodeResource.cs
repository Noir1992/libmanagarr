namespace LibManagarr.Api.Models;

public sealed class SonarrEpisodeResource
{
    public int? Id { get; set; }

    public string? Title { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public bool? HasFile { get; set; }

    public int? EpisodeFileId { get; set; }

    public int? SeriesId { get; set; }

    public SonarrSeriesResource? Series { get; set; }

    public int? Runtime { get; set; }

    // Pure calendar date as reported by Sonarr, without a clock time. Read it as the raw
    // deserialized day so it never shifts through a UTC conversion.
    public DateTimeOffset? AirDate { get; set; }

    public DateTimeOffset? AirDateUtc { get; set; }

    public bool? Monitored { get; set; }
}
