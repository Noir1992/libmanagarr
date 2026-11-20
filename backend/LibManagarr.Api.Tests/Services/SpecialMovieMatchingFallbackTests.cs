using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class SpecialMovieMatchingFallbackTests
{
    public static TheoryData<string, string, string, string> MissingPairs => new()
    {
        { "Digimon: Digital Monsters", "Digimon Adventure tri. Part 1: Reunion", "Digimon Adventure tri. Chapter 1: Reunion", "Digimon Adventure tri. Part 1 Reunion" },
        { "Digimon: Digital Monsters", "Digimon Adventure tri. Part 2: Determination", "Digimon Adventure tri. Chapter 2: Determination", "Digimon Adventure tri. Part 2 Determination" },
        { "Digimon: Digital Monsters", "Digimon Adventure tri. Part 3: Confession", "Digimon Adventure tri. Chapter 3: Confession", "Digimon Adventure tri. Part 3 Confession" },
        { "Digimon: Digital Monsters", "Digimon Adventure tri. Part 4: Loss", "Digimon Adventure tri. Chapter 4: Lost", "Digimon Adventure tri. Part 4 Loss" },
        { "Digimon: Digital Monsters", "Digimon Adventure tri. Part 5: Coexistence", "Digimon Adventure tri. Chapter 5: Coexistence", "Digimon Adventure tri. Part 5 Coexistence" },
        { "Digimon: Digital Monsters", "Digimon Adventure tri. Part 6: Future", "Digimon Adventure tri. Chapter 6: Our Future", "Digimon Adventure tri. Part 6 Future" },
        { "Eden of the East", "Movie I: The King of Eden", "Eden of the East - Der König von Eden", "Eden of the East Movie I The King of Eden" },
        { "Eden of the East", "Movie II: Paradise Lost", "Eden of the East - Das verlorene Paradies", "Eden of the East Movie II Paradise Lost" },
        { "Haikyu!!", "Haikyu!! Movie: The Dumpster Battle", "Haikyu!! Das Play-off der Müllhalde", "HAIKYU!! The Dumpster Battle" },
        { "Steins;Gate", "Steins;Gate the Movie: Load Region of Déjà Vu", "Steins;Gate: The Movie", "Steins;Gate The Movie Load Region of Deja Vu" }
    };

    [Theory]
    [MemberData(nameof(MissingPairs))]
    public void Match_ReportedRadarrFilenameSuppliesTheMissingTitleVariant(
        string series, string episode, string movie, string fileTitle)
    {
        SpecialMovieMatch? match = Match(series, episode, Movie(movie, fileTitle));
        Assert.NotNull(match);
        Assert.Equal("filename-title", match.Reasons[0].Code);
        Assert.Contains(fileTitle, match.Reasons[0].Detail);
    }

    [Fact]
    public void Match_ShortSeriesNameRequiresCorroborationFromAnotherTitleOfTheSameMovie()
    {
        RadarrMovieSnapshot movie = new()
        {
            MovieId = 198, Title = "K: Missing Kings - The Movie",
            AlternativeTitles = ["K Missing Kings", "K-Project Movie"],
            File = new MediaFileSnapshot { Id = 163 }
        };
        SpecialMovieMatch? match = Match("K Project", "Missing Kings", movie);
        Assert.NotNull(match);
        Assert.Equal("series-alias-title", match.Reasons[0].Code);
        Assert.Null(Match("Other Project", "Missing Kings", movie));
    }

    [Theory]
    [InlineData("K Missing Kings")]
    [InlineData("K-Project Movie")]
    [InlineData("Making of K Project Missing Kings")]
    public void Match_AShortSeriesNameOrGenericSeriesAliasAloneCannotIdentifyTheMovie(string onlyAlias)
    {
        Assert.Null(Match("K Project", "Missing Kings", new RadarrMovieSnapshot
        {
            MovieId = 198, Title = "K: Missing Kings - The Movie", AlternativeTitles = [onlyAlias],
            File = new MediaFileSnapshot { Id = 163 }
        }));
    }

    [Theory]
    [InlineData("Other Movie (2017) {tmdb-422347} [Bluray].mkv")]
    [InlineData("Digimon Adventure tri. Part 4 Loss [Bluray].mkv")]
    public void Match_UnstructuredOrInconsistentFilenameIsNotAnAdditionalTitle(string filename)
    {
        RadarrMovieSnapshot movie = Movie("Digimon Adventure tri. Chapter 4: Lost", "Digimon Adventure tri. Part 4 Loss");
        movie.File.Path = "/movies/Digimon Adventure tri. Part 4 Loss (2017) {tmdb-422347}/" + filename;
        Assert.Null(Match("Digimon: Digital Monsters", "Digimon Adventure tri. Part 4: Loss", movie));
    }

    [Fact]
    public void Match_FilenameDoesNotOverrideAnExplicitDifferentPartInTheMovieMetadata()
    {
        Assert.Null(Match("Digimon: Digital Monsters", "Digimon Adventure tri. Part 4: Loss",
            Movie("Digimon Adventure tri. Chapter 5: Lost", "Digimon Adventure tri. Part 4 Loss")));
    }

    [Fact]
    public void Match_FilenameDoesNotTurnRecapsIntoMovies()
    {
        Assert.Null(Match("Girls und Panzer", "Understand in less than 1 minute! Girls und Panzer das Finale: Part 1",
            Movie("Girls und Panzer - Das Finale: Teil 1", "Girls und Panzer das Finale Part 1")));
    }

    [Fact]
    public void Match_FilenameFallbackWorksForWindowsPaths()
    {
        RadarrMovieSnapshot movie = Movie("Digimon Adventure tri. Chapter 4: Lost", "Digimon Adventure tri. Part 4 Loss");
        movie.File.Path = movie.File.Path!.Replace('/', '\\');
        Assert.NotNull(Match("Digimon: Digital Monsters", "Digimon Adventure tri. Part 4: Loss", movie));
    }

    [Fact]
    public void Match_TheSixDigimonPartsDoNotMatchEachOther()
    {
        var parts = MissingPairs.Take(6).ToArray();
        for (int episode = 0; episode < parts.Length; episode++)
        for (int movie = 0; movie < parts.Length; movie++)
        {
            SpecialMovieMatch? match = Match((string)parts[episode][0], (string)parts[episode][1],
                Movie((string)parts[movie][2], (string)parts[movie][3]));
            Assert.Equal(episode == movie, match is not null);
        }
    }

    private static RadarrMovieSnapshot Movie(string title, string fileTitle) => new()
    {
        MovieId = 70, Title = title,
        File = new MediaFileSnapshot
        {
            Id = 900,
            Path = $"/movies/{fileTitle} (2017) {{tmdb-422347}}/{fileTitle} (2017) {{tmdb-422347}} [Bluray].mkv"
        }
    };

    private static SpecialMovieMatch? Match(string series, string episode, RadarrMovieSnapshot movie) =>
        SpecialMovieMatchingRules.Match("sonarr", new SonarrSeriesSnapshot { SeriesId = 12, Title = series },
            new SonarrEpisodeSnapshot { EpisodeId = 5, EpisodeNumber = 3, HasFile = true, Title = episode }, 0, "radarr", movie);
}
