using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
    GenericWordDifference,
    SeriesQualifiedTitle,
    TitleVariant
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
    public const int MatcherVersion = 6;

    public const string FilenameTitleCode = "filename-title";
    public const string SeriesAliasTitleCode = "series-alias-title";

    public const string TitleVariantCode = "title-variant";
    public const string IdenticalTitleCode = "identical-title";
    public const string AlternativeTitleCode = "alternative-title";
    public const string SeriesQualifiedTitleCode = "series-qualified-title";
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

    private enum MovieTitleSource { Main, Alternative, Filename }

    private static readonly Regex MovieFolderPattern = new(
        @"^(?<title>.+) \((?:19|20)[0-9]{2}\) \{tmdb-[1-9][0-9]*\}$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

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

        var titles = MovieTitles(movie).ToArray();
        bool hasSeriesAlias = titles.Any(candidate => candidate.Source != MovieTitleSource.Filename
            && StartsWithSeries(candidate.Title, normalizedSeries));
        foreach ((string title, MovieTitleSource source, string reportedTitle) in titles)
        {
            if (!TrySpecialPart(title, normalizedSeries, out string moviePart)) continue;
            SpecialMovieMatchStrength? strength = Compare(
                normalizedEpisode, specialPart, episodeCarriesSeries,
                title, moviePart, StartsWithSeries(title, normalizedSeries));
            strength ??= MatchesTitleVariant(normalizedSeries, normalizedEpisode, title)
                ? SpecialMovieMatchStrength.TitleVariant : null;
            bool seriesAliasMatch = strength is null && !episodeCarriesSeries && hasSeriesAlias
                && MatchesAbbreviatedSeries(normalizedSeries, specialPart, title);
            if (seriesAliasMatch) strength = SpecialMovieMatchStrength.SeriesQualifiedTitle;
            if (strength is null) continue;

            SymlinkSuggestionReason titleReason = source == MovieTitleSource.Filename
                ? new SymlinkSuggestionReason(FilenameTitleCode,
                    $"The special matches the title '{reportedTitle}' from Radarr's movie folder and filename. No existing file or link was adopted or modified.")
                : seriesAliasMatch
                    ? new SymlinkSuggestionReason(SeriesAliasTitleCode,
                        $"The special title matches '{reportedTitle}' with a shortened series prefix; another title of the same Radarr movie identifies the full series '{series.Title}'.")
                    : TitleFinding(strength.Value, source == MovieTitleSource.Alternative, movie.Title, title, specialPart);
            List<SymlinkSuggestionReason> reasons = [titleReason];
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
    /// The defensible forms, most defensible first. Equality of the whole title wins over anything
    /// derived. A short episode title can also match the exact special part of a movie carrying the
    /// full series name. Only leading format labels are ignored for that comparison.
    /// </summary>
    private static SpecialMovieMatchStrength? Compare(
        string episodeTitle, string episodePart, bool episodeCarriesSeries,
        string movieTitle, string moviePart, bool movieCarriesSeries)
    {
        if (string.Equals(episodeTitle, movieTitle, StringComparison.Ordinal)) return SpecialMovieMatchStrength.IdenticalTitles;
        if (!episodeCarriesSeries && movieCarriesSeries)
        {
            string shortTitle = WithoutMovieLabel(episodePart);
            string qualifiedPart = WithoutMovieLabel(moviePart);
            return IsMeaningful(shortTitle) && shortTitle == qualifiedPart
                ? SpecialMovieMatchStrength.SeriesQualifiedTitle
                : null;
        }

        if (!episodeCarriesSeries || !movieCarriesSeries) return null;
        return TryGenericWordDifference(episodePart, moviePart) ? SpecialMovieMatchStrength.GenericWordDifference : null;
    }

    /// <summary>
    /// A shared, distinctive series phrase anchors both titles. Only the suffix after that phrase is
    /// compared loosely: articles and format labels may differ, subtitle words may move, but every
    /// identity word and every part number must agree. This never matches on the series name alone.
    /// </summary>
    private static bool MatchesTitleVariant(string series, string episode, string movie)
    {
        string[] seriesWords = series.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int length = seriesWords.Length; length >= 2; length--)
        {
            for (int start = 0; start + length <= seriesWords.Length; start++)
            {
                string[] phraseWords = seriesWords.Skip(start).Take(length).ToArray();
                if (phraseWords.Count(IsIdentityWord) < 2) continue;
                string phrase = string.Join(' ', phraseWords);
                int episodeStart = (" " + episode + " ").IndexOf(" " + phrase + " ", StringComparison.Ordinal);
                int movieStart = (" " + movie + " ").IndexOf(" " + phrase + " ", StringComparison.Ordinal);
                if (episodeStart < 0 || movieStart < 0) continue;

                // When the anchor starts the canonical series name, preceding words describe
                // additional content (for example a recap), not a localized series prefix.
                // Do not retry a shorter anchor that could conceal those identity words.
                if (start == 0 && (WithoutMovieLabel(episode[..episodeStart].Trim()).Length > 0
                    || WithoutMovieLabel(movie[..movieStart].Trim()).Length > 0)) return false;

                // Use the longest common series phrase, so a shorter phrase cannot hide a differing
                // identity word or sequel number inside a larger series name.
                return SameVariantSuffix(episode[(episodeStart + phrase.Length)..].Trim(),
                    movie[(movieStart + phrase.Length)..].Trim());
            }
        }
        return false;
    }

    private static bool IsIdentityWord(string word) =>
        !IsFiller(word) && word is not ("the" or "of" or "a" or "an" or "and");

    private static bool SameVariantSuffix(string episode, string movie)
    {
        var left = VariantParts(episode);
        var right = VariantParts(movie);
        return left.Words.Length >= 2
            && left.Words.SequenceEqual(right.Words)
            && left.Numbers.SequenceEqual(right.Numbers);
    }

    private static (string[] Words, string[] Numbers) VariantParts(string suffix)
    {
        string[] tokens = suffix.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<string> words = [];
        List<string> numbers = [];
        for (int index = 0; index < tokens.Length; index++)
        {
            string token = tokens[index];
            // Roman numerals are only numbers after an explicit part/format label, never arbitrary
            // title words (for example the pronoun "I"). Arabic numbers remain significant anywhere.
            if (index > 0 && tokens[index - 1] is "part" or "chapter" or "movie" or "film")
            {
                token = token switch
                {
                    "i" => "1", "ii" => "2", "iii" => "3", "iv" => "4", "v" => "5",
                    "vi" => "6", "vii" => "7", "viii" => "8", "ix" => "9", "x" => "10",
                    _ => token
                };
            }
            if (IsNumeric(token)) numbers.Add(token.TrimStart('0') is { Length: > 0 } number ? number : "0");
            else if (token is not ("the" or "of" or "a" or "an" or "and"
                or "movie" or "movies" or "film" or "films" or "part" or "chapter")) words.Add(token);
        }
        return (words.Order(StringComparer.Ordinal).ToArray(), numbers.ToArray());
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

        string[] tokens = builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int index = 0; index + 1 < tokens.Length; index++)
        {
            // A numbered German part label is the same format marker as "Part". Keep every
            // sequel number and leave "Teil" untouched when it is an ordinary title word.
            if (tokens[index] == "teil" && (IsNumeric(tokens[index + 1])
                || tokens[index + 1] is "i" or "ii" or "iii" or "iv" or "v"
                    or "vi" or "vii" or "viii" or "ix" or "x"))
                tokens[index] = "part";
        }

        return string.Join(' ', tokens);
    }

    private static IEnumerable<(string Title, MovieTitleSource Source, string ReportedTitle)> MovieTitles(RadarrMovieSnapshot movie)
    {
        string main = Normalize(movie.Title);
        if (main.Length > 0) yield return (main, MovieTitleSource.Main, movie.Title);
        foreach (string alternative in movie.AlternativeTitles)
        {
            string normalized = Normalize(alternative);
            if (normalized.Length > 0 && !string.Equals(normalized, main, StringComparison.Ordinal))
                yield return (normalized, MovieTitleSource.Alternative, alternative);
        }

        string? fileTitle = MovieFilenameTitle(movie);
        if (fileTitle is not null) yield return (Normalize(fileTitle), MovieTitleSource.Filename, fileTitle);
    }

    private static string? MovieFilenameTitle(RadarrMovieSnapshot movie)
    {
        // This is snapshot data only: do not access or traverse the filesystem during matching.
        // Restrict the fallback to a structured movie folder repeated in its actual filename.
        if (!movie.HasFile || movie.File.Id is null || string.IsNullOrWhiteSpace(movie.File.Path)) return null;
        string[] segments = movie.File.Path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return null;
        string folder = segments[^2];
        string filename = segments[^1];
        int extension = filename.LastIndexOf('.');
        if (extension <= 0) return null;
        string stem = filename[..extension];
        if (stem != folder && !stem.StartsWith(folder + " ", StringComparison.Ordinal)) return null;
        System.Text.RegularExpressions.Match match = MovieFolderPattern.Match(folder);
        if (!match.Success) return null;
        string title = match.Groups["title"].Value;
        string[] metadataNumbers = VariantParts(Normalize(movie.Title)).Numbers;
        if (metadataNumbers.Length > 0
            && !metadataNumbers.SequenceEqual(VariantParts(Normalize(title)).Numbers)) return null;
        return title;
    }

    private static bool MatchesAbbreviatedSeries(string series, string episodePart, string movieTitle)
    {
        string subtitle = WithoutMovieLabel(episodePart);
        if (subtitle.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(IsIdentityWord) < 2
            || !movieTitle.EndsWith(" " + subtitle, StringComparison.Ordinal)) return false;
        string prefix = movieTitle[..^(subtitle.Length + 1)];
        // An abbreviation must be an exact leading segment of the canonical series name,
        // corroborated by another metadata title of this very same Radarr movie.
        return prefix.Length > 0 && series.StartsWith(prefix + " ", StringComparison.Ordinal);
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

    /// <summary>Remove a leading format label without discarding identity words or sequel numbers.</summary>
    private static string WithoutMovieLabel(string title)
    {
        foreach (string label in new[] { "the movie", "movie" })
        {
            if (title == label) return string.Empty;
            if (title.StartsWith(label + " ", StringComparison.Ordinal)) return title[(label.Length + 1)..];
        }

        return title;
    }

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

        return leftover.Count == 1 && Array.IndexOf(GenericWords, leftover[0]) >= 0;
    }

    private static SymlinkSuggestionReason TitleFinding(
        SpecialMovieMatchStrength strength,
        bool isAlternative,
        string movieTitle,
        string normalizedMovieTitle,
        string specialPart) =>
        strength switch
        {
            SpecialMovieMatchStrength.TitleVariant => new SymlinkSuggestionReason(
                TitleVariantCode,
                $"The special and {(isAlternative ? "an alternative title" : "the title")} of '{movieTitle}' share distinctive series words, the same subtitle words and matching part numbers after normalizing format labels and Roman numerals."),
            SpecialMovieMatchStrength.SeriesQualifiedTitle => new SymlinkSuggestionReason(
                SeriesQualifiedTitleCode,
                $"The special title '{specialPart}' matches the special part of {(isAlternative ? "an alternative title" : "the title")} of '{movieTitle}' after removing leading movie labels; the movie title carries the full series name."),
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
