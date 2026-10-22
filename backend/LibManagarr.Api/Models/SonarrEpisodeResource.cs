namespace LibManagarr.Api.Models;

public sealed class SonarrEpisodeResource
{
    public int? Id { get; set; }

    public string? Title { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public bool? HasFile { get; set; }

    public int? EpisodeFileId { get; set; }

    public SonarrSeriesResource? Series { get; set; }

    public int? Runtime { get; set; }

    public DateTimeOffset? AirDateUtc { get; set; }

    public bool? Monitored { get; set; }
}
