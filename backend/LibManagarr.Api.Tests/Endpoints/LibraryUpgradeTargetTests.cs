using System.Reflection;
using System.Text.Json;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Endpoints;

public sealed class LibraryUpgradeTargetTests
{
    [Theory]
    [InlineData(false, "below", new[] { 1, 4 })]
    [InlineData(true, "below", new[] { 1, 4 })]
    [InlineData(false, "reached", new[] { 2, 3 })]
    [InlineData(true, "reached", new[] { 2, 3 })]
    [InlineData(false, "all", new[] { 1, 2, 3, 4, 5 })]
    [InlineData(true, "all", new[] { 1, 2, 3, 4, 5 })]
    public async Task Library_ShouldCompareEachFileWithItsOwnProfile(bool sonarr, string selection, int[] expected)
    {
        AppSettings settings = Settings();
        LibraryRequestFilter filter = new()
        {
            UpgradeTargetFilter = selection, RadarrInstanceFilter = ["__all__"], SonarrInstanceFilter = ["__all__"]
        };
        JsonElement response = await Invoke(sonarr, "PostLibraryAsync", [filter, new SettingsStub(settings), CancellationToken.None]);
        JsonElement rows = response.GetProperty("Instances")[0].GetProperty("Rows");
        Assert.Equal(expected, rows.EnumerateArray().Select(row => row.GetProperty(sonarr ? "SeriesId" : "MovieId").GetInt32()));
        foreach (JsonElement row in rows.EnumerateArray())
        {
            int id = row.GetProperty(sonarr ? "SeriesId" : "MovieId").GetInt32();
            Assert.Equal(id == 5 ? null : id == 4 ? 20 : 10, row.GetProperty("UpgradeUntilCustomFormatScore").Deserialize<int?>());
            if (sonarr)
            {
                Assert.Equal(1, row.GetProperty("TotalEpisodes").GetInt32());
                Assert.Single(row.GetProperty("Seasons").EnumerateArray());
                Assert.Single(row.GetProperty("Seasons")[0].GetProperty("Episodes").EnumerateArray());
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Counts_ShouldRebuildVersionTwoCacheAndCountUnknownTargetsOnlyInAll(bool sonarr)
    {
        AppSettings settings = Settings();
        settings.LibraryFilterValues = [new() { InstanceId = "first", Version = 2 }];
        Type endpoints = sonarr ? typeof(SonarrEndpoints) : typeof(RadarrEndpoints);
        MethodInfo method = endpoints.GetMethod("PostFilterValuesAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        object request = Activator.CreateInstance(method.GetParameters()[0].ParameterType, true)!;
        request.GetType().GetProperty("InstanceIds")!.SetValue(request, new[] { "first" });
        JsonElement response = await Invoke(sonarr, "PostFilterValuesAsync", [request, new SettingsStub(settings)]);
        JsonElement counts = response.GetProperty("Counts").GetProperty("upgrade-target-filter");
        Assert.Equal(5, counts.GetProperty("__all__").GetInt32());
        Assert.Equal(2, counts.GetProperty("below").GetInt32());
        Assert.Equal(2, counts.GetProperty("reached").GetInt32());
        Assert.Equal(2, Assert.Single(settings.LibraryFilterValues).Version);
    }

    [Fact]
    public async Task Sonarr_ShouldCombineScoreAndMediaConditionsOnTheSameEpisode()
    {
        AppSettings settings = Settings();
        settings.SonarrSnapshots[0].Series = [new()
        {
            SeriesId = 1, Profile = new() { UpgradeUntilCustomFormatScore = 10 },
            Seasons = [new() { SeasonNumber = 1, Episodes = [
                new() { File = new() { CustomFormatScore = 9, VideoCodec = "AV1" } },
                new() { File = new() { CustomFormatScore = 10, VideoCodec = "HEVC" } },
                new() { EpisodeNumber = 3, Title = "Matching", File = new() { CustomFormatScore = 8, VideoCodec = "HEVC" } }
            ] }, new() { SeasonNumber = 2, Episodes = [new() { File = new() { CustomFormatScore = 10 } }] }]
        }];
        LibraryRequestFilter filter = new() { UpgradeTargetFilter = "below", VideoCodecs = ["HEVC"], SonarrInstanceFilter = ["__all__"] };
        JsonElement response = await Invoke(true, "PostLibraryAsync", [filter, new SettingsStub(settings), CancellationToken.None]);
        JsonElement series = Assert.Single(response.GetProperty("Instances")[0].GetProperty("Rows").EnumerateArray());
        Assert.Equal(1, series.GetProperty("TotalEpisodes").GetInt32());
        JsonElement season = Assert.Single(series.GetProperty("Seasons").EnumerateArray());
        Assert.Equal(1, season.GetProperty("Number").GetInt32());
        JsonElement episode = Assert.Single(season.GetProperty("Episodes").EnumerateArray());
        Assert.Equal("Matching", episode.GetProperty("Title").GetString());
    }

    [Fact]
    public void Filter_ShouldSupportNegativeScoresZeroTargetsAndExclusion()
    {
        LibraryRequestFilter filter = new() { UpgradeTargetFilter = "below", ScoreMin = -5, ScoreMax = -1 };
        LibraryFilterMatcher matcher = new(filter);
        RadarrMovieSnapshot movie = new() { Profile = new() { UpgradeUntilCustomFormatScore = 0 }, File = new() { CustomFormatScore = -1 } };
        Assert.True(matcher.MatchesMovie(movie));
        movie.File.CustomFormatScore = -6;
        Assert.False(matcher.MatchesMovie(movie));
        filter.ScoreMin = null;
        filter.ScoreMax = null;
        filter.ExcludedFilters = ["upgrade-target-filter"];
        matcher = new(filter);
        Assert.False(matcher.MatchesMovie(movie));
        movie.File.CustomFormatScore = 0;
        Assert.True(matcher.MatchesMovie(movie));
        filter.UpgradeTargetFilter = "reached";
        Assert.False(new LibraryFilterMatcher(filter).MatchesMovie(movie));
        movie.Profile.UpgradeUntilCustomFormatScore = null;
        Assert.True(new LibraryFilterMatcher(filter).MatchesMovie(movie));
    }

    private static AppSettings Settings()
    {
        (int Score, int? Target)[] values = [(9, 10), (10, 10), (11, 10), (10, 20), (-2, null)];
        return new()
        {
            RadarrInstances = [new() { Id = "first" }], SonarrInstances = [new() { Id = "first" }],
            RadarrSnapshots = [new() { InstanceId = "first", Movies = values.Select((value, index) => new RadarrMovieSnapshot
            {
                MovieId = index + 1, Title = $"Item {index + 1}", Profile = new() { UpgradeUntilCustomFormatScore = value.Target },
                File = new() { CustomFormatScore = value.Score }
            }).ToList() }],
            SonarrSnapshots = [new() { InstanceId = "first", Series = values.Select((value, index) => new SonarrSeriesSnapshot
            {
                SeriesId = index + 1, Title = $"Item {index + 1}", Profile = new() { UpgradeUntilCustomFormatScore = value.Target },
                Seasons = [new() { SeasonNumber = 1, Episodes = [new() { EpisodeNumber = 1, File = new() { CustomFormatScore = value.Score } }] }]
            }).ToList() }]
        };
    }

    private static async Task<JsonElement> Invoke(bool sonarr, string name, object[] args)
    {
        Type endpoints = sonarr ? typeof(SonarrEndpoints) : typeof(RadarrEndpoints);
        Task task = (Task)endpoints.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;
        await task;
        object result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        object ok = result.GetType().GetProperty("Result")!.GetValue(result)!;
        return JsonSerializer.SerializeToElement(ok.GetType().GetProperty("Value")!.GetValue(ok)!);
    }

    private sealed class SettingsStub(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Library queries must not write settings.");
    }
}
