namespace LibManagarr.Api.Models;

public sealed class RadarrLibraryResponse
{
    public DateTimeOffset? SyncedAt { get; set; }

    public IReadOnlyList<RadarrLibraryEntryResponse> Rows { get; set; } = [];
}
