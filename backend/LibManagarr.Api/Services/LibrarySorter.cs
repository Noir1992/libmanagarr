using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>Orders library entries by the field the client requested, defaulting to alphabetical.</summary>
public static class LibrarySorter
{
    public static IReadOnlyList<RadarrLibraryEntryResponse> SortRadarr(
        IEnumerable<RadarrLibraryEntryResponse> entries, LibraryRequestFilter filter, TitleSortingSettings? titleSorting = null)
    {
        LibrarySortField field = ResolveField(filter);
        return Order(entries, field, filter.SortDirection, (entry) => new SortKey(entry.MovieTitle, TitleSortKeyBuilder.Build(entry.MovieTitle, titleSorting ?? new()), field switch
        {
            LibrarySortField.Added => entry.AddedAt,
            LibrarySortField.Aired => entry.AirDateUtc,
            _ => null,
        }, entry.Size));
    }

    public static IReadOnlyList<SonarrLibraryEntryResponse> SortSonarr(
        IEnumerable<SonarrLibraryEntryResponse> entries, LibraryRequestFilter filter, TitleSortingSettings? titleSorting = null)
    {
        LibrarySortField field = ResolveField(filter);
        return Order(entries, field, filter.SortDirection, (entry) => new SortKey(entry.SeriesTitle, TitleSortKeyBuilder.Build(entry.SeriesTitle, titleSorting ?? new()), field switch
        {
            LibrarySortField.Added => entry.AddedAt,
            LibrarySortField.Aired => LatestEpisodeAirDate(entry),
            _ => null,
        }, entry.TotalSize));
    }

    private static LibrarySortField ResolveField(LibraryRequestFilter filter) =>
        filter.SortField ?? LibrarySortField.Alphabetical;

    private static IReadOnlyList<T> Order<T>(
        IEnumerable<T> entries,
        LibrarySortField field,
        LibrarySortDirection? direction,
        Func<T, SortKey> keySelector)
    {
        bool descending = direction == LibrarySortDirection.Descending;
        return entries
            .OrderBy(keySelector, Comparer<SortKey>.Create((x, y) => Compare(x, y, field, descending)))
            .ToArray();
    }

    private static int Compare(SortKey x, SortKey y, LibrarySortField field, bool descending)
    {
        if (field == LibrarySortField.Alphabetical)
            return CompareTitles(x, y, descending);

        int byValue = field == LibrarySortField.Size
            ? CompareValues(x.Size, y.Size, descending)
            : CompareValues(x.Date, y.Date, descending);
        return byValue != 0 ? byValue : CompareTitles(x, y, false);
    }

    /// <summary>Entries without a value trail in both directions, so unknown entries never crowd out the top.</summary>
    private static int CompareValues<T>(T? x, T? y, bool descending) where T : struct, IComparable<T>
    {
        if (x.HasValue != y.HasValue)
            return x.HasValue ? -1 : 1;
        if (!x.HasValue)
            return 0;
        return descending ? y!.Value.CompareTo(x!.Value) : x.Value.CompareTo(y!.Value);
    }

    private static int CompareTitles(SortKey x, SortKey y, bool descending)
    {
        int result = string.Compare(x.SortTitle, y.SortTitle, StringComparison.OrdinalIgnoreCase);
        if (result == 0) result = string.Compare(x.Title, y.Title, StringComparison.OrdinalIgnoreCase);
        return descending ? -result : result;
    }

    private static DateTimeOffset? LatestEpisodeAirDate(SonarrLibraryEntryResponse entry) =>
        entry.Seasons.SelectMany(season => season.Episodes).Select(episode => episode.AirDateUtc).Max();

    private readonly record struct SortKey(string Title, string SortTitle, DateTimeOffset? Date, long? Size);
}
