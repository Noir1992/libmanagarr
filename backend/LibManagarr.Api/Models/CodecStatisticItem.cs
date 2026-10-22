using System.Text.Json.Serialization;

namespace LibManagarr.Api.Models;

public sealed class CodecStatisticItem
{
    public string Label { get; set; } = string.Empty;

    public int Count { get; set; }

    public double Percentage { get; set; }

    public List<CodecMediaFileItem> MediaFiles { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? FileIds { get; set; }

    public long TotalSizeBytes { get; set; }
}
