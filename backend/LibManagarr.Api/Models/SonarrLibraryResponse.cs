namespace LibManagarr.Api.Models;

public sealed class SonarrLibraryResponse
{
    public DateTimeOffset? SyncedAt { get; set; }

    public IReadOnlyList<SonarrLibraryEntryResponse> Rows { get; set; } = [];
}
