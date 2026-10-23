namespace LibManagarr.Api.Models;

public sealed class SonarrLibraryBatchResponse
{
    public Dictionary<string, Dictionary<string, int>> Counts { get; init; } = [];

    public DateTimeOffset? SyncedAt { get; set; }

    public IReadOnlyList<SonarrLibraryInstanceResult> Instances { get; set; } = [];
}

public sealed class SonarrLibraryInstanceResult
{
    public string InstanceId { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public IReadOnlyList<SonarrLibraryEntryResponse> Rows { get; set; } = [];
}
