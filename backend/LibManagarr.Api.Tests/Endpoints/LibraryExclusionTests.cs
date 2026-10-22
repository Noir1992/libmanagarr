using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Endpoints;

public sealed class LibraryExclusionTests
{
    private static readonly DateTimeOffset Date = new(2020, 6, 15, 12, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> Conditions()
    {
        foreach (bool sonarr in new[] { false, true })
        foreach (string id in new[] { "status-filter", "genre-filter", "tag-filter", "profile-filter",
                     "audio-language-filter", "subtitle-language-filter", "monitored-filter", "aired-filter", "score-range", "aired-range" })
            yield return [sonarr, !sonarr && id == "monitored-filter" ? "episode-monitored-filter" : id];
    }

    [Theory]
    [MemberData(nameof(Conditions))]
    public async Task Exclusion_ShouldInvertOnlyTheSelectedCondition(bool sonarr, string id)
    {
        LibraryRequestFilter filter = new() { RadarrInstanceFilter = ["first"], SonarrInstanceFilter = ["first"] };
        switch (id)
        {
            case "status-filter": filter.Statuses = ["released"]; break;
            case "genre-filter": filter.Genres = ["drama"]; break;
            case "tag-filter": filter.Tags = ["favorite"]; break;
            case "profile-filter": filter.Profiles = ["HD"]; break;
            case "audio-language-filter": filter.AudioLanguages = ["GERMAN"]; break;
            case "subtitle-language-filter": filter.SubtitleLanguages = ["german"]; break;
            case "monitored-filter": filter.MonitoredFilter = "monitored"; break;
            case "episode-monitored-filter": filter.EpisodeMonitoredFilter = "monitored"; break;
            case "aired-filter": filter.AiredFilter = "aired"; break;
            case "score-range": filter.ScoreMin = 100; filter.ScoreMax = 100; break;
            case "aired-range": filter.AiredFrom = "2020-06-15"; filter.AiredTo = "2020-06-15"; break;
        }
        AppSettings settings = CreateSettings();
        int[] expectedIncluded = id switch
        {
            "audio-language-filter" => [1, 3],
            "subtitle-language-filter" => [1, 4],
            "aired-filter" => [1, 3, 4],
            "score-range" or "aired-range" => [1, 4],
            _ => [1, 3, 4]
        };
        Assert.Equal(expectedIncluded, await QueryIds(sonarr, filter, settings));

        filter.ExcludedFilters = [id];
        Assert.Equal(Enumerable.Range(1, 4).Except(expectedIncluded), await QueryIds(sonarr, filter, settings));
    }

    [Theory]
    [InlineData(false, false, new[] { 2 })]
    [InlineData(true, false, new[] { 2 })]
    [InlineData(false, true, new[] { 2, 3 })]
    [InlineData(true, true, new[] { 2, 3 })]
    public async Task Languages_ShouldSupportBothUserExamplesAndRejectMixedTracks(bool sonarr, bool positiveAudio, int[] expected)
    {
        LibraryRequestFilter filter = new()
        {
            RadarrInstanceFilter = ["first"], SonarrInstanceFilter = ["first"],
            AudioLanguages = positiveAudio ? ["Japanese"] : ["German"],
            SubtitleLanguages = ["German"],
            ExcludedFilters = positiveAudio
                ? ["subtitle-language-filter"]
                : ["audio-language-filter", "subtitle-language-filter"]
        };

        Assert.Equal(expected, await QueryIds(sonarr, filter, CreateSettings()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleExcludedLanguages_ShouldRejectAnySelectedTrack(bool sonarr)
    {
        LibraryRequestFilter filter = new()
        {
            RadarrInstanceFilter = ["first"], SonarrInstanceFilter = ["first"],
            AudioLanguages = ["German", "Japanese"], ExcludedFilters = ["audio-language-filter"]
        };
        Assert.Equal(new[] { 4 }, await QueryIds(sonarr, filter, CreateSettings()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Defaults_ShouldRemainUnrestrictedEvenInExcludeMode(bool sonarr)
    {
        LibraryRequestFilter filter = new()
        {
            RadarrInstanceFilter = ["__all__"], SonarrInstanceFilter = ["__all__"],
            Statuses = ["__all__"], Genres = ["__all__"], Tags = ["__all__"], Profiles = ["__all__"],
            AudioLanguages = ["__all__"], SubtitleLanguages = ["__all__"],
            AiredFilter = "all", MonitoredFilter = "all",
            ExcludedFilters = ["status-filter", "genre-filter", "tag-filter", "profile-filter", "audio-language-filter",
                "subtitle-language-filter", "aired-filter", "monitored-filter", "score-range", "aired-range"]
        };
        Assert.Equal(new[] { 1, 2, 3, 4 }, await QueryIds(sonarr, filter, CreateSettings()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyExcludedSelection_ShouldMatchEverythingIncludingMissingTracks(bool sonarr)
    {
        LibraryRequestFilter filter = new()
        {
            RadarrInstanceFilter = ["first"], SonarrInstanceFilter = ["first"],
            AudioLanguages = [], ExcludedFilters = ["audio-language-filter"]
        };
        Assert.Equal(new[] { 1, 2, 3, 4 }, await QueryIds(sonarr, filter, CreateSettings()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExcludedInstances_ShouldReturnOnlyTheOtherConfiguredInstances(bool sonarr)
    {
        AppSettings settings = CreateSettings();
        (sonarr ? settings.SonarrInstances : settings.RadarrInstances).Add(new() { Id = "second", Name = "Second" });
        LibraryRequestFilter filter = new()
        {
            RadarrInstanceFilter = ["first"], SonarrInstanceFilter = ["first"],
            ExcludedFilters = ["radarr-instance-filter", "sonarr-instance-filter"]
        };
        object response = await Query(sonarr, filter, settings);
        string[] ids = sonarr
            ? Assert.IsType<Ok<SonarrLibraryBatchResponse>>(response).Value!.Instances.Select(i => i.InstanceId).ToArray()
            : Assert.IsType<Ok<RadarrLibraryBatchResponse>>(response).Value!.Instances.Select(i => i.InstanceId).ToArray();
        Assert.Equal(new[] { "second" }, ids);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExcludingAllInstances_ShouldMatchNoInstance(bool sonarr)
    {
        LibraryRequestFilter filter = new()
        {
            RadarrInstanceFilter = ["__all__"], SonarrInstanceFilter = ["__all__"],
            ExcludedFilters = [sonarr ? "sonarr-instance-filter" : "radarr-instance-filter"]
        };
        // Same contract as excluding every configured instance by id: no instance remains, so the endpoint reports not found.
        Assert.IsType<NotFound<ProblemDetails>>(await Query(sonarr, filter, CreateSettings()));
    }

    [Fact]
    public async Task Sonarr_ShouldApplyExclusionToEachEpisodeAndRecalculateParentCounts()
    {
        AppSettings settings = CreateSettings();
        SonarrSeriesSnapshot first = settings.SonarrSnapshots[0].Series[0];
        first.Seasons.Add(new()
        {
            SeasonNumber = 2,
            Episodes = [new() { EpisodeNumber = 2, File = new() { SubtitleLanguages = ["French"] } },
                new() { EpisodeNumber = 3, File = new() { SubtitleLanguages = ["French", "German"] } }]
        });
        LibraryRequestFilter filter = new()
        {
            SonarrInstanceFilter = ["first"], SubtitleLanguages = ["German"], ExcludedFilters = ["subtitle-language-filter"]
        };
        object response = await Query(true, filter, settings);
        SonarrLibraryEntryResponse series = Assert.IsType<Ok<SonarrLibraryBatchResponse>>(response).Value!.Instances[0].Rows.Single(s => s.SeriesId == 1);
        SonarrSeasonResponse season = Assert.Single(series.Seasons);
        Assert.Equal(2, season.Number);
        Assert.Equal(2, Assert.Single(season.Episodes).Number);
        Assert.Equal(1, series.TotalEpisodes);
        Assert.Equal(0, series.AiredEpisodes);
    }

    private static AppSettings CreateSettings()
    {
        AppSettings settings = new()
        {
            RadarrInstances = [new() { Id = "first" }], SonarrInstances = [new() { Id = "first" }],
            RadarrSnapshots = [new() { InstanceId = "first" }], SonarrSnapshots = [new() { InstanceId = "first" }]
        };
        for (int id = 1; id <= 4; id++)
        {
            MediaFileSnapshot file = new()
            {
                AudioLanguages = id switch { 1 => ["German"], 2 => ["Japanese"], 3 => ["Japanese", "German"], _ => [] },
                SubtitleLanguages = id switch { 1 => ["German"], 2 => ["English"], 3 => [], _ => ["English", "German"] },
                CustomFormatScore = id switch { 2 => 99, 3 => 101, _ => 100 }
            };
            DateTimeOffset? airDate = id switch { 2 => null, 3 => Date.AddDays(-1), _ => Date };
            settings.RadarrSnapshots[0].Movies.Add(new()
            {
                MovieId = id, Title = $"Title {id}", Status = id == 2 ? "announced" : "released",
                Genres = id == 2 ? ["comedy"] : ["drama"], Tags = id == 2 ? [] : ["favorite"],
                Profile = new() { Name = id == 2 ? "SD" : "HD" }, Monitored = id != 2,
                HasAired = id != 2, AirDateUtc = airDate, File = file
            });
            settings.SonarrSnapshots[0].Series.Add(new()
            {
                SeriesId = id, Title = $"Title {id}", Status = id == 2 ? "announced" : "released",
                Genres = id == 2 ? ["comedy"] : ["drama"], Tags = id == 2 ? [] : ["favorite"],
                Profile = new() { Name = id == 2 ? "SD" : "HD" }, Monitored = id != 2,
                Seasons = [new() { SeasonNumber = 1, Episodes = [new() { EpisodeNumber = 1, AirDateUtc = airDate, File = file }] }]
            });
        }
        return settings;
    }

    private static async Task<int[]> QueryIds(bool sonarr, LibraryRequestFilter filter, AppSettings settings)
    {
        object result = await Query(sonarr, filter, settings);
        return sonarr
            ? Assert.IsType<Ok<SonarrLibraryBatchResponse>>(result).Value!.Instances.SelectMany(i => i.Rows).Select(s => s.SeriesId).ToArray()
            : Assert.IsType<Ok<RadarrLibraryBatchResponse>>(result).Value!.Instances.SelectMany(i => i.Rows).Select(m => m.MovieId).ToArray();
    }

    private static async Task<object> Query(bool sonarr, LibraryRequestFilter filter, AppSettings settings)
    {
        MethodInfo method = (sonarr ? typeof(SonarrEndpoints) : typeof(RadarrEndpoints))
            .GetMethod("PostLibraryAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        object task = method.Invoke(null, [filter, new SettingsStub(settings), CancellationToken.None])!;
        if (sonarr)
            return (await (Task<Results<Ok<SonarrLibraryBatchResponse>, NotFound<ProblemDetails>>>)task).Result;
        return (await (Task<Results<Ok<RadarrLibraryBatchResponse>, NotFound<ProblemDetails>>>)task).Result;
    }

    private sealed class SettingsStub(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
