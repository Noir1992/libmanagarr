namespace LibManagarr.Api.Models;

public sealed class SonarrInstanceSnapshot : IInstanceSnapshot
{
    public string InstanceId { get; set; } = string.Empty;

    public string InstanceName { get; set; } = string.Empty;

    public DateTimeOffset SyncedAt { get; set; }

    public List<SonarrSeriesSnapshot> Series { get; set; } = [];

    public Dictionary<int, string?> PosterUrls { get; set; } = [];
}
