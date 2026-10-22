namespace LibManagarr.Api.Models;

public sealed class SonarrEpisodeSnapshot
{
    public int EpisodeId { get; set; }

    public int EpisodeNumber { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTimeOffset? AirDateUtc { get; set; }

    public bool Monitored { get; set; }

    // Older snapshots contain only entries with files.
    public bool HasFile { get; set; } = true;

    public MediaFileSnapshot File { get; set; } = new();
}
