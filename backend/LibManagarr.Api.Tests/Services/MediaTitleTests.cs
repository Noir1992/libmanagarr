using System.Net;
using System.Text;
using System.Text.Json;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibManagarr.Api.Tests.Services;

public sealed class MediaTitleTests
{
    [Theory]
    [InlineData(AppLanguage.De, "Deutsch")]
    [InlineData(AppLanguage.Fr, "English")]
    [InlineData(AppLanguage.En, "English")]
    public void Resolver_UsesLanguageThenEnglish(AppLanguage language, string expected)
    {
        MediaTitles titles = new() { OriginalTitle = "Original", Translations = new()
        {
            ["de"] = new() { Title = " Deutsch " }, ["en"] = new() { Title = "English" }, ["fr"] = new() { Title = "  " }
        } };
        Assert.Equal(expected, MediaTitleResolver.Resolve("Source", titles, language));
        Assert.Equal(["Deutsch", "English", "Original", "Source", "Alias"], MediaTitleResolver.Candidates("Source", titles, AppLanguage.De, ["Alias", "English", " "]));
        titles.Translations.Clear();
        Assert.Equal("Original", MediaTitleResolver.Resolve("Source", titles, language));
        titles.OriginalTitle = " ";
        Assert.Equal("Source", MediaTitleResolver.Resolve("Source", titles, language));
    }

    [Fact]
    public void OldSnapshots_KeepSourceTitles()
    {
        RadarrMovieSnapshot movie = JsonSerializer.Deserialize<RadarrMovieSnapshot>("""{"Title":"Legacy"}""")!;
        Assert.Equal("Legacy", MediaTitleResolver.Resolve(movie.Title, movie.Titles, AppLanguage.De));
        string restored = JsonSerializer.Serialize(movie);
        Assert.Equal("Legacy", JsonSerializer.Deserialize<RadarrMovieSnapshot>(restored)!.Title);
    }

    [Fact]
    public async Task Refresh_BatchesAllLanguages_DetectsFallback_AndCoalescesConcurrentReads()
    {
        Fixture fixture = new();
        await Task.WhenAll(fixture.Service.RefreshAsync(default), fixture.Service.RefreshAsync(default));
        Assert.Equal(4, fixture.Handler.Requests);
        Assert.Equal("Deutsch", fixture.Movie.Titles.Translations["de"].Title);
        Assert.Equal(MediaTitleFetchStatus.Available, fixture.Movie.Titles.Translations["de"].Status);
        Assert.Null(fixture.Movie.Titles.Translations["fr"].Title);
        Assert.Equal(MediaTitleFetchStatus.Missing, fixture.Movie.Titles.Translations["fr"].Status);
        Assert.Equal("English", MediaTitleResolver.Resolve(fixture.Movie.Title, fixture.Movie.Titles, AppLanguage.Fr));
        Assert.Equal("Source", fixture.Movie.Title);
        Assert.Equal("/movies/source.mkv", fixture.Movie.File.Path);
        fixture.Clock.Now = fixture.Clock.Now.AddDays(2);
        await fixture.Service.RefreshAsync(default);
        // English reference plus French; the successful German translation is reused.
        Assert.Equal(7, fixture.Handler.Requests);
    }

    [Fact]
    public async Task Refresh_FailuresKeepSuccessfulTitles_AndRetryAfterBackoff()
    {
        Fixture fixture = new();
        await fixture.Service.RefreshAsync(default);
        fixture.Clock.Now = fixture.Clock.Now.AddDays(8);
        fixture.Handler.Fail = true;
        await fixture.Service.RefreshAsync(default);
        Assert.Equal("Deutsch", fixture.Movie.Titles.Translations["de"].Title);
        Assert.Equal(MediaTitleFetchStatus.Failed, fixture.Movie.Titles.Translations["de"].Status);
        int requests = fixture.Handler.Requests;
        await fixture.Service.RefreshAsync(default);
        Assert.Equal(requests, fixture.Handler.Requests);
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(6);
        fixture.Handler.Fail = false;
        await fixture.Service.RefreshAsync(default);
        Assert.Equal(MediaTitleFetchStatus.Available, fixture.Movie.Titles.Translations["de"].Status);
    }

    [Fact]
    public async Task Refresh_EmptyAnswersDoNotReplaceKnownTitles()
    {
        Fixture fixture = new();
        await fixture.Service.RefreshAsync(default);
        fixture.Clock.Now = fixture.Clock.Now.AddDays(8);
        fixture.Handler.German = " ";
        await fixture.Service.RefreshAsync(default);
        Assert.Equal("Deutsch", fixture.Movie.Titles.Translations["de"].Title);
        Assert.Equal(MediaTitleFetchStatus.Missing, fixture.Movie.Titles.Translations["de"].Status);
    }

    [Fact]
    public async Task Refresh_RemovedMediaAreNotReintroduced()
    {
        Fixture fixture = new();
        fixture.Handler.OnRead = () => fixture.Settings.RadarrSnapshots[0].Movies.Clear();
        await fixture.Service.RefreshAsync(default);
        Assert.Empty(fixture.Settings.RadarrSnapshots[0].Movies);
    }

    [Fact]
    public async Task Refresh_ChangedExternalIdentityRejectsOldResponse()
    {
        Fixture fixture = new();
        fixture.Handler.TmdbId = 999;
        await fixture.Service.RefreshAsync(default);
        Assert.All(fixture.Movie.Titles.Translations.Values, translation => Assert.Null(translation.Title));
    }

    [Fact]
    public async Task Sonarr_ReadsExplicitSeriesTranslationsWithoutAssumingEpisodeLanguage()
    {
        using HttpClient http = new(new SonarrHandler());
        SonarrInstanceSnapshot snapshot = await new SonarrClient(http).FetchSnapshotAsync(new() { Id = "s", BaseUrl = "http://sonarr", ApiKey = "key" }, default);
        SonarrSeriesSnapshot series = Assert.Single(snapshot.Series);
        Assert.Equal("Quelltitel", series.Title);
        Assert.Equal("Serie", MediaTitleResolver.Resolve(series.Title, series.Titles, AppLanguage.De));
        Assert.Equal("Original", MediaTitleResolver.Resolve(series.Title, series.Titles, AppLanguage.Fr));
        Assert.Equal("sonarr:translations", series.Titles.Translations["de"].Source);
        Assert.Equal("Special", Assert.Single(Assert.Single(series.Seasons).Episodes).Title);
    }

    private sealed class SonarrHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string body = request.RequestUri!.AbsolutePath switch
            {
                "/api/v3/series" => """[{"id":1,"title":"Quelltitel","originalTitle":"Original","translations":[{"language":{"id":99,"name":"German"},"title":"Serie"},{"language":{"id":100,"name":"French"},"title":" "}]}]""",
                "/api/v3/episode" => """[{"id":10,"title":"Special","seasonNumber":0,"episodeNumber":1,"hasFile":false}]""",
                _ => "[]"
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class Fixture
    {
        public readonly Clock Clock = new();
        public readonly Handler Handler = new();
        public readonly RadarrMovieSnapshot Movie = new() { MovieId = 1, TmdbId = 10, Title = "Source", File = new() { Path = "/movies/source.mkv" } };
        public readonly AppSettings Settings;
        public readonly MediaTitleRefreshService Service;
        public Fixture()
        {
            Settings = new() { RadarrInstances = [new() { Id = "r", BaseUrl = "http://radarr", ApiKey = "key" }],
                RadarrSnapshots = [new() { InstanceId = "r", Movies = [Movie] }] };
            Service = new(new Store(Settings), new RadarrClient(new HttpClient(Handler)), Clock, NullLogger<MediaTitleRefreshService>.Instance);
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Store(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken token) => Task.FromResult(settings);
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken token) => Task.FromResult(value);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public int Requests;
        public bool Fail;
        public string German = "Deutsch";
        public int TmdbId = 10;
        public Action? OnRead;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            Assert.Equal("key", Assert.Single(request.Headers.GetValues("X-Api-Key")));
            OnRead?.Invoke();
            if (Fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            string body;
            if (request.RequestUri!.AbsolutePath == "/api/v3/language")
                body = """[{"id":7,"name":"English"},{"id":17,"name":"German"},{"id":27,"name":"French"}]""";
            else
            {
                Assert.Equal("/api/v3/movie", request.RequestUri.AbsolutePath);
                Assert.Contains("excludeLocalCovers=true", request.RequestUri.Query);
                string title = request.RequestUri.Query.Contains("languageId=17&") ? German : "English";
                body = JsonSerializer.Serialize(new[] { new { id = 1, tmdbId = TmdbId, title } });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
