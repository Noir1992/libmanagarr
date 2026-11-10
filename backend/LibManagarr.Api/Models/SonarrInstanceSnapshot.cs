namespace LibManagarr.Api.Models;

public sealed class SonarrInstanceSnapshot : IInstanceSnapshot
{
    public string InstanceId { get; set; } = string.Empty;

    public string InstanceName { get; set; } = string.Empty;

    public DateTimeOffset SyncedAt { get; set; }

    /// <summary>Retained during partial syncs so a later full sync still reads other series' history.</summary>
    public DateTimeOffset? LastFullSyncAt { get; set; }

    /// <summary>History already consumed by targeted series syncs since the last full sync.</summary>
    public Dictionary<int, DateTimeOffset> SeriesSyncTimes { get; set; } = [];

    public List<SonarrSeriesSnapshot> Series { get; set; } = [];

    public Dictionary<int, string?> PosterUrls { get; set; } = [];
}
