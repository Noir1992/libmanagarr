using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public static class LibraryAvailability
{
    // Require a file, but ignore future and unmonitored missing episodes (such as deselected specials).
    public static bool IsAvailable(SonarrSeriesSnapshot series, DateTimeOffset now) =>
        series.Seasons.SelectMany(season => season.Episodes).Any(episode => episode.HasFile)
        && !series.Seasons.SelectMany(season => season.Episodes).Any(episode => episode.Monitored && IsMissing(episode, now));

    public static bool IsMissing(SonarrEpisodeSnapshot episode, DateTimeOffset now) =>
        !episode.HasFile && episode.AirDateUtc.HasValue && episode.AirDateUtc.Value <= now;
}
