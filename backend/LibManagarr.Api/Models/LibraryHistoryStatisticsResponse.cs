namespace LibManagarr.Api.Models;

public sealed class LibraryHistoryStatisticsResponse
{
    public List<LibraryHistoryPoint> Points { get; set; } = [];

    public List<LibraryChangeHistoryEntry> History { get; set; } = [];

    public long TotalMediaFileCount { get; set; }

    public long TotalSizeBytes { get; set; }

    public DateTimeOffset? AvailableFrom { get; set; }

    public DateTimeOffset? AvailableTo { get; set; }
}
