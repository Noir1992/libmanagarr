using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public enum LibraryStatus
{
    Missing,
    Partial,
    Present
}

public static class LibraryAvailability
{
    // Present requires a file for every monitored episode, so a series with pending or unfinished seasons is only Partial.
    public static LibraryStatus GetStatus(SonarrSeriesSnapshot series)
    {
        List<SonarrEpisodeSnapshot> episodes = series.Seasons.SelectMany(season => season.Episodes).ToList();
        if (!episodes.Any(episode => episode.HasFile))
            return LibraryStatus.Missing;
        return episodes.Any(episode => episode.Monitored && !episode.HasFile) ? LibraryStatus.Partial : LibraryStatus.Present;
    }

    public static string ToWireValue(this LibraryStatus status) => status switch
    {
        LibraryStatus.Present => "present",
        LibraryStatus.Partial => "partial",
        _ => "missing"
    };

    public static bool IsMissing(SonarrEpisodeSnapshot episode, DateTimeOffset now) =>
        !episode.HasFile && episode.AirDateUtc.HasValue && episode.AirDateUtc.Value <= now;
}
