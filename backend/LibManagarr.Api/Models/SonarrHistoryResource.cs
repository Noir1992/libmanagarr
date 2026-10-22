using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibManagarr.Api.Models;

public sealed class SonarrHistoryResource
{
    public int? Id { get; set; }

    public int? EpisodeId { get; set; }

    public int? SeriesId { get; set; }

    public string? SourceTitle { get; set; }

    public DateTimeOffset? Date { get; set; }

    public string? EventType { get; set; }

    public Dictionary<string, string>? Data { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}
