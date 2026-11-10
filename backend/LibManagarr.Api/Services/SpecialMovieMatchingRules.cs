using System.Globalization;
using System.Text;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>
/// How defensible a pairing looks from the titles alone. The levels are ordered by strength, so the
/// alternatives of a special can be listed without inventing a percentage. A pair that shares only the
/// series name never reaches this enum at all — it is refused before there is a strength to report.
/// </summary>
public enum SpecialMovieMatchStrength
{
    IdenticalTitles,
    GenericWordDifference
}

/// <summary>
/// One special episode against one Radarr movie, as far as the titles and dates in the snapshots
/// reach. Nothing here knows the file system or the register — it only says whether this pairing is
/// defensible enough to ask the user about.
/// </summary>
public sealed record SpecialMovieMatch(
    SpecialMoviePair Pair,
    int SeasonNumber,
    int EpisodeNumber,
    int? RadarrFileId,
    string? RadarrFilePath,
    bool MovieHasFile,
    SpecialMovieMatchStrength Strength,
    IReadOnlyList<SymlinkSuggestionReason> Reasons);

/// <summary>
/// Title matching between a Sonarr special and a Radarr movie. There is no shared key between the
/// two (Sonarr speaks episodes and TVDB, Radarr speaks movies and TMDB), so the only honest signal is
/// the title, and the rules below exist to keep the one signal that is weak — the series name — from
/// deciding anything on its own. A date or a runtime never rejects a pairing; they only add findings
/// the user sees.
/// </summary>
public static class SpecialMovieMatchingRules
{
    public const int MatcherVersion = 1;

    public const string IdenticalTitleCode = "identical-title";
    public const string AlternativeTitleCode = "alternative-title";
    public const string GenericWordCode = "generic-word-difference";
    public const string ReleaseYearMatchCode = "release-year-match";
    public const string ReleaseYearDiffersCode = "release-year-differs";
    public const string ReleaseDateUnknownCode = "release-date-unknown";
    public const string RuntimeMatchCode = "runtime-match";
    public const string RuntimeDiffersCode = "runtime-differs";
    public const string RuntimeUnknownCode = "runtime-unknown";
    public const string EpisodeHasFileCode = "episode-has-file";

    /// <summary>
    /// Words that carry no identity: a special whose whole title is one of these is the series prefix
    /// wearing a hat, which is exactly the case the plan refuses to match on.
    /// </summary>
    private static readonly string[] GenericWords =
    [
        "movie", "movies", "film", "films", "special", "specials", "ova", "oad",
        "episode", "episodes", "ep", "extra", "extras", "part", "chapter"
    ];

    private const int RuntimeToleranceMinutes = 5;

    /// <summary>Null when the pairing is not defensible; the caller does not get a half-match.</summary>
    public static SpecialMovieMatch? Match(
        string sonarrInstanceId,
        SonarrSeriesSnapshot series,
        SonarrEpisodeSnapshot episode,
        int seasonNumber,
        string radarrInstanceId,
        RadarrMovieSnapshot movie)
    {
        string normalizedSeries = Normalize(series.Title);
        string normalizedEpisode = Normalize(episode.Title);
        if (normalizedEpisode.Length == 0) return null;
        if (!TrySpecialPart(normalizedEpisode, normalizedSeries, out string specialPart)) return null;
        bool episodeCarriesSeries = StartsWithSeries(normalizedEpisode, normalizedSeries);

        foreach ((string title, bool isAlternative) in MovieTitles(movie))
        {
            if (!TrySpecialPart(title, normalizedSeries, out string moviePart)) continue;
            SpecialMovieMatchStrength? strength = Compare(
                normalizedEpisode, specialPart, episodeCarriesSeries,
                title, moviePart, StartsWithSeries(title, normalizedSeries));
            if (strength is null) continue;

            List<SymlinkSuggestionReason> reasons =
            [
                TitleFinding(strength.Value, isAlternative, movie.Title, title, specialPart)
            ];
            AppendDateFindings(reasons, episode.AirDateUtc, movie.AirDateUtc);
            AppendRuntimeFindings(reasons, episode.File.Duration, movie.File.Duration);

            return new SpecialMovieMatch(
                new SpecialMoviePair(radarrInstanceId, movie.MovieId, sonarrInstanceId, series.SeriesId, episode.EpisodeId),
                seasonNumber,
                episode.EpisodeNumber,
                movie.File.Id,
                movie.File.Path,
                movie.HasFile && movie.File.Id is not null,
                strength.Value,
                reasons);
        }

        return null;
    }

    /// <summary>
    /// The two defensible forms, most defensible first. Equality of the whole title wins over anything
    /// derived; a shared series name only counts when both sides also say something besides it — and
    /// when that something is the same on both sides, the whole titles are already equal above.
    /// </summary>
    private static SpecialMovieMatchStrength? Compare(
        string episodeTitle, string episodePart, bool episodeCarriesSeries,
        string movieTitle, string moviePart, bool movieCarriesSeries)
    {
        if (string.Equals(episodeTitle, movieTitle, StringComparison.Ordinal)) return SpecialMovieMatchStrength.IdenticalTitles;
        if (!episodeCarriesSeries || !movieCarriesSeries) return null;
        return TryGenericWordDifference(episodePart, moviePart) ? SpecialMovieMatchStrength.GenericWordDifference : null;
    }

    /// <summary>
    /// Case-folded, accent-stripped, punctuation-free form of a title, so that "Kabaneri: Unato" and
    /// "kabaneri unato" are one string while "Unato" and "Unato 2" stay two.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        string decomposed = UnicodeSimpleCaseFold.Fold(value).Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(decomposed.Length);
        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character)) builder.Append(character);
            else builder.Append(' ');
        }

        return string.Join(' ', builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static IEnumerable<(string Title, bool IsAlternative)> MovieTitles(RadarrMovieSnapshot movie)
    {
        string main = Normalize(movie.Title);
        if (main.Length > 0) yield return (main, false);
        foreach (string alternative in movie.AlternativeTitles)
        {
            string normalized = Normalize(alternative);
            if (normalized.Length > 0 && !string.Equals(normalized, main, StringComparison.Ordinal))
            {
                yield return (normalized, true);
            }
        }
    }

    private static bool StartsWithSeries(string normalizedTitle, string normalizedSeries) =>
        normalizedSeries.Length > 0
        && (normalizedTitle == normalizedSeries || normalizedTitle.StartsWith(normalizedSeries + " ", StringComparison.Ordinal));

    /// <summary>
    /// The part of a title that says which special this is. A title that is only the series name, or
    /// only filler around it, gives nothing to match on and is refused here rather than later.
    /// </summary>
    private static bool TrySpecialPart(string normalizedTitle, string normalizedSeries, out string part)
    {
        part = StartsWithSeries(normalizedTitle, normalizedSeries)
            ? TitleSortKeyBuilder.TrimWhiteSpace(normalizedTitle[normalizedSeries.Length..])
            : normalizedTitle;
        return IsMeaningful(part);
    }

    private static bool IsMeaningful(string part) =>
        part.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(token => !IsFiller(token));

    private static bool IsFiller(string token) => IsNumeric(token) || Array.IndexOf(GenericWords, token) >= 0;

    private static bool IsNumeric(string token) => token.All(character => char.IsAsciiDigit(character));

    /// <summary>True when the two titles differ by exactly one filler word, whichever side carries it.</summary>
    private static bool TryGenericWordDifference(string left, string right) =>
        HasOneExtraFiller(left, right) || HasOneExtraFiller(right, left);

    private static bool HasOneExtraFiller(string shorter, string longer)
    {
        string[] kept = shorter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] given = longer.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (given.Length != kept.Length + 1) return false;
        List<string> leftover = [.. given];
        foreach (string token in kept)
        {
            leftover.Remove(token);
        }

        return leftover.Count == 1 && IsFiller(leftover[0]);
    }

    private static SymlinkSuggestionReason TitleFinding(
        SpecialMovieMatchStrength strength,
        bool isAlternative,
        string movieTitle,
        string normalizedMovieTitle,
        string specialPart) =>
        strength switch
        {
            SpecialMovieMatchStrength.GenericWordDifference => new SymlinkSuggestionReason(
                GenericWordCode,
                $"'{specialPart}' and '{normalizedMovieTitle}' differ only by a generic word."),
            _ => isAlternative
                ? new SymlinkSuggestionReason(AlternativeTitleCode, $"The episode title equals an alternative title of '{movieTitle}'.")
                : new SymlinkSuggestionReason(IdenticalTitleCode, $"The special and the movie have the same title: '{movieTitle}'.")
        };

    private static void AppendDateFindings(List<SymlinkSuggestionReason> reasons, DateTimeOffset? episodeAirDate, DateTimeOffset? movieReleaseDate)
    {
        if (episodeAirDate is null || movieReleaseDate is null)
        {
            string missing = episodeAirDate is null && movieReleaseDate is null
                ? "Neither the special nor the movie has a date, so the dates could not be compared."
                : "Only one of the two has a date, so the dates could not be compared.";
            reasons.Add(new SymlinkSuggestionReason(ReleaseDateUnknownCode, missing));
            return;
        }

        string episodeDate = episodeAirDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string movieDate = movieReleaseDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        int yearDistance = Math.Abs(episodeAirDate.Value.Year - movieReleaseDate.Value.Year);
        if (yearDistance == 0)
        {
            reasons.Add(new SymlinkSuggestionReason(
                ReleaseYearMatchCode, $"The special aired {episodeDate} and the movie release is dated {movieDate}."));
            return;
        }

        string span = yearDistance == 1 ? "year" : "years";
        reasons.Add(new SymlinkSuggestionReason(
            ReleaseYearDiffersCode,
            $"The special aired {episodeDate}, the movie release is dated {movieDate} — {yearDistance} {span} apart. A date difference is a caution, not a refusal."));
    }

    private static void AppendRuntimeFindings(List<SymlinkSuggestionReason> reasons, TimeSpan? episodeRuntime, TimeSpan? movieRuntime)
    {
        if (episodeRuntime is null || movieRuntime is null || episodeRuntime.Value <= TimeSpan.Zero || movieRuntime.Value <= TimeSpan.Zero)
        {
            reasons.Add(new SymlinkSuggestionReason(RuntimeUnknownCode, "The runtime of the special or of the movie file is not known."));
            return;
        }

        int episodeMinutes = (int)Math.Round(episodeRuntime.Value.TotalMinutes);
        int movieMinutes = (int)Math.Round(movieRuntime.Value.TotalMinutes);
        if (Math.Abs(movieMinutes - episodeMinutes) <= RuntimeToleranceMinutes)
        {
            reasons.Add(new SymlinkSuggestionReason(RuntimeMatchCode, $"Both run about {episodeMinutes} minutes."));
            return;
        }

        reasons.Add(new SymlinkSuggestionReason(
            RuntimeDiffersCode,
            $"The special runs {episodeMinutes} minutes, the movie file {movieMinutes} minutes."));
    }
}
