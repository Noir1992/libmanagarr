namespace LibManagarr.Api.Models;

public sealed class RadarrInstanceSnapshot : IInstanceSnapshot
{
    public string InstanceId { get; set; } = string.Empty;

    public string InstanceName { get; set; } = string.Empty;

    public DateTimeOffset SyncedAt { get; set; }

    public List<RadarrMovieSnapshot> Movies { get; set; } = [];

    public Dictionary<int, string?> PosterUrls { get; set; } = [];
}
