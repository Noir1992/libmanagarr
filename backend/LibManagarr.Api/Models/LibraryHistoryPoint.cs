namespace LibManagarr.Api.Models;

public sealed class LibraryHistoryPoint
{
    public DateTimeOffset OccurredAt { get; set; }

    public int MediaFileCount { get; set; }

    public long TotalSizeBytes { get; set; }

    public int DeltaMediaFileCount { get; set; }

    public long DeltaSizeBytes { get; set; }
}
