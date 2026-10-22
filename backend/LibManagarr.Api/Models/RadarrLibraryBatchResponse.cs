namespace LibManagarr.Api.Models;

public sealed class RadarrLibraryBatchResponse
{
    public Dictionary<string, Dictionary<string, int>> Counts { get; init; } = [];

    public DateTimeOffset? SyncedAt { get; set; }

    public IReadOnlyList<RadarrLibraryInstanceResult> Instances { get; set; } = [];
}

public sealed class RadarrLibraryInstanceResult
{
    public string InstanceId { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public IReadOnlyList<RadarrLibraryEntryResponse> Rows { get; set; } = [];
}
