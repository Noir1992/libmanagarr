namespace LibManagarr.Api.Models;

public sealed class SonarrSeasonResponse
{
    public int Number { get; init; }

    public bool Monitored { get; init; }

    public IReadOnlyList<SonarrEpisodeResponse> Episodes { get; init; } = [];
}
