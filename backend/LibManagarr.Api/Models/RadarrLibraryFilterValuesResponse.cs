namespace LibManagarr.Api.Models;

public sealed class RadarrLibraryFilterValuesResponse
{
    public Dictionary<string, Dictionary<string, int>> Counts { get; init; } = [];

    public IReadOnlyList<string> Resolutions { get; init; } = [];

    public IReadOnlyList<string> VideoCodecs { get; init; } = [];

    public IReadOnlyList<string> AudioCodecs { get; init; } = [];

    public IReadOnlyList<string> DynamicRanges { get; init; } = [];

    public IReadOnlyList<string> ContainerFormats { get; init; } = [];

    public IReadOnlyList<string> Genres { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    public IReadOnlyList<string> Statuses { get; init; } = [];

    public IReadOnlyList<string> QualityProfiles { get; init; } = [];

    public IReadOnlyList<string> AudioLanguages { get; init; } = [];

    public IReadOnlyList<string> SubtitleLanguages { get; init; } = [];
}
