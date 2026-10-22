using System.Text.Json.Serialization;
using System.Text.Json;

namespace LibManagarr.Api.Models;

public sealed class MediaInfoResource
{
    public long? VideoBitrate { get; set; }

    public string? VideoCodec { get; set; }

    public string? AudioCodec { get; set; }

    public string? AudioLanguages { get; set; }

    public string? Subtitles { get; set; }

    public string? ContainerFormat { get; set; }

    public string? VideoDynamicRange { get; set; }

    [JsonConverter(typeof(StringOrNumberToStringConverter))]
    public string? Resolution { get; set; }

    [JsonConverter(typeof(FlexibleNullableTimeSpanConverter))]
    public TimeSpan? RunTime { get; set; }

    [JsonConverter(typeof(StringOrNumberToStringConverter))]
    public string? VideoBitDepth { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }

    public string? ReadString(params string[] fieldNames)
    {
        foreach (string fieldName in fieldNames)
        {
            if (AdditionalData is null || !AdditionalData.TryGetValue(fieldName, out JsonElement value))
            {
                continue;
            }

            string? asText = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(asText))
            {
                return asText;
            }
        }

        return null;
    }
}
