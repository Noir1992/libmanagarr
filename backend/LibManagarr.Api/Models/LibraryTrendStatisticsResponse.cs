namespace LibManagarr.Api.Models;

public sealed class LibraryTrendStatisticsResponse
{
    public List<LibraryTrendPoint> Points { get; set; } = [];

    public long TotalMediaFileCount { get; set; }

    public long TotalSizeBytes { get; set; }

    public DateTimeOffset? AvailableFrom { get; set; }

    public DateTimeOffset? AvailableTo { get; set; }
}
