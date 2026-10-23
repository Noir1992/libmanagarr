using System.Globalization;

namespace LibManagarr.Api.Services;

public static class MediaFileValueNormalizer
{
    public const string Unknown = "Unknown";

    public static string Value(string? raw)
    {
        string value = raw?.Trim() ?? string.Empty;
        return value.ToLowerInvariant() is "" or "unknown" or "n/a" or "na" or "none" or "-"
            ? Unknown : value;
    }

    public static string Container(string? raw)
    {
        string value = Value(raw);
        return value.ToLowerInvariant() switch
        {
            "matroska" => "MKV",
            "mpeg-4" or "mpeg4" => "MP4",
            "quicktime / mov" => "MOV",
            _ when value.StartsWith('.') => value.TrimStart('.').ToUpperInvariant(),
            _ when value.Length <= 5 => value.ToUpperInvariant(),
            _ => value
        };
    }

    public static string Resolution(string? raw)
    {
        string value = raw?.Trim().ToLowerInvariant() ?? string.Empty;
        if (value == "sd") return "SD";
        if (value is "4k" or "uhd" or "8k") return "4k";

        string[] dimensions = value.Replace('×', 'x').Split('x', StringSplitOptions.TrimEntries);
        double equivalentHeight;
        if (dimensions.Length == 2)
        {
            if (!int.TryParse(dimensions[0], NumberStyles.None, CultureInfo.InvariantCulture, out int width)
                || !int.TryParse(dimensions[1], NumberStyles.None, CultureInfo.InvariantCulture, out int height)
                || width <= 0 || height <= 0)
                return "unknown";
            // Width preserves the category for widescreen films with cropped black bars.
            equivalentHeight = Math.Max(height, width * 9d / 16d);
        }
        else
        {
            string heightValue = value.EndsWith('p') || value.EndsWith('i') ? value[..^1] : value;
            if (!int.TryParse(heightValue, NumberStyles.None, CultureInfo.InvariantCulture, out int height) || height <= 0)
                return "unknown";
            equivalentHeight = height;
        }

        if (equivalentHeight < 440) return "SD";
        int[] categories = [480, 576, 720, 1080, 1440, 2160];
        int category = categories.MinBy(candidate => Math.Abs(candidate - equivalentHeight));
        if (category == 2160) return "4k";
        return category.ToString(CultureInfo.InvariantCulture) + "p";
    }
}
