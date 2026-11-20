using System.Net;
using System.Text;
using System.Text.Json;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Clients;

public sealed class RadarrTitleSnapshotTests
{
    [Theory]
    [InlineData(false, "Anohana: The Flower We Saw That Day", "AnoHana - Die Blume, die wir an jenem Tag sahen", "Anohana The Movie - The Flower We Saw That Day")]
    [InlineData(false, "Digimon: Digital Monsters", "Digimon Adventure tri. Chapter 4: Lost", "Digimon Adventure tri. Part 4: Loss")]
    [InlineData(false, "Made in Abyss", "Made in Abyss: Seelen der Finsternis", "Made in Abyss: Dawn of the Deep Soul")]
    [InlineData(true, "Made in Abyss", "Made in Abyss: Seelen der Finsternis", "Made in Abyss: Dawn of the Deep Soul")]
    public async Task FetchSnapshot_ReportedTitleVariantsReachTheSpecialMatcher(
        bool originalOnly, string series, string displayedTitle, string matchingTitle)
    {
        using HttpClient http = new(new TitlesHandler(displayedTitle, matchingTitle, originalOnly));
        ArrInstanceSettings instance = new() { Id = "radarr", BaseUrl = "http://arr.test", ApiKey = "test" };

        RadarrInstanceSnapshot snapshot = await new RadarrClient(http).FetchSnapshotAsync(instance, CancellationToken.None);
        RadarrMovieSnapshot movie = Assert.Single(snapshot.Movies);

        Assert.Equal(displayedTitle, movie.Title);
        Assert.Contains(matchingTitle, movie.AlternativeTitles);
        SpecialMovieMatch? match = SpecialMovieMatchingRules.Match("sonarr",
            new SonarrSeriesSnapshot { SeriesId = 12, Title = series },
            new SonarrEpisodeSnapshot { EpisodeId = 5, EpisodeNumber = 3, Title = series == "Made in Abyss" ? "Movie: Dawn of the Deep Soul" : matchingTitle },
            0, instance.Id, movie);
        Assert.NotNull(match);
        Assert.True(match.MovieHasFile);
        Assert.Equal(series == "Made in Abyss"
            ? SpecialMovieMatchingRules.SeriesQualifiedTitleCode
            : SpecialMovieMatchingRules.AlternativeTitleCode, match.Reasons[0].Code);
    }

    private sealed class TitlesHandler(string title, string matchingTitle, bool originalOnly) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.RequestUri!.AbsolutePath switch
            {
                "/api/v3/qualityprofile" => "[]",
                "/api/v3/movie" => JsonSerializer.Serialize(new[] { new
                {
                    id = 70, title, hasFile = true, movieFileId = 900,
                    originalTitle = originalOnly ? matchingTitle : null,
                    alternateTitles = originalOnly ? [] : new[] { new { title = matchingTitle } }
                } }),
                "/api/v3/moviefile" => """[{"id":900,"movieId":70,"path":"/movies/movie.mkv"}]""",
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
