using System.Globalization;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public static class FileSizeFormatter
{
    public static string Format(long bytes, SizeUnitSystem unitSystem, AppLanguage language)
    {
        if (bytes <= 0) return "0 B";
        bool binary = unitSystem == SizeUnitSystem.Binary;
        int factor = binary ? 1024 : 1000;
        string[] units = binary ? ["B", "KiB", "MiB", "GiB", "TiB"] : ["B", "KB", "MB", "GB", "TB"];
        decimal value = bytes;
        int unit = 0;
        while (value >= factor && unit < units.Length - 1)
        {
            value /= factor;
            unit++;
        }

        CultureInfo culture = CultureInfo.GetCultureInfo(language switch
        {
            AppLanguage.De => "de-DE",
            AppLanguage.Fr => "fr-FR",
            _ => "en-US"
        });
        return $"{value.ToString("#,0.##", culture)} {units[unit]}";
    }
}
