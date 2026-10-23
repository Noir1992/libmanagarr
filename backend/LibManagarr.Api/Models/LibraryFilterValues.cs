namespace LibManagarr.Api.Models;

/// <summary>
/// Aggregated unique filter values for a single ARR instance. Populated during sync.
/// </summary>
public sealed class LibraryFilterValues
{
    public const int CurrentVersion = 7;

    public int Version { get; init; }

    public Dictionary<string, Dictionary<string, int>> Counts { get; init; } = [];

    public string InstanceId { get; init; } = string.Empty;

    public HashSet<string> Resolutions { get; init; } = [];

    public HashSet<string> VideoCodecs { get; init; } = [];

    public HashSet<string> AudioCodecs { get; init; } = [];

    public HashSet<string> DynamicRanges { get; init; } = [];

    public HashSet<string> ContainerFormats { get; init; } = [];

    public HashSet<string> Genres { get; init; } = [];

    public HashSet<string> Tags { get; init; } = [];

    public HashSet<string> Statuses { get; init; } = [];

    public HashSet<string> QualityProfiles { get; init; } = [];

    public HashSet<string> AudioLanguages { get; init; } = [];

    public HashSet<string> SubtitleLanguages { get; init; } = [];
}
