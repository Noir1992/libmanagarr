namespace LibManagarr.Api.Models;

public sealed class LibraryTrendPoint
{
    public DateTimeOffset SyncedAt { get; set; }

    public long MediaFileCount { get; set; }

    public long TotalSizeBytes { get; set; }

    public long DeltaMediaFileCount { get; set; }

    public long DeltaSizeBytes { get; set; }
}
