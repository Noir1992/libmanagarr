using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibManagarr.Api.Models;

/// <summary>
/// Reads an identifier that the Arr APIs fill with 0 while nothing is behind it: both Sonarr's
/// <c>episodeFileId</c> and Radarr's <c>movieFileId</c> answer 0 for a file that does not exist, next to a
/// <c>hasFile</c> that says false. A caller that then asks whether an identifier is present gets "yes" for
/// a file that is not there, so the absence is decided once here instead of in every comparison.
/// </summary>
public sealed class ZeroIsMissingInt32Converter : JsonConverter<int?>
{
    public override int? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;

        int id = reader.GetInt32();
        return id == 0 ? null : id;
    }

    public override void Write(
        Utf8JsonWriter writer,
        int? value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteNumberValue(value.Value);
    }
}
