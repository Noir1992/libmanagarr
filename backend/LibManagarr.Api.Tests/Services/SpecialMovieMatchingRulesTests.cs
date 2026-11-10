using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

/// <summary>
/// The plan's confirmed pair plus the counterexamples it names: another film of the same series,
/// another episode title, the series prefix alone, a divergent date and a remainder that says nothing.
/// Every case is a title pair, because the matcher is the only place a pairing is decided and it must
/// be decidable without a database, an Arr instance or the file system.
/// </summary>
public sealed class SpecialMovieMatchingRulesTests
{
    private const string SonarrInstance = "sonarr-1";
    private const string RadarrInstance = "radarr-1";
    private const string SeriesTitle = "Kabaneri of the Iron Fortress";
    private const string SpecialTitle = "Kabaneri of the Iron Fortress: The Battle of Unato";
    private const string UnatoFile = @"\\nas\radarr\kabaneri\unato.mkv";

    private static readonly DateTimeOffset Aired = new(2019, 12, 30, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan FilmRuntime = TimeSpan.FromMinutes(110);

    [Fact]
    public void Match_IdenticalFullTitle_IsTheConfirmedKabaneriPair()
    {
        SpecialMovieMatch match = Matched(Special(SpecialTitle), Film(SpecialTitle))!;

        Assert.Equal(SpecialMovieMatchStrength.IdenticalTitles, match.Strength);
        Assert.Equal(new SpecialMoviePair(RadarrInstance, 70, SonarrInstance, 12, 5), match.Pair);
        Assert.Equal(0, match.SeasonNumber);
        Assert.Equal(3, match.EpisodeNumber);
        Assert.Equal(900, match.RadarrFileId);
        Assert.Equal(UnatoFile, match.RadarrFilePath);
        Assert.True(match.MovieHasFile);
        Assert.Equal(SpecialMovieMatchingRules.IdenticalTitleCode, match.Reasons[0].Code);
        Assert.Contains(SpecialTitle, match.Reasons[0].Detail);
    }

    [Fact]
    public void Match_AccentsCaseAndPunctuationDoNotSplitTheSameTitle()
    {
        SpecialMovieMatch? match = Matched(
            Special("Kabaneri of the Iron Fortress — The Battle of Unato!"),
            Film("  KABANERI   of the iron fortress: the battle of UNATO  "));

        Assert.NotNull(match);
        Assert.Equal(SpecialMovieMatchStrength.IdenticalTitles, match!.Strength);
    }

    [Fact]
    public void Match_EpisodeTitleEqualsAnAlternativeTitle_ReportsTheAlternativeTitle()
    {
        RadarrMovieSnapshot movie = new()
        {
            MovieId = 70,
            Title = "Kabaneri of the Iron Fortress: The Battle of Unato (Movie Edition)",
            AlternativeTitles = ["Kabaneri of the Iron Fortress – Compilation Film", SpecialTitle],
            HasFile = true,
            AirDateUtc = Aired,
            File = DownloadedFile()
        };

        SpecialMovieMatch match = Matched(Special(SpecialTitle), movie)!;

        // The main title says nothing defensible, so the match rests on the alternative title, and the
        // user is told exactly that instead of being shown a plain "same title".
        Assert.Equal(SpecialMovieMatchStrength.IdenticalTitles, match.Strength);
        Assert.Equal(SpecialMovieMatchingRules.AlternativeTitleCode, match.Reasons[0].Code);
    }

    [Fact]
    public void Match_AnotherFilmOfTheSameSeries_IsNotAPair()
    {
        // Radarr carries a different Kabaneri film. The shared series name is exactly the signal the
        // plan refuses to pair on, so this stays a non-match instead of a weak suggestion.
        Assert.Null(Matched(Special(SpecialTitle), Film("Kabaneri of the Iron Fortress: Raging Break")));
    }

    [Fact]
    public void Match_AnotherEpisodeTitleOfTheSeries_IsNotAPair()
    {
        Assert.Null(Matched(Special("Kabaneri of the Iron Fortress: Light That Crosses His Path"), Film(SpecialTitle)));
    }

    [Fact]
    public void Match_SeriesNameAloneOnEitherSide_IsNotAPair()
    {
        SonarrSeriesSnapshot series = Series();

        // A movie that is only the series name, and a special that adds nothing to it, leave no special
        // identity to point at — even when both sides carry the very same title.
        Assert.Null(Matched(Special(SeriesTitle), Film(SeriesTitle), series));
        Assert.Null(Matched(Special(string.Empty), Film(SpecialTitle), series));
    }

    [Fact]
    public void Match_FillerOrNumericRemainder_IsNotAPair()
    {
        Assert.Null(Matched(Special("Kabaneri of the Iron Fortress: Movie"), Film("Kabaneri of the Iron Fortress")));
        Assert.Null(Matched(Special("Kabaneri of the Iron Fortress: 2"), Film("Kabaneri of the Iron Fortress: 2")));
    }

    [Fact]
    public void Match_SeriesNameOnBothSidesWithTheSameAddition_IsTheSameTitleNotAPrefixMatch()
    {
        SonarrSeriesSnapshot series = Series("One Piece");

        SpecialMovieMatch match = Matched(
            Special("One Piece: Episode of Storm King"),
            Film("One Piece - Episode of Storm King"),
            series)!;

        // Both sides say the series and the same addition, which makes the whole titles equal. There is
        // no weaker "only the prefix matched" state to report, so the interface never has to render one.
        Assert.Equal(SpecialMovieMatchStrength.IdenticalTitles, match.Strength);
        Assert.Equal(SpecialMovieMatchingRules.IdenticalTitleCode, match.Reasons[0].Code);
    }

    [Fact]
    public void Match_SameAdditionOnTheMovieSideOnly_IsNotAPair()
    {
        // "The Party" is an unrelated film here. Because only the episode carries the series name, the
        // shared words cannot show that the movie is the special of this series.
        SonarrSeriesSnapshot series = Series("Money Heist");

        Assert.Null(Matched(Special("Money Heist: The Party"), Film("The Party"), series));
    }

    [Fact]
    public void Match_OneGenericWordApart_IsPairedAndSaysWhichWordsDiffer()
    {
        SonarrSeriesSnapshot series = Series("Attack on Titan");

        SpecialMovieMatch match = Matched(
            Special("Attack on Titan: Lost Girls"),
            Film("Attack on Titan: Lost Girls Movie"),
            series)!;

        Assert.Equal(SpecialMovieMatchStrength.GenericWordDifference, match.Strength);
        Assert.Equal(SpecialMovieMatchingRules.GenericWordCode, match.Reasons[0].Code);
    }

    [Fact]
    public void Match_BothSidesCarryTheSeriesButDifferOtherwise_IsNotAPair()
    {
        SonarrSeriesSnapshot series = Series("Attack on Titan");

        Assert.Null(Matched(
            Special("Attack on Titan: Lost Girls"),
            Film("Attack on Titan: Lost Girls Chapter 2"),
            series));
    }

    [Fact]
    public void Match_DatesApartStillPairAndTheDistanceIsReportedAsAFinding()
    {
        SonarrEpisodeSnapshot episode = Special(SpecialTitle);
        episode.AirDateUtc = new DateTimeOffset(2019, 12, 30, 0, 0, 0, TimeSpan.Zero);
        RadarrMovieSnapshot movie = new()
        {
            MovieId = 70,
            Title = SpecialTitle,
            HasFile = true,
            AirDateUtc = new DateTimeOffset(2022, 3, 14, 0, 0, 0, TimeSpan.Zero),
            File = DownloadedFile()
        };

        SpecialMovieMatch match = Matched(episode, movie)!;

        SymlinkSuggestionReason finding = Assert.Single(
            match.Reasons, reason => reason.Code == SpecialMovieMatchingRules.ReleaseYearDiffersCode);
        Assert.Contains("3 years apart", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("not a refusal", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Match_SameYearIsReportedAsAMatchingYear()
    {
        SonarrEpisodeSnapshot episode = Special(SpecialTitle);
        episode.AirDateUtc = new DateTimeOffset(2016, 12, 24, 0, 0, 0, TimeSpan.Zero);
        RadarrMovieSnapshot movie = new()
        {
            MovieId = 70,
            Title = SpecialTitle,
            HasFile = true,
            AirDateUtc = new DateTimeOffset(2016, 12, 28, 0, 0, 0, TimeSpan.Zero),
            File = DownloadedFile()
        };

        SpecialMovieMatch match = Matched(episode, movie)!;

        SymlinkSuggestionReason finding = Assert.Single(
            match.Reasons, reason => reason.Code == SpecialMovieMatchingRules.ReleaseYearMatchCode);
        Assert.Contains("2016-12-24", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("2016-12-28", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Match_MissingDatesAreReportedAndDoNotRefuseThePair()
    {
        SonarrEpisodeSnapshot episode = Special(SpecialTitle);
        episode.AirDateUtc = null;
        RadarrMovieSnapshot movie = new()
        {
            MovieId = 70,
            Title = SpecialTitle,
            HasFile = true,
            File = DownloadedFile()
        };

        SpecialMovieMatch? match = Matched(episode, movie);

        Assert.NotNull(match);
        Assert.Contains(SpecialMovieMatchingRules.ReleaseDateUnknownCode, match!.Reasons.Select(reason => reason.Code));
    }

    [Fact]
    public void Match_EqualRuntimesAreReportedAsAFinding()
    {
        SonarrEpisodeSnapshot episode = Special(SpecialTitle);
        episode.File.Duration = TimeSpan.FromMinutes(112);

        SpecialMovieMatch match = Matched(episode, Film(SpecialTitle, FilmRuntime))!;

        SymlinkSuggestionReason finding = Assert.Single(
            match.Reasons, reason => reason.Code == SpecialMovieMatchingRules.RuntimeMatchCode);
        Assert.Contains("112 minutes", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Match_DifferentRuntimesAreAFindingAndNotARefusal()
    {
        SonarrEpisodeSnapshot episode = Special(SpecialTitle);
        episode.File.Duration = TimeSpan.FromMinutes(45);

        SpecialMovieMatch match = Matched(episode, Film(SpecialTitle, FilmRuntime))!;

        SymlinkSuggestionReason finding = Assert.Single(
            match.Reasons, reason => reason.Code == SpecialMovieMatchingRules.RuntimeDiffersCode);
        Assert.Contains("45 minutes", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("110 minutes", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Match_UnknownRuntimeIsReportedInsteadOfGuessed()
    {
        SpecialMovieMatch match = Matched(Special(SpecialTitle), Film(SpecialTitle))!;

        Assert.Contains(SpecialMovieMatchingRules.RuntimeUnknownCode, match.Reasons.Select(reason => reason.Code));
    }

    [Fact]
    public void Match_MovieWithoutAFile_IsPairedAsWaitingAndWithoutAFileIdentity()
    {
        RadarrMovieSnapshot movie = new()
        {
            MovieId = 70,
            Title = SpecialTitle,
            HasFile = false,
            AirDateUtc = Aired
        };

        SpecialMovieMatch match = Matched(Special(SpecialTitle), movie)!;

        Assert.False(match.MovieHasFile);
        Assert.Null(match.RadarrFileId);
        Assert.Null(match.RadarrFilePath);
    }

    [Fact]
    public void Match_FileEntryWithoutTheDownloadedFlag_IsNotTreatedAsDownloadable()
    {
        // Radarr can keep a file entry on a movie it no longer counts as downloaded. The flag decides,
        // because a suggestion that offers that file would point at something the user cannot watch.
        RadarrMovieSnapshot movie = new()
        {
            MovieId = 70,
            Title = SpecialTitle,
            HasFile = false,
            AirDateUtc = Aired,
            File = DownloadedFile()
        };
        Assert.NotNull(movie.File.Id);

        Assert.False(Matched(Special(SpecialTitle), movie)!.MovieHasFile);
    }

    [Fact]
    public void Match_PairCarriesBothInstancesSoTheSameMovieNumberInTwoInstancesStaysApart()
    {
        SonarrEpisodeSnapshot episode = Special(SpecialTitle);

        SpecialMovieMatch first = SpecialMovieMatchingRules.Match(
            SonarrInstance, Series(), episode, 0, "radarr-1", Film(SpecialTitle))!;
        SpecialMovieMatch second = SpecialMovieMatchingRules.Match(
            SonarrInstance, Series(), episode, 0, "radarr-2", Film(SpecialTitle))!;

        Assert.NotEqual(first.Pair, second.Pair);
    }

    [Theory]
    [InlineData("Kabaneri: Unato", "kabaneri unato")]
    [InlineData("  Kàbaneri — Unato!  ", "kabaneri unato")]
    [InlineData("Unato", "unato")]
    public void Normalize_FoldsCaseAccentsAndPunctuation(string title, string expected) =>
        Assert.Equal(expected, SpecialMovieMatchingRules.Normalize(title));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void Normalize_NothingMeaningfulIsAnEmptyString(string? title) =>
        Assert.Equal(string.Empty, SpecialMovieMatchingRules.Normalize(title));

    private static SonarrSeriesSnapshot Series(string title = SeriesTitle) => new()
    {
        SeriesId = 12,
        Title = title
    };

    private static SonarrEpisodeSnapshot Special(string title) => new()
    {
        EpisodeId = 5,
        EpisodeNumber = 3,
        Title = title,
        HasFile = false,
        AirDateUtc = Aired
    };

    private static RadarrMovieSnapshot Film(string title, TimeSpan? runtime = null) => new()
    {
        MovieId = 70,
        Title = title,
        HasFile = true,
        AirDateUtc = Aired,
        File = DownloadedFile(runtime)
    };

    private static MediaFileSnapshot DownloadedFile(TimeSpan? runtime = null) => new()
    {
        Id = 900,
        Path = UnatoFile,
        Duration = runtime
    };

    private static SpecialMovieMatch? Matched(SonarrEpisodeSnapshot episode, RadarrMovieSnapshot movie) =>
        Matched(episode, movie, Series());

    private static SpecialMovieMatch? Matched(
        SonarrEpisodeSnapshot episode, RadarrMovieSnapshot movie, SonarrSeriesSnapshot series) =>
        SpecialMovieMatchingRules.Match(SonarrInstance, series, episode, 0, RadarrInstance, movie);
}
