using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public static class LibrarySizeCalculator
{
    // Use the complete snapshot, independent of episode filters. Multi-episode files share a path.
    public static long? SeriesSize(SonarrSeriesSnapshot series)
    {
        MediaFileSnapshot[] files = series.Seasons.SelectMany(season => season.Episodes)
            .Where(episode => episode.HasFile)
            .Select(episode => episode.File)
            .ToArray();
        if (files.Length == 0)
            return 0;

        long?[] sizes = files
            .GroupBy(file => string.IsNullOrWhiteSpace(file.RelativePath) ? null : file.RelativePath)
            .SelectMany(group => group.Key is null
                ? group.Select(file => file.Size)
                : new[] { group.Max(file => file.Size) })
            .ToArray();
        return sizes.Any(size => size.HasValue) ? sizes.Sum() : null;
    }
}
