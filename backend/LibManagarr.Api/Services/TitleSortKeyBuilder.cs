using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public static class TitleSortKeyBuilder
{
    public static string Build(string title, TitleSortingSettings settings)
    {
        if (!settings.Enabled || settings.IgnoredLeadingWords.Count == 0) return title;

        int start = 0;
        while (start < title.Length && IsWhiteSpace(title[start])) start++;
        int end = start;
        while (end < title.Length && !IsWhiteSpace(title[end])) end++;
        int rest = end;
        while (rest < title.Length && IsWhiteSpace(title[rest])) rest++;
        if (start == end || rest == title.Length) return title;

        string firstWord = UnicodeSimpleCaseFold.Fold(title[start..end]);
        return settings.IgnoredLeadingWords.Any(word => UnicodeSimpleCaseFold.Fold(word) == firstWord)
            ? title[rest..]
            : title;
    }

    public static bool IsWhiteSpace(char value) => value is >= '\u0009' and <= '\u000D'
        or '\u0020' or '\u0085' or '\u00A0' or '\u1680'
        or >= '\u2000' and <= '\u200A' or '\u2028' or '\u2029'
        or '\u202F' or '\u205F' or '\u3000';

    public static string TrimWhiteSpace(string value)
    {
        int start = 0;
        while (start < value.Length && IsWhiteSpace(value[start])) start++;
        int end = value.Length;
        while (end > start && IsWhiteSpace(value[end - 1])) end--;
        return value[start..end];
    }
}
