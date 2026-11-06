using System.Text;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Tests.Clients;

// The live reads a confirmation and a setup run depend on. They exist because a snapshot answers only
// what the instances looked like at the last sync: which film still exists, which file it points at now,
// and how Sonarr would name the file are questions only the instance itself can answer.
public sealed class ArrClientLiveReadTests
{
    [Fact]
    public async Task Series_ReadsOneSeriesFromItsOwnRoute()
    {
        StubArrHandler handler = new(new Dictionary<string, string>
        {
            ["GET /api/v3/series/12"] = """{ "id": 12, "title": "Kabaneri of the Iron Fortress", "monitored": true }"""
        });
        SonarrClient client = new(new HttpClient(handler));

        SonarrSeriesResource? series = await client.FetchSeriesAsync(CreateSonarr(), 12, CancellationToken.None);

        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal("/api/v3/series/12", sent.Path);
        Assert.Equal("secret", sent.ApiKey);
        Assert.Equal(12, series?.Id);
        Assert.Equal("Kabaneri of the Iron Fortress", series?.Title);
    }

    [Fact]
    public async Task Episodes_ListOnlyTheEpisodesOfThatSeries()
    {
        StubArrHandler handler = new(new Dictionary<string, string>
        {
            ["GET /api/v3/episode"] = """
                [
                  { "id": 5, "seriesId": 12, "seasonNumber": 0, "episodeNumber": 1, "title": "The Battle of Unato", "monitored": true },
                  { "id": 6, "seriesId": 12, "seasonNumber": 1, "episodeNumber": 1, "title": "Gold Debt", "monitored": true }
                ]
                """
        });
        SonarrClient client = new(new HttpClient(handler));

        IReadOnlyList<SonarrEpisodeResource> episodes =
            await client.FetchEpisodesAsync(CreateSonarr(), 12, CancellationToken.None);

        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal("/api/v3/episode", sent.Path);
        Assert.Equal("?seriesId=12", sent.Query);
        Assert.Equal([5, 6], [.. episodes.Select(episode => episode.Id)]);
    }

    [Fact]
    public async Task EpisodeFiles_ListOnlyTheFilesOfThatSeries()
    {
        StubArrHandler handler = new(new Dictionary<string, string>
        {
            ["GET /api/v3/episodefile"] = """
                [
                  { "id": 44, "seriesId": 12, "path": "/data/series/Kabaneri/S00E05.mkv", "size": 5368709120 }
                ]
                """
        });
        SonarrClient client = new(new HttpClient(handler));

        IReadOnlyList<SonarrEpisodeFileResource> files =
            await client.FetchEpisodeFilesAsync(CreateSonarr(), 12, CancellationToken.None);

        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal("/api/v3/episodefile", sent.Path);
        Assert.Equal("?seriesId=12", sent.Query);
        Assert.Equal(
            "/data/series/Kabaneri/S00E05.mkv",
            Assert.Single(files).Path);
    }

    [Fact]
    public async Task EpisodeFile_ReadsOneFileFromItsOwnRoute()
    {
        StubArrHandler handler = new(new Dictionary<string, string>
        {
            ["GET /api/v3/episodefile/44"] = """
                { "id": 44, "path": "/data/series/Kabaneri/S00E05.mkv", "size": 5368709120, "releaseGroup": "WFFF" }
                """
        });
        SonarrClient client = new(new HttpClient(handler));

        SonarrEpisodeFileResource? file =
            await client.FetchEpisodeFileAsync(CreateSonarr(), 44, CancellationToken.None);

        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal("/api/v3/episodefile/44", sent.Path);
        Assert.Equal(5_368_709_120, file?.Size);
        Assert.Equal("WFFF", file?.ReleaseGroup);
    }

    [Fact]
    public async Task NamingConfig_ReadsThePatternSonarrAppliesItself()
    {
        StubArrHandler handler = new(new Dictionary<string, string>
        {
            ["GET /api/v3/config/naming"] = """
                {
                  "renameEpisodes": true,
                  "specialEpisodeFormat": "{Series Title} - {Air-Date} - {Episode Title}",
                  "episodeFormat": "{Series Title} - S{season:00}E{episode:00} - {Episode Title}",
                  "animeEpisodeFormat": "{Series Title} - S{season:00}E{episode:00}",
                  "dailyEpisodeFormat": "{Series Title} - {Air-Date}"
                }
                """
        });
        SonarrClient client = new(new HttpClient(handler));

        SonarrNamingConfigResource? naming =
            await client.FetchNamingConfigAsync(CreateSonarr(), CancellationToken.None);

        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal("/api/v3/config/naming", sent.Path);
        // The pattern stays Sonarr's text; whether renaming is on at all decides how a missing
        // proposal has to be read.
        Assert.True(naming?.RenameEpisodes);
        Assert.Equal("{Series Title} - {Air-Date} - {Episode Title}", naming?.SpecialEpisodeFormat);
        Assert.Equal("{Series Title} - S{season:00}E{episode:00} - {Episode Title}", naming?.EpisodeFormat);
    }

    [Fact]
    public async Task RenameProposals_ReadWhatSonarrWouldMakeOfEachFile()
    {
        StubArrHandler handler = new(new Dictionary<string, string>
        {
            ["GET /api/v3/rename"] = """
                [
                  {
                    "id": 44,
                    "seriesId": 12,
                    "episodeId": 5,
                    "filename": "kabaneri.s00e05.mkv",
                    "newFilename": "Kabaneri of the Iron Fortress - 2016-06-23 - The Battle of Unato"
                  }
                ]
                """
        });
        SonarrClient client = new(new HttpClient(handler));

        IReadOnlyList<SonarrRenameResource> proposals =
            await client.FetchRenameProposalsAsync(CreateSonarr(), 12, CancellationToken.None);

        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal("/api/v3/rename", sent.Path);
        Assert.Equal("?seriesId=12", sent.Query);
        SonarrRenameResource proposal = Assert.Single(proposals);
        Assert.Equal(44, proposal.Id);
        Assert.Equal("kabaneri.s00e05.mkv", proposal.Filename);
        Assert.Equal(
            "Kabaneri of the Iron Fortress - 2016-06-23 - The Battle of Unato",
            proposal.NewFilename);
    }

    [Fact]
    public async Task Movie_ReadsTheFilmThatIsChosenNow()
    {
        StubArrHandler handler = new(new Dictionary<string, string>
        {
            ["GET /api/v3/movie/70"] = """
                {
                  "id": 70,
                  "title": "Kabaneri of the Iron Fortress: The Battle of Unato",
                  "movieFileId": 900,
                  "hasFile": true
                }
                """
        });
        RadarrClient client = new(new HttpClient(handler));

        RadarrMovieResource? movie = await client.FetchMovieAsync(CreateRadarr(), 70, CancellationToken.None);

        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal("/api/v3/movie/70", sent.Path);
        Assert.Equal("secret", sent.ApiKey);
        Assert.Equal(900, movie?.MovieFileId);
        Assert.Equal("Kabaneri of the Iron Fortress: The Battle of Unato", movie?.Title);
    }

    [Fact]
    public async Task MovieFiles_ListOnlyTheFilesOfThatFilm()
    {
        StubArrHandler handler = new(new Dictionary<string, string>
        {
            ["GET /api/v3/moviefile"] = """
                [
                  { "id": 900, "movieId": 70, "path": "/data/movies/The Battle of Unato.mkv", "size": 5368709120 }
                ]
                """
        });
        RadarrClient client = new(new HttpClient(handler));

        IReadOnlyList<RadarrMovieFileResource> files =
            await client.FetchMovieFilesAsync(CreateRadarr(), 70, CancellationToken.None);

        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal("/api/v3/moviefile", sent.Path);
        Assert.Equal("?movieId=70", sent.Query);
        Assert.Equal(900, Assert.Single(files).Id);
    }

    private static ArrInstanceSettings CreateSonarr()
    {
        return new ArrInstanceSettings
        {
            Id = "sonarr-1",
            Name = "Sonarr",
            BaseUrl = "http://sonarr.local/",
            ApiKey = "secret"
        };
    }

    private static ArrInstanceSettings CreateRadarr()
    {
        return new ArrInstanceSettings
        {
            Id = "radarr-1",
            Name = "Radarr",
            BaseUrl = "http://radarr.local/",
            ApiKey = "secret"
        };
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string ApiKey, string Body);

    private sealed class StubArrHandler(Dictionary<string, string> jsonByRoute) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            string apiKey = request.Headers.TryGetValues("X-Api-Key", out IEnumerable<string>? values)
                ? string.Join(',', values)
                : string.Empty;
            string path = request.RequestUri!.AbsolutePath;
            Requests.Add(new RecordedRequest(request.Method, path, request.RequestUri.Query, apiKey, body));

            string json = jsonByRoute.TryGetValue($"{request.Method} {path}", out string? found)
                ? found
                : "{}";

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
        }
    }
}
