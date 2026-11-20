using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public static class MediaTitleResolver
{
    public static string Resolve(string sourceTitle, MediaTitles titles, AppLanguage language) =>
        new[] { Translation(titles, language.ToString().ToLowerInvariant()), Translation(titles, "en"), titles.OriginalTitle, sourceTitle }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    public static IReadOnlyList<string> Candidates(string sourceTitle, MediaTitles titles, AppLanguage language,
        IEnumerable<string>? alternatives = null) =>
        new[] { Translation(titles, language.ToString().ToLowerInvariant()), Translation(titles, "en"), titles.OriginalTitle, sourceTitle }
            .Concat(alternatives ?? []).Concat(titles.Translations.Values.Select(value => value.Title))
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string? Translation(MediaTitles titles, string language) =>
        titles.Translations.GetValueOrDefault(language)?.Title;
}
