using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Endpoints;

public sealed class LibraryFileStatusTests
{
    private static async Task<AppSettings> Settings()
    {
        using HttpClient http = new(new ArrHandler());
        ArrInstanceSettings instance = new() { Id = "first", Name = "Test", BaseUrl = "http://arr.test", ApiKey = "test" };
        return new()
        {
            SonarrInstances = [instance], RadarrInstances = [instance],
            SonarrSnapshots = [await new SonarrClient(http).FetchSnapshotAsync(instance, CancellationToken.None)],
            RadarrSnapshots = [await new RadarrClient(http).FetchSnapshotAsync(instance, CancellationToken.None)]
        };
    }

    [Fact]
    public async Task Sync_ShouldRetainMissingMetadataAndExplicitFileStatusWithoutRequestingMissingMovieFiles()
    {
        AppSettings settings = await Settings();
        Assert.Equal(3, settings.RadarrSnapshots[0].Movies.Count);
        RadarrMovieSnapshot missing = settings.RadarrSnapshots[0].Movies[1];
        Assert.False(missing.HasFile);
        Assert.Equal("HD", missing.Profile.Name);
        Assert.Equal(10, missing.Profile.UpgradeUntilCustomFormatScore);
        Assert.Equal(new[] { "Drama" }, missing.Genres);
        Assert.Equal(new[] { "favorite" }, missing.Tags);
        Assert.Equal("released", missing.Status);
        Assert.Equal(2020, missing.AirDateUtc!.Value.Year);
        Assert.False(settings.RadarrSnapshots[0].Movies[2].HasAired);
        Assert.Equal(4, settings.SonarrSnapshots[0].Series[0].Seasons.Sum(season => season.Episodes.Count));
        Assert.Equal(3, settings.SonarrSnapshots[0].Series[0].Seasons.SelectMany(season => season.Episodes).Count(episode => !episode.HasFile));
    }

    [Theory]
    [InlineData(false, "all", 3)]
    [InlineData(false, "present", 1)]
    [InlineData(false, "missing", 2)]
    [InlineData(true, "all", 4)]
    [InlineData(true, "present", 0)]
    [InlineData(true, "partial", 3)]
    [InlineData(true, "missing", 0)]
    public async Task Library_ShouldFilterFileStatusAndExposeNoScoreForMissingFiles(bool sonarr, string selection, int count)
    {
        JsonElement response = await Query(sonarr, await Settings(), new() { FileStatusFilter = selection });
        JsonElement[] rows = Entries(sonarr, response);
        Assert.Equal(count, rows.Length);
        foreach (JsonElement row in rows.Where(row => !row.GetProperty("HasFile").GetBoolean()))
        {
            Assert.Equal(JsonValueKind.Null, row.GetProperty("CustomFormatScore").ValueKind);
            Assert.Equal(JsonValueKind.Null, row.GetProperty("MediaDetails").ValueKind);
        }
        foreach (JsonElement row in rows.Where(row => row.GetProperty("HasFile").GetBoolean()))
            Assert.Equal("HEVC", row.GetProperty("MediaDetails").GetProperty("VideoCodec").GetString());
    }

    [Fact]
    public async Task Sonarr_ShouldCombineSeriesEpisodeMonitoringAiredAndRegularEpisodeFilters()
    {
        LibraryRequestFilter filter = new()
        {
            FileStatusFilter = "partial", MonitoredFilter = "monitored", EpisodeMonitoredFilter = "monitored",
            AiredFilter = "aired", Types = ["regular"], Genres = ["Drama"], Profiles = ["HD"]
        };
        JsonElement response = await Query(true, await Settings(), filter);
        JsonElement[] episodes = Entries(true, response);
        Assert.Equal(new[] { "Missing" }, episodes.Select(episode => episode.GetProperty("Title").GetString()));
        Assert.All(episodes, episode => Assert.True(episode.GetProperty("Monitored").GetBoolean()));
        JsonElement series = Assert.Single(response.GetProperty("Instances")[0].GetProperty("Rows").EnumerateArray());
        Assert.Equal("Drama", series.GetProperty("Genres")[0].GetString());
        Assert.Equal(4, series.GetProperty("LibraryEpisodeCount").GetInt32());
        Assert.Equal(1, series.GetProperty("FileCount").GetInt32());
        Assert.Equal(1, series.GetProperty("TotalEpisodes").GetInt32());
        Assert.Equal(1, series.GetProperty("AiredEpisodes").GetInt32());
        Assert.Single(series.GetProperty("Seasons").EnumerateArray());
        filter.EpisodeMonitoredFilter = "notMonitored";
        filter.AiredFilter = "notAired";
        Assert.Equal(new[] { "Future" }, Entries(true, await Query(true, await Settings(), filter))
            .Select(episode => episode.GetProperty("Title").GetString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingFiles_ShouldNotMatchScoreUpgradeOrUnknownMediaFilters(bool sonarr)
    {
        AppSettings settings = await Settings();
        LibraryRequestFilter[] filters = [
            new() { ScoreMin = 0 }, new() { UpgradeTargetFilter = "below" }, new() { UpgradeTargetFilter = "reached" },
            new() { Resolutions = ["Unknown"] }, new() { VideoCodecs = ["Unknown"] }, new() { VideoBitrateMin = 0 }
        ];
        foreach (LibraryRequestFilter filter in filters)
        {
            filter.FileStatusFilter = "missing";
            JsonElement[] matches = Entries(sonarr, await Query(sonarr, settings, filter));
            Assert.Empty(matches);
        }
        JsonElement result = await Query(sonarr, settings, new() { FileStatusFilter = "missing", ExcludedFilters = ["file-status-filter"] });
        if (sonarr)
            Assert.Equal(4, Entries(true, result).Length);
        else
            Assert.True(Assert.Single(Entries(false, result)).GetProperty("HasFile").GetBoolean());
    }

    [Fact]
    public async Task CountsAndStatistics_ShouldDistinguishAllEntriesFromActualFiles()
    {
        AppSettings settings = await Settings();
        Dictionary<string, Dictionary<string, int>> movieCounts = LibraryFilterCountBuilder.Build(settings.RadarrSnapshots[0]);
        Dictionary<string, Dictionary<string, int>> episodeCounts = LibraryFilterCountBuilder.Build(settings.SonarrSnapshots[0]);
        Assert.Equal(3, movieCounts["file-status-filter"]["__all__"]);
        Assert.Equal(2, movieCounts["file-status-filter"]["missing"]);
        Assert.Equal(1, movieCounts["video-codec-filter"]["__all__"]);
        Assert.Equal(1, episodeCounts["file-status-filter"]["__all__"]);
        Assert.Equal(1, episodeCounts["file-status-filter"]["partial"]);
        Assert.Equal(1, episodeCounts["video-codec-filter"]["__all__"]);
        Assert.Equal(3, episodeCounts["episode-monitored-filter"]["monitored"]);
        Assert.Equal(1, episodeCounts["episode-type-filter"]["special"]);
        Assert.Equal(3, episodeCounts["episode-type-filter"]["regular"]);
        Assert.Equal(1, episodeCounts["upgrade-target-filter"]["below"]);
        CodecStatisticsResponse codecs = new CodecStatisticsService().Build(settings, null, null);
        Assert.Equal(2, Assert.Single(codecs.VideoCodecs).Count);
        Assert.Equal("HEVC", Assert.Single(codecs.VideoCodecs).Label);
        LanguageStatisticsResponse languages = new LanguageStatisticsService().Build(settings, null, null);
        Assert.Equal(2, languages.AudioLanguages.Sum(item => item.Count));
    }

    [Fact]
    public async Task Availability_ShouldReportPresentOnlyForCompleteSeriesAndPartialForPendingOrMissingEpisodes()
    {
        AppSettings settings = await Settings();
        SonarrSeriesSnapshot incomplete = settings.SonarrSnapshots[0].Series[0];
        incomplete.Seasons.Where(season => season.SeasonNumber == 0).SelectMany(season => season.Episodes)
            .ToList().ForEach(episode => episode.Monitored = false);
        settings.SonarrSnapshots[0].Series.Add(new SonarrSeriesSnapshot
        {
            SeriesId = 2, Title = "Pending", Status = "continuing",
            Seasons = [new() { SeasonNumber = 1, Episodes = [
                new() { EpisodeNumber = 1, Title = "Downloaded", HasFile = true, Monitored = true, AirDateUtc = DateTimeOffset.UtcNow.AddDays(-1) },
                new() { EpisodeNumber = 2, Title = "Future", HasFile = false, Monitored = true, AirDateUtc = DateTimeOffset.UtcNow.AddDays(1) }
            ] }]
        });
        settings.SonarrSnapshots[0].Series.Add(new SonarrSeriesSnapshot
        {
            SeriesId = 3, Title = "Complete", Status = "ended",
            Seasons = [new() { SeasonNumber = 1, Episodes = [
                new() { EpisodeNumber = 1, Title = "Downloaded", HasFile = true, Monitored = true, AirDateUtc = DateTimeOffset.UtcNow.AddDays(-2) }
            ] }, new() { SeasonNumber = 0, Episodes = [
                new() { EpisodeNumber = 0, Title = "Unmonitored special", HasFile = false, Monitored = false, AirDateUtc = DateTimeOffset.UtcNow.AddDays(-1) }
            ] }]
        });
        JsonElement present = await Query(true, settings, new() { FileStatusFilter = "present" });
        JsonElement completeSeries = Assert.Single(present.GetProperty("Instances")[0].GetProperty("Rows").EnumerateArray());
        Assert.Equal("Complete", completeSeries.GetProperty("SeriesTitle").GetString());
        Assert.Equal("present", completeSeries.GetProperty("LibraryStatus").GetString());
        Assert.Equal(2, Entries(true, present).Length);
        JsonElement partial = await Query(true, settings, new() { FileStatusFilter = "partial" });
        Assert.Equal(new[] { "Pending", "Series" }, partial.GetProperty("Instances")[0].GetProperty("Rows")
            .EnumerateArray().Select(row => row.GetProperty("SeriesTitle").GetString()));
        Assert.All(Entries(true, partial), episode => Assert.False(episode.GetProperty("HasFile").GetBoolean()));
        Assert.Empty(Entries(true, await Query(true, settings, new() { FileStatusFilter = "missing" })));
        Assert.Empty(Entries(true, await Query(true, settings, new() { FileStatusFilter = "missing", VideoCodecs = ["HEVC"] })));
        JsonElement excluded = await Query(true, settings, new() { FileStatusFilter = "missing", ExcludedFilters = ["file-status-filter"] });
        Assert.Equal(3, excluded.GetProperty("Instances")[0].GetProperty("Rows").EnumerateArray().Count());
        Dictionary<string, int> counts = LibraryFilterCountBuilder.Build(settings.SonarrSnapshots[0])["file-status-filter"];
        Assert.Equal(3, counts["__all__"]);
        Assert.Equal(1, counts["present"]);
        Assert.Equal(2, counts["partial"]);
        Assert.False(counts.ContainsKey("missing"));
        JsonElement futureMovie = await Query(false, settings, new() { FileStatusFilter = "missing", AiredFilter = "notAired" });
        Assert.Equal("Future", Assert.Single(Entries(false, futureMovie)).GetProperty("MovieTitle").GetString());
    }

    [Theory]
    [InlineData("movie", false, 3, 0)]
    [InlineData("regular", false, 0, 3)]
    [InlineData("special", false, 0, 1)]
    [InlineData("movie,regular", false, 3, 3)]
    [InlineData("regular,special", false, 0, 4)]
    [InlineData("", false, 3, 4)]
    [InlineData("__all__", false, 3, 4)]
    [InlineData("special", true, 3, 3)]
    [InlineData("movie", true, 0, 4)]
    [InlineData("movie,regular", true, 0, 1)]
    public async Task Type_ShouldCombineMoviesEpisodesAndSpecialsWithOrAndSupportExclusion(string selected, bool exclude, int movies, int episodes)
    {
        AppSettings settings = await Settings();
        LibraryRequestFilter filter = new()
        {
            Types = selected.Split(',', StringSplitOptions.RemoveEmptyEntries),
            ExcludedFilters = exclude ? ["episode-type-filter"] : []
        };
        Assert.Equal(movies, Entries(false, await Query(false, settings, filter)).Length);
        Assert.Equal(episodes, Entries(true, await Query(true, settings, filter)).Length);
    }

    [Theory]
    [InlineData("monitored", false, 1, 3)]
    [InlineData("notMonitored", false, 2, 1)]
    [InlineData("monitored", true, 2, 1)]
    public async Task Monitored_ShouldApplyToMoviesAndEpisodes(string selected, bool exclude, int movies, int episodes)
    {
        AppSettings settings = await Settings();
        LibraryRequestFilter filter = new()
        {
            EpisodeMonitoredFilter = selected,
            ExcludedFilters = exclude ? ["episode-monitored-filter"] : []
        };
        Assert.Equal(movies, Entries(false, await Query(false, settings, filter)).Length);
        Assert.Equal(episodes, Entries(true, await Query(true, settings, filter)).Length);
        Dictionary<string, int> counts = LibraryFilterCountBuilder.Build(settings.RadarrSnapshots[0])["episode-monitored-filter"];
        Assert.Equal(1, counts["monitored"]);
        Assert.Equal(2, counts["notMonitored"]);
        Assert.Equal(3, LibraryFilterCountBuilder.Build(settings.RadarrSnapshots[0])["episode-type-filter"]["movie"]);
    }

    [Fact]
    public async Task SeriesMonitoring_ShouldRemainIndependentOfMovieMonitoring()
    {
        AppSettings settings = await Settings();
        LibraryRequestFilter filter = new() { MonitoredFilter = "notMonitored" };
        Assert.Equal(3, Entries(false, await Query(false, settings, filter)).Length);
        Assert.Empty(Entries(true, await Query(true, settings, filter)));
    }

    [Theory]
    [InlineData("partial", false)]
    [InlineData("present", true)]
    public async Task MissingAndMonitored_ShouldRequireTheSameEpisodeToMatchBothConditions(string fileStatus, bool exclude)
    {
        AppSettings settings = await Settings();
        SonarrSeriesSnapshot mixed = settings.SonarrSnapshots[0].Series[0];
        foreach (SonarrEpisodeSnapshot episode in mixed.Seasons.SelectMany(season => season.Episodes).Where(episode => !episode.HasFile))
            episode.Monitored = false;
        LibraryRequestFilter filter = new()
        {
            FileStatusFilter = fileStatus, EpisodeMonitoredFilter = "monitored",
            ExcludedFilters = exclude ? ["file-status-filter"] : []
        };
        Assert.Empty(Entries(true, await Query(true, settings, filter)));
        settings.SonarrSnapshots[0].Series.Add(new SonarrSeriesSnapshot
        {
            SeriesId = 2, Title = "Actual missing monitored episode", Monitored = false,
            Seasons = [new() { SeasonNumber = 1, Episodes = [
                new() { EpisodeNumber = 1, Title = "Downloaded", HasFile = true, Monitored = true, AirDateUtc = DateTimeOffset.UtcNow.AddDays(-1) },
                new() { EpisodeNumber = 2, Title = "Missing monitored", HasFile = false, Monitored = true, AirDateUtc = DateTimeOffset.UtcNow.AddDays(-1) },
                new() { EpisodeNumber = 3, Title = "Future", HasFile = false, Monitored = true, AirDateUtc = DateTimeOffset.UtcNow.AddDays(1) },
                new() { EpisodeNumber = 4, Title = "Missing unmonitored", HasFile = false, Monitored = false, AirDateUtc = DateTimeOffset.UtcNow.AddDays(-1) },
                new() { EpisodeNumber = 5, Title = "Unknown date", HasFile = false, Monitored = true }
            ] }]
        });
        JsonElement result = await Query(true, settings, filter);
        JsonElement series = Assert.Single(result.GetProperty("Instances")[0].GetProperty("Rows").EnumerateArray());
        Assert.Equal(2, series.GetProperty("SeriesId").GetInt32());
        Assert.Equal("partial", series.GetProperty("LibraryStatus").GetString());
        JsonElement[] matches = Entries(true, result);
        Assert.DoesNotContain("Missing unmonitored", matches.Select(episode => episode.GetProperty("Title").GetString()));
        JsonElement match = matches.Single(episode => episode.GetProperty("Title").GetString() == "Missing monitored");
        Assert.False(match.GetProperty("HasFile").GetBoolean());
        Assert.True(match.GetProperty("Monitored").GetBoolean());
        Assert.True(match.GetProperty("HasAired").GetBoolean());
    }

    [Fact]
    public async Task Counts_ShouldDescribeOnlyTheFilteredMoviesSeriesAndEpisodes()
    {
        AppSettings settings = await Settings();
        settings.RadarrInstances.Add(new() { Id = "empty", Name = "No results" });
        settings.SonarrInstances.Add(new() { Id = "empty", Name = "No results" });
        LibraryRequestFilter filter = new() { FileStatusFilter = "missing", EpisodeMonitoredFilter = "monitored" };
        JsonElement movies = await Query(false, settings, filter);
        Assert.Single(Entries(false, movies));
        JsonElement movieCounts = movies.GetProperty("Counts");
        Assert.Equal(1, movieCounts.GetProperty("file-status-filter").GetProperty("__all__").GetInt32());
        Assert.Equal(1, movieCounts.GetProperty("file-status-filter").GetProperty("missing").GetInt32());
        Assert.Equal(1, movieCounts.GetProperty("genre-filter").GetProperty("Drama").GetInt32());
        Assert.Equal(1, movieCounts.GetProperty("episode-type-filter").GetProperty("movie").GetInt32());
        Assert.Equal(0, movieCounts.GetProperty("resolution-filter").GetProperty("__all__").GetInt32());
        Assert.Equal(1, movieCounts.GetProperty("radarr-instance-filter").GetProperty("first").GetInt32());
        Assert.Equal(0, movieCounts.GetProperty("radarr-instance-filter").GetProperty("empty").GetInt32());
        JsonElement episodes = await Query(true, settings, new()
        {
            FileStatusFilter = "partial", EpisodeMonitoredFilter = "monitored",
            SonarrInstanceFilter = ["__all__"], RadarrInstanceFilter = ["__all__"]
        });
        Assert.Equal(2, Entries(true, episodes).Length);
        JsonElement counts = episodes.GetProperty("Counts");
        Assert.Equal(1, counts.GetProperty("genre-filter").GetProperty("Drama").GetInt32());
        Assert.Equal(1, counts.GetProperty("file-status-filter").GetProperty("__all__").GetInt32());
        Assert.Equal(1, counts.GetProperty("monitored-filter").GetProperty("monitored").GetInt32());
        Assert.Equal(2, counts.GetProperty("episode-monitored-filter").GetProperty("monitored").GetInt32());
        Assert.Equal(1, counts.GetProperty("episode-type-filter").GetProperty("regular").GetInt32());
        Assert.Equal(1, counts.GetProperty("episode-type-filter").GetProperty("special").GetInt32());
        Assert.Equal(0, counts.GetProperty("video-codec-filter").GetProperty("__all__").GetInt32());
        Assert.Equal(1, counts.GetProperty("sonarr-instance-filter").GetProperty("first").GetInt32());
        filter.Types = ["movie"];
        JsonElement noResults = await Query(true, settings, filter);
        Assert.Empty(Entries(true, noResults));
        Assert.All(noResults.GetProperty("Counts").EnumerateObject(), facet =>
            Assert.All(facet.Value.EnumerateObject(), value => Assert.Equal(0, value.Value.GetInt32())));
    }

    [Fact]
    public async Task FilteredCounts_ShouldIncludeOnlyMediaValuesOfMatchingFilesAndRespectExclusion()
    {
        AppSettings settings = await Settings();
        LibraryRequestFilter filter = new() { Types = ["regular"], VideoCodecs = ["HEVC"] };
        JsonElement result = await Query(true, settings, filter);
        Assert.Single(Entries(true, result));
        JsonElement counts = result.GetProperty("Counts");
        Assert.Equal(1, counts.GetProperty("video-codec-filter").GetProperty("HEVC").GetInt32());
        Assert.Equal(1, counts.GetProperty("resolution-filter").GetProperty("1080p").GetInt32());
        Assert.Equal(1, counts.GetProperty("audio-language-filter").GetProperty("English").GetInt32());
        Assert.Equal(1, counts.GetProperty("upgrade-target-filter").GetProperty("below").GetInt32());
        Assert.Equal(1, counts.GetProperty("episode-type-filter").GetProperty("regular").GetInt32());
        Assert.False(counts.GetProperty("episode-type-filter").TryGetProperty("special", out _));
        Assert.Equal(1, counts.GetProperty("file-status-filter").GetProperty("partial").GetInt32());
        filter.ExcludedFilters = ["video-codec-filter"];
        result = await Query(true, settings, filter);
        Assert.Equal(2, Entries(true, result).Length);
        Assert.Equal(0, result.GetProperty("Counts").GetProperty("video-codec-filter").GetProperty("__all__").GetInt32());
        Assert.Equal(2, result.GetProperty("Counts").GetProperty("episode-type-filter").GetProperty("regular").GetInt32());
    }

    [Fact]
    public async Task UpcomingSeriesWithoutFiles_ShouldBeUnavailableAndRemainInMissingFilterAndCounts()
    {
        AppSettings settings = await Settings();
        settings.SonarrSnapshots[0].Series.Clear();
        settings.SonarrSnapshots[0].Series.Add(new SonarrSeriesSnapshot
        {
            SeriesId = 99, Title = "Upcoming", Status = "upcoming",
            Seasons = [new() { SeasonNumber = 1, Episodes = [
                new() { EpisodeNumber = 1, HasFile = false, Monitored = true, AirDateUtc = DateTimeOffset.UtcNow.AddDays(30) }
            ] }]
        });

        JsonElement missing = await Query(true, settings, new() { FileStatusFilter = "missing" });
        JsonElement series = Assert.Single(missing.GetProperty("Instances")[0].GetProperty("Rows").EnumerateArray());
        Assert.Equal("missing", series.GetProperty("LibraryStatus").GetString());
        Assert.Equal(1, missing.GetProperty("Counts").GetProperty("file-status-filter").GetProperty("missing").GetInt32());
        Assert.Single(Entries(true, missing));
        Assert.Empty(Entries(true, await Query(true, settings, new() { FileStatusFilter = "present" })));
        Assert.Single(Entries(true, await Query(true, settings, new()
        {
            FileStatusFilter = "present", ExcludedFilters = ["file-status-filter"]
        })));
        Dictionary<string, int> counts = LibraryFilterCountBuilder.Build(settings.SonarrSnapshots[0])["file-status-filter"];
        Assert.Equal(1, counts["missing"]);
        Assert.False(counts.ContainsKey("present"));
    }

    private static JsonElement[] Entries(bool sonarr, JsonElement response)
    {
        IEnumerable<JsonElement> rows = response.GetProperty("Instances")[0].GetProperty("Rows").EnumerateArray();
        return sonarr ? rows.SelectMany(series => series.GetProperty("Seasons").EnumerateArray())
            .SelectMany(season => season.GetProperty("Episodes").EnumerateArray()).ToArray() : rows.ToArray();
    }

    private static async Task<JsonElement> Query(bool sonarr, AppSettings settings, LibraryRequestFilter filter)
    {
        filter.SonarrInstanceFilter = ["__all__"];
        filter.RadarrInstanceFilter = ["__all__"];
        Type endpoints = sonarr ? typeof(SonarrEndpoints) : typeof(RadarrEndpoints);
        Task task = (Task)endpoints.GetMethod("PostLibraryAsync", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [filter, new SettingsStub(settings), CancellationToken.None])!;
        await task;
        object result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        object ok = result.GetType().GetProperty("Result")!.GetValue(result)!;
        return JsonSerializer.SerializeToElement(ok.GetType().GetProperty("Value")!.GetValue(ok)!);
    }

    private sealed class SettingsStub(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class ArrHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.RequestUri!.AbsolutePath switch
            {
                "/api/v3/qualityprofile" => """[{"id":1,"name":"HD","cutoffFormatScore":10}]""",
                "/api/v3/tag" => """[{"id":1,"label":"favorite"}]""",
                "/api/v3/movie" => """[{"id":1,"title":"Present","qualityProfileId":1,"hasFile":true,"movieFileId":10},{"id":2,"title":"Missing","qualityProfileId":1,"hasFile":false,"status":"released","genres":["Drama"],"tags":[1],"monitored":true,"inCinemas":"2019-01-01T00:00:00Z","digitalRelease":"2020-01-01T00:00:00Z"},{"id":3,"title":"Future","hasFile":false,"digitalRelease":"9999-01-01T00:00:00Z"}]""",
                "/api/v3/series" => """[{"id":1,"title":"Series","qualityProfileId":1,"genres":["Drama"],"monitored":true}]""",
                "/api/v3/episode" => """[{"id":1,"title":"Present","seasonNumber":1,"episodeNumber":1,"hasFile":true,"episodeFileId":10,"monitored":true,"airDateUtc":"2020-01-01T00:00:00Z"},{"id":2,"title":"Missing","seasonNumber":1,"episodeNumber":2,"hasFile":false,"monitored":true,"airDateUtc":"2020-01-01T00:00:00Z"},{"id":3,"title":"Future","seasonNumber":1,"episodeNumber":3,"hasFile":false,"monitored":false,"airDateUtc":"9999-01-01T00:00:00Z"},{"id":4,"title":"Special","seasonNumber":0,"episodeNumber":1,"hasFile":false,"monitored":true,"airDateUtc":"2020-01-01T00:00:00Z"}]""",
                "/api/v3/episodefile" => File,
                "/api/v3/moviefile" when request.RequestUri.Query == "?movieId=1" => File,
                _ => throw new InvalidOperationException($"Unexpected request {request.RequestUri}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }

        private const string File = """[{"id":10,"customFormatScore":5,"relativePath":"file.mkv","size":1000,"languages":[{"name":"English"}],"mediaInfo":{"videoCodec":"HEVC","resolution":"1920x1080"}}]""";
    }
}
