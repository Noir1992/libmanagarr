using LibManagarr.Api.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LibManagarr.Api.Clients;

public static class MediaFileLanguageHelper
{
    private static readonly IReadOnlyDictionary<string, string> LanguageAliases = BuildLanguageAliases();

    private static IReadOnlyDictionary<string, string> BuildLanguageAliases()
    {
        Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = "English",
            ["eng"] = "English",
            ["english"] = "English",
            ["de"] = "German",
            ["deu"] = "German",
            ["ger"] = "German",
            ["german"] = "German",
            ["deutsch"] = "German",
            ["fr"] = "French",
            ["fra"] = "French",
            ["fre"] = "French",
            ["french"] = "French",
            ["es"] = "Spanish",
            ["spa"] = "Spanish",
            ["spanish"] = "Spanish",
            ["it"] = "Italian",
            ["ita"] = "Italian",
            ["italian"] = "Italian",
            ["pt"] = "Portuguese",
            ["por"] = "Portuguese",
            ["portuguese"] = "Portuguese",
            ["ja"] = "Japanese",
            ["jpn"] = "Japanese",
            ["jap"] = "Japanese",
            ["jp"] = "Japanese",
            ["japanese"] = "Japanese",
            ["ko"] = "Korean",
            ["kor"] = "Korean",
            ["korean"] = "Korean",
            ["zh"] = "Chinese",
            ["chi"] = "Chinese",
            ["zho"] = "Chinese",
            ["chinese"] = "Chinese",
            ["ru"] = "Russian",
            ["rus"] = "Russian",
            ["russian"] = "Russian",
            ["nl"] = "Dutch",
            ["nld"] = "Dutch",
            ["dut"] = "Dutch",
            ["dutch"] = "Dutch",
            ["sv"] = "Swedish",
            ["swe"] = "Swedish",
            ["swedish"] = "Swedish",
            ["no"] = "Norwegian",
            ["nor"] = "Norwegian",
            ["norwegian"] = "Norwegian",
            ["da"] = "Danish",
            ["dan"] = "Danish",
            ["danish"] = "Danish",
            ["fi"] = "Finnish",
            ["fin"] = "Finnish",
            ["finnish"] = "Finnish",
            ["pl"] = "Polish",
            ["pol"] = "Polish",
            ["polish"] = "Polish",
            ["tr"] = "Turkish",
            ["tur"] = "Turkish",
            ["turkish"] = "Turkish",
            ["ar"] = "Arabic",
            ["ara"] = "Arabic",
            ["arabic"] = "Arabic",
            ["he"] = "Hebrew",
            ["heb"] = "Hebrew",
            ["hebrew"] = "Hebrew",
            ["uk"] = "Ukrainian",
            ["ukr"] = "Ukrainian",
            ["ukrainian"] = "Ukrainian",
            ["cs"] = "Czech",
            ["ces"] = "Czech",
            ["cze"] = "Czech",
            ["czech"] = "Czech",
            ["el"] = "Greek",
            ["ell"] = "Greek",
            ["gre"] = "Greek",
            ["greek"] = "Greek",
            ["alb"] = "Albanian",
            ["arm"] = "Armenian",
            ["baq"] = "Basque",
            ["bur"] = "Burmese",
            ["geo"] = "Georgian",
            ["ice"] = "Icelandic",
            ["mac"] = "Macedonian",
            ["mao"] = "Maori",
            ["may"] = "Malay",
            ["per"] = "Persian",
            ["rum"] = "Romanian",
            ["slo"] = "Slovak",
            ["tib"] = "Tibetan",
            ["wel"] = "Welsh"
        };

        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            if (string.IsNullOrEmpty(culture.Name) || culture.Name.Contains('-')) continue;
            aliases.TryAdd(culture.TwoLetterISOLanguageName, culture.EnglishName);
            aliases.TryAdd(culture.ThreeLetterISOLanguageName, culture.EnglishName);
            aliases.TryAdd(culture.EnglishName, culture.EnglishName);
            aliases.TryAdd(culture.NativeName, culture.EnglishName);
        }
        return aliases;
    }

    public static List<string> CollectLanguageNames(
        params IReadOnlyList<LanguageResource>?[] languageSources)
    {
        return languageSources
            .SelectMany((source) => source ?? [])
            .Select((language) => NormalizeLanguageLabel(language?.Name))
            .Where((name) => !string.IsNullOrWhiteSpace(name))
            .Select((name) => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<string> CollectLanguageNames(params string?[] languageSources)
    {
        return languageSources
            .SelectMany((source) => SplitLanguageValues(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> SplitLanguageValues(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            yield break;
        }

        string normalized = rawValue.Trim();
        string? directValue = NormalizeLanguageLabel(normalized);
        if (!string.IsNullOrWhiteSpace(directValue)
            && (directValue.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                || !HasLanguageSeparator(normalized)))
        {
            yield return directValue!;
            yield break;
        }

        foreach (string part in Regex.Split(normalized, @"\s*[/,;|+]\s*"))
        {
            string? value = NormalizeLanguageLabel(part);
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value!;
            }
        }
    }

    private static bool HasLanguageSeparator(string value)
    {
        return value.Contains('/')
               || value.Contains(',')
               || value.Contains(';')
               || value.Contains('|')
               || value.Contains('+');
    }

    private static string? NormalizeLanguageLabel(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        string value = rawValue.Trim();
        if (value.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            || value.Equals("n/a", StringComparison.OrdinalIgnoreCase)
            || value.Equals("na", StringComparison.OrdinalIgnoreCase)
            || value.Equals("none", StringComparison.OrdinalIgnoreCase)
            || value.Equals("-", StringComparison.OrdinalIgnoreCase))
        {
            return "Unknown";
        }

        if (LanguageAliases.TryGetValue(value, out string? alias))
        {
            return alias;
        }

        return value;
    }
}
