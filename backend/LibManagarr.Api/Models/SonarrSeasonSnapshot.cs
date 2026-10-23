namespace LibManagarr.Api.Models;

public sealed class SonarrSeasonSnapshot
{
    public int SeasonNumber { get; set; }

    public bool Monitored { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<SonarrEpisodeSnapshot> Episodes { get; set; } = [];
}
