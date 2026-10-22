using System.Text;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public static class TitleSortingSettingsRules
{
    public static TitleSortingSettings Normalize(TitleSortingSettings? settings)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> words = [];
        foreach (string? value in settings?.IgnoredLeadingWords ?? [])
        {
            if (value is null) continue;
            string word;
            try { word = TitleSortKeyBuilder.TrimWhiteSpace(value).Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { continue; }
            if (word.Length > 0 && seen.Add(UnicodeSimpleCaseFold.Fold(word))) words.Add(word);
        }
        return new TitleSortingSettings { Enabled = words.Count > 0, IgnoredLeadingWords = words };
    }

    public static Dictionary<string, string[]> Validate(TitleSortingSettings? settings)
    {
        Dictionary<string, string[]> errors = [];
        if (settings is null)
        {
            errors["titleSorting"] = ["Title sorting settings are required."];
            return errors;
        }
        if (settings.IgnoredLeadingWords is null)
        {
            errors["titleSorting.ignoredLeadingWords"] = ["A word list is required."];
            return errors;
        }
        if (settings.IgnoredLeadingWords.Count > 100)
            errors["titleSorting.ignoredLeadingWords"] = ["At most 100 words are allowed."];
        for (int index = 0; index < settings.IgnoredLeadingWords.Count; index++)
        {
            string? value = settings.IgnoredLeadingWords[index];
            string key = $"titleSorting.ignoredLeadingWords[{index}]";
            if (value is null)
            {
                errors[key] = ["A word is required."];
                continue;
            }
            string word;
            try { word = TitleSortKeyBuilder.TrimWhiteSpace(value).Normalize(NormalizationForm.FormC); }
            catch (ArgumentException)
            {
                errors[key] = ["A word must contain valid Unicode characters."];
                continue;
            }
            int length = word.EnumerateRunes().Count();
            if (length is < 1 or > 64)
                errors[key] = ["A word must contain between 1 and 64 Unicode codepoints."];
            else if (word.Any(character => TitleSortKeyBuilder.IsWhiteSpace(character) || char.IsControl(character)))
                errors[key] = ["Use a single word without whitespace or control characters."];
        }
        return errors;
    }
}
