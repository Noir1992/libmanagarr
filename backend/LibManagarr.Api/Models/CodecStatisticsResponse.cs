using System.Text.Json.Serialization;

namespace LibManagarr.Api.Models;

public sealed class CodecStatisticsResponse
{
    public IReadOnlyList<CodecStatisticItem> ContainerFormats { get; set; } = [];

    public IReadOnlyList<CodecStatisticItem> Resolutions { get; set; } = [];

    public IReadOnlyList<CodecStatisticItem> DynamicRanges { get; set; } = [];

    public IReadOnlyList<CodecStatisticItem> VideoCodecs { get; set; } = [];

    public IReadOnlyList<CodecStatisticItem> AudioCodecs { get; set; } = [];

    public IReadOnlyList<CodecStatisticItem> VideoBitrates { get; set; } = [];

    public IReadOnlyList<LanguageStatisticItem> AudioLanguages { get; set; } = [];

    public IReadOnlyList<LanguageStatisticItem> SubtitleLanguages { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CodecMediaFileItem>? Files { get; set; }

    public LibraryFilterValues FilterValues { get; set; } = new();

    public Dictionary<string, Dictionary<string, int>> Counts { get; set; } = [];
}
