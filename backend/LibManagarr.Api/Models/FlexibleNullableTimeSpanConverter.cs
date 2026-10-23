using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibManagarr.Api.Models;

public sealed class FlexibleNullableTimeSpanConverter : JsonConverter<TimeSpan?>
{
    public override TimeSpan? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.Number => ParseNumber(ref reader),
            JsonTokenType.String => ParseString(reader.GetString()),
            _ => null
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        TimeSpan? value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.ToString("c", CultureInfo.InvariantCulture));
    }

    private static TimeSpan? ParseNumber(ref Utf8JsonReader reader)
    {
        if (!reader.TryGetDouble(out double numericValue) || double.IsNaN(numericValue) || double.IsInfinity(numericValue))
        {
            return null;
        }

        if (numericValue < 0)
        {
            return null;
        }

        // Sonarr/Radarr can expose runtime as a bare number; interpret as seconds.
        return TimeSpan.FromSeconds(numericValue);
    }

    private static TimeSpan? ParseString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out TimeSpan parsedTimeSpan))
        {
            return parsedTimeSpan;
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double plainNumeric))
        {
            return plainNumeric >= 0 ? TimeSpan.FromSeconds(plainNumeric) : null;
        }

        const string minutesSuffix = "min";
        if (trimmed.EndsWith(minutesSuffix, StringComparison.OrdinalIgnoreCase))
        {
            string minuteText = trimmed[..^minutesSuffix.Length].Trim();
            if (double.TryParse(minuteText, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes) && minutes >= 0)
            {
                return TimeSpan.FromMinutes(minutes);
            }
        }

        return null;
    }
}
