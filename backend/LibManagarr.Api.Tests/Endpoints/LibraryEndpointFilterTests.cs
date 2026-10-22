using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Endpoints;

public sealed class LibraryEndpointFilterTests
{
    private static readonly DateTimeOffset AirDate = new(2020, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SyncedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] ListFilters = ["status", "genre", "tag", "profile", "audio", "subtitle"];

    public static IEnumerable<object[]> IndividualFilters()
    {
        foreach (bool sonarr in new[] { false, true })
        {
            yield return [sonarr, "status", new[] { 2 }];
            yield return [sonarr, "genre", new[] { 3 }];
            yield return [sonarr, "tag", new[] { 4 }];
            yield return [sonarr, "profile", new[] { 5 }];
            yield return [sonarr, "audio", new[] { 6, 15 }];
            yield return [sonarr, "subtitle", new[] { 7, 15 }];
            yield return [sonarr, "monitored", new[] { 8 }];
            yield return [sonarr, "notMonitored", Enumerable.Range(1, 15).Except([8]).ToArray()];
            yield return [sonarr, "aired", new[] { 9, 10 }];
            yield return [sonarr, "notAired", Enumerable.Range(1, 15).Except([9, 10]).ToArray()];
            yield return [sonarr, "scoreMin", new[] { 11 }];
            yield return [sonarr, "scoreMax", new[] { 12 }];
            yield return [sonarr, "scoreExact", new[] { 11, 12 }];
            yield return [sonarr, "airedFrom", new[] { 10, 13 }];
            yield return [sonarr, "airedTo", new[] { 9, 10, 14 }];
            yield return [sonarr, "airedRange", new[] { 9, 10, 13, 14 }];
            yield return [sonarr, "invalidDates", Array.Empty<int>()];
            yield return [sonarr, "reversedScore", Enumerable.Range(1, 15).ToArray()];
            yield return [sonarr, "reversedDates", Enumerable.Range(1, 15).ToArray()];
        }
    }

    [Theory]
    [MemberData(nameof(IndividualFilters))]
    public async Task PostLibrary_ShouldApplyEachFilterIndependently(bool sonarr, string filterName, int[] excludedIds)
    {
        LibraryRequestFilter filter = CreateFilter();
        ApplyFilter(filter, filterName);

        object response = await InvokeAsync(sonarr, filter, CreateSettings());

        Assert.Equal(Enumerable.Range(1, 15).Except(excludedIds), GetIds(sonarr, response));
    }

    public static IEnumerable<object[]> ListFilterCases()
    {
        foreach (bool sonarr in new[] { false, true })
        foreach (string field in ListFilters)
        foreach (string mode in new[] { "null", "all", "empty", "unknown", "multiple", "bothValues" })
            yield return [sonarr, field, mode];
    }

    [Theory]
    [MemberData(nameof(ListFilterCases))]
    public async Task PostLibrary_ShouldRespectListSelectionSemantics(bool sonarr, string field, string mode)
    {
        LibraryRequestFilter filter = CreateFilter();
        string[]? values = mode switch
        {
            "null" => null,
            "all" => ["__all__"],
            "empty" => [],
            "unknown" => ["does-not-exist"],
            "bothValues" => [MatchingValue(field).ToUpperInvariant(), AlternativeValue(field).ToUpperInvariant()],
            _ => ["does-not-exist", MatchingValue(field).ToUpperInvariant()]
        };
        SetListFilter(filter, field, values);

        object response = await InvokeAsync(sonarr, filter, CreateSettings());

        int[] excluded = field switch
        {
            "status" => [2], "genre" => [3], "tag" => [4], "profile" => [5],
            "audio" => [6, 15], _ => [7, 15]
        };
        IEnumerable<int> expected = mode switch
        {
            "empty" or "unknown" => [],
            "multiple" => Enumerable.Range(1, 15).Except(excluded),
            "bothValues" when field is "audio" or "subtitle" => Enumerable.Range(1, 14),
            _ => Enumerable.Range(1, 15)
        };
        Assert.Equal(expected, GetIds(sonarr, response));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostLibrary_ShouldCombineFiltersWithAnd(bool sonarr)
    {
        LibraryRequestFilter filter = CreateFilter();
        foreach (string field in ListFilters)
            ApplyFilter(filter, field);
        foreach (string field in new[] { "monitored", "aired", "scoreExact", "airedRange" })
            ApplyFilter(filter, field);

        object response = await InvokeAsync(sonarr, filter, CreateSettings());

        Assert.Equal(new[] { 1 }, GetIds(sonarr, response));
    }

    [Theory]
    [InlineData(false, "selected")]
    [InlineData(true, "selected")]
    [InlineData(false, "all")]
    [InlineData(true, "all")]
    [InlineData(false, "empty")]
    [InlineData(true, "empty")]
    [InlineData(false, "unknown")]
    [InlineData(true, "unknown")]
    public async Task PostLibrary_ShouldSelectOnlyRequestedInstances(bool sonarr, string mode)
    {
        AppSettings settings = CreateSettings();
        List<ArrInstanceSettings> instances = sonarr ? settings.SonarrInstances : settings.RadarrInstances;
        instances.Add(new ArrInstanceSettings { Id = "second", Name = "Second instance" });
        LibraryRequestFilter filter = CreateFilter();
        string[] selection = mode switch
        {
            "selected" => ["second"], "all" => ["__all__"], "empty" => [], _ => ["unknown"]
        };
        filter.SonarrInstanceFilter = selection;
        filter.RadarrInstanceFilter = selection;

        object response = await InvokeAsync(sonarr, filter, settings);

        if (mode is "empty" or "unknown")
        {
            ProblemDetails problem = Assert.IsType<NotFound<ProblemDetails>>(response).Value!;
            Assert.Equal(404, problem.Status);
            Assert.Equal(sonarr ? "Sonarr instance not found" : "Radarr instance not found", problem.Title);
            return;
        }

        if (sonarr)
        {
            SonarrLibraryBatchResponse batch = Assert.IsType<Ok<SonarrLibraryBatchResponse>>(response).Value!;
            Assert.Equal(mode == "all" ? new[] { "first", "second" } : ["second"], batch.Instances.Select(i => i.InstanceId));
            Assert.Equal("Second instance", batch.Instances.Last().InstanceName);
            Assert.Empty(batch.Instances.Last().Rows);
            Assert.Equal(mode == "all" ? SyncedAt : (DateTimeOffset?)null, batch.SyncedAt);
        }
        else
        {
            RadarrLibraryBatchResponse batch = Assert.IsType<Ok<RadarrLibraryBatchResponse>>(response).Value!;
            Assert.Equal(mode == "all" ? new[] { "first", "second" } : ["second"], batch.Instances.Select(i => i.InstanceId));
            Assert.Equal("Second instance", batch.Instances.Last().InstanceName);
            Assert.Empty(batch.Instances.Last().Rows);
            Assert.Equal(mode == "all" ? SyncedAt : (DateTimeOffset?)null, batch.SyncedAt);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostLibrary_ShouldForwardCancellationAndReadSettingsOnce(bool sonarr)
    {
        using CancellationTokenSource source = new();
        StubSettingsService settingsService = new(CreateSettings());

        await InvokeAsync(sonarr, CreateFilter(), settingsService, source.Token);

        Assert.Equal(1, settingsService.GetCallCount);
        Assert.Equal(source.Token, settingsService.LastCancellationToken);
    }

    [Fact]
    public async Task PostRadarrLibrary_ShouldMapMovieFieldsAndSortByTitle()
    {
        object response = await InvokeAsync(false, CreateFilter(), CreateSettings());
        RadarrLibraryBatchResponse batch = Assert.IsType<Ok<RadarrLibraryBatchResponse>>(response).Value!;
        RadarrLibraryInstanceResult instance = Assert.Single(batch.Instances);
        RadarrLibraryEntryResponse movie = instance.Rows[0];

        Assert.Equal(SyncedAt, batch.SyncedAt);
        Assert.Equal("first", instance.InstanceId);
        Assert.Equal("First instance", instance.InstanceName);
        Assert.Equal(Enumerable.Range(1, 15), instance.Rows.Select(m => m.MovieId));
        Assert.Equal("Snapshot instance", movie.InstanceName);
        Assert.Equal("Title 01", movie.MovieTitle);
        Assert.Equal("slug-1", movie.MovieTitleSlug);
        Assert.Equal("HD", movie.QualityProfileName);
        Assert.Equal("released", movie.Status);
        Assert.Equal(AirDate, movie.AirDateUtc);
        Assert.True(movie.Monitored);
        Assert.True(movie.HasFile);
        Assert.Equal(100, movie.CustomFormatScore);
        Assert.Equal("1080p", movie.Resolution);
        Assert.Equal(1024L, movie.Size);
        Assert.True(instance.Rows.Last().HasFile);
        Assert.Null(instance.Rows.Last().Size);
    }

    [Fact]
    public async Task PostSonarrLibrary_ShouldFilterNestedEpisodesRemoveEmptyParentsAndRecalculateCounts()
    {
        AppSettings settings = CreateSettings();
        SonarrSeriesSnapshot series = settings.SonarrSnapshots[0].Series.Single(s => s.SeriesId == 1);
        series.Seasons.Clear();
        series.Seasons.Add(new SonarrSeasonSnapshot
        {
            SeasonNumber = 2, Monitored = false,
            Episodes = [CreateEpisode(3, 100), CreateEpisode(1, 100), CreateEpisode(2, 99)]
        });
        series.Seasons.Add(new SonarrSeasonSnapshot { SeasonNumber = 3, Episodes = [CreateEpisode(1, 99)] });
        series.Seasons.Add(new SonarrSeasonSnapshot { SeasonNumber = 1, Monitored = true, Episodes = [CreateEpisode(4, 100, false)] });
        LibraryRequestFilter filter = CreateFilter();
        filter.ScoreMin = 100;
        filter.ScoreMax = 100;
        filter.Statuses = ["released"];
        filter.Genres = ["drama"];
        filter.Tags = ["favorite"];
        filter.Profiles = ["HD"];
        filter.MonitoredFilter = "monitored";
        settings.SonarrSnapshots[0].Series.RemoveAll(s => s.SeriesId != 1 && s.SeriesId != 11);

        object response = await InvokeAsync(true, filter, settings);

        SonarrLibraryBatchResponse batch = Assert.IsType<Ok<SonarrLibraryBatchResponse>>(response).Value!;
        SonarrLibraryInstanceResult instance = Assert.Single(batch.Instances);
        SonarrLibraryEntryResponse row = Assert.Single(instance.Rows);
        Assert.Equal(SyncedAt, batch.SyncedAt);
        Assert.Equal(1, row.SeriesId);
        Assert.Equal("Snapshot instance", row.InstanceName);
        Assert.Equal("Title 01", row.SeriesTitle);
        Assert.Equal("slug-1", row.SeriesTitleSlug);
        Assert.Equal("HD", row.QualityProfileName);
        Assert.Equal("released", row.Status);
        Assert.True(row.Monitored);
        Assert.Equal(2, Assert.Single(row.Seasons).Number);
        Assert.False(row.Seasons[0].Monitored);
        Assert.Equal(new[] { 1, 3 }, row.Seasons[0].Episodes.Select(e => e.Number));
        Assert.Equal(2, row.TotalEpisodes);
        Assert.Equal(2, row.AiredEpisodes);
        SonarrEpisodeResponse episode = row.Seasons[0].Episodes[0];
        Assert.Equal(1, episode.Number);
        Assert.Equal("Episode 1", episode.Title);
        Assert.Equal(AirDate, episode.AirDateUtc);
        Assert.Equal(100, episode.CustomFormatScore);
        Assert.True(episode.HasFile);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostLibrary_ShouldIncludeEntireLastDayOfDateRange(bool sonarr)
    {
        AppSettings settings = CreateSettings(new DateTimeOffset(2020, 6, 16, 0, 0, 0, TimeSpan.Zero).AddTicks(-1));
        LibraryRequestFilter filter = CreateFilter();
        filter.AiredFrom = "2020-06-15T00:00:00Z";
        filter.AiredTo = "2020-06-15T00:00:00Z";

        object response = await InvokeAsync(sonarr, filter, settings);

        Assert.Equal(Enumerable.Range(1, 15).Except([9, 10, 13, 14]), GetIds(sonarr, response));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostLibrary_ShouldInterpretDateOnlyRangeAsUtcCalendarDays(bool sonarr)
    {
        AppSettings settings = CreateSettings(new DateTimeOffset(2020, 6, 15, 23, 0, 0, TimeSpan.Zero));
        LibraryRequestFilter filter = CreateFilter();
        filter.AiredFrom = "2020-06-15";
        filter.AiredTo = "2020-06-15";

        object response = await InvokeAsync(sonarr, filter, settings);

        Assert.Equal(Enumerable.Range(1, 15).Except([9, 10, 13, 14]), GetIds(sonarr, response));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostLibrary_ShouldIncludeFirstInstantOfDateRange(bool sonarr)
    {
        AppSettings settings = CreateSettings(new DateTimeOffset(2020, 6, 15, 0, 0, 0, TimeSpan.Zero));
        LibraryRequestFilter filter = CreateFilter();
        filter.AiredFrom = "2020-06-15T00:00:00Z";
        filter.AiredTo = "2020-06-15T00:00:00Z";

        object response = await InvokeAsync(sonarr, filter, settings);

        Assert.Equal(Enumerable.Range(1, 15).Except([9, 10, 13, 14]), GetIds(sonarr, response));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostLibrary_ShouldReturnEmptyRowsForEmptySnapshots(bool sonarr)
    {
        AppSettings settings = CreateSettings();
        settings.RadarrSnapshots[0].Movies.Clear();
        settings.SonarrSnapshots[0].Series.Clear();

        object response = await InvokeAsync(sonarr, CreateFilter(), settings);

        Assert.Empty(GetIds(sonarr, response));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostLibrary_ShouldReturnNotFoundWhenNoInstancesAreConfigured(bool sonarr)
    {
        object response = await InvokeAsync(sonarr, CreateFilter(), new AppSettings());

        ProblemDetails problem = Assert.IsType<NotFound<ProblemDetails>>(response).Value!;
        Assert.Equal(404, problem.Status);
        Assert.Equal(sonarr ? "Sonarr instance not found" : "Radarr instance not found", problem.Title);
        Assert.Equal(sonarr
            ? "No Sonarr instances found for the requested instance IDs."
            : "No Radarr instances found for the requested instance IDs.", problem.Detail);
    }

    private static LibraryRequestFilter CreateFilter() => new()
    {
        RadarrInstanceFilter = ["first"], SonarrInstanceFilter = ["first"]
    };

    public static IEnumerable<object[]> DateRangeEndpointCases()
    {
        foreach (bool sonarr in new[] { false, true })
        foreach (bool batch in new[] { false, true })
        foreach (int scenario in Enumerable.Range(0, 4))
            yield return [sonarr, batch, scenario];
    }

    [Theory]
    [MemberData(nameof(DateRangeEndpointCases))]
    public async Task Library_ShouldUseCompleteCalendarDaysAndPreserveExplicitOffsets(bool sonarr, bool batch, int scenario)
    {
        string date = scenario switch
        {
            0 => "2020-06-15",
            1 => "2020-06-15T00:00:00Z",
            2 => "2020-06-15T00:00:00+02:00",
            _ => "2020-06-15T00:00:00-04:00"
        };
        DateTimeOffset matchingDate = scenario switch
        {
            0 => new DateTimeOffset(2020, 6, 15, 23, 0, 0, TimeSpan.Zero),
            1 => new DateTimeOffset(2020, 6, 16, 0, 0, 0, TimeSpan.Zero).AddTicks(-1),
            2 => new DateTimeOffset(2020, 6, 15, 21, 59, 59, TimeSpan.Zero).AddTicks(9999999),
            _ => new DateTimeOffset(2020, 6, 16, 3, 59, 59, TimeSpan.Zero).AddTicks(9999999)
        };
        LibraryRequestFilter filter = CreateFilter();
        filter.AiredFrom = date;
        filter.AiredTo = date;
        AppSettings settings = CreateSettings(matchingDate);

        IEnumerable<int> actual;
        if (batch)
        {
            actual = GetIds(sonarr, await InvokeAsync(sonarr, filter, settings));
        }
        else
        {
            object response = await InvokeGetAsync(sonarr, filter, settings);
            actual = sonarr
                ? Assert.IsType<Ok<SonarrLibraryResponse>>(response).Value!.Rows.Select(s => s.SeriesId)
                : Assert.IsType<Ok<RadarrLibraryResponse>>(response).Value!.Rows.Select(m => m.MovieId);
        }

        Assert.Equal(Enumerable.Range(1, 15).Except([9, 10, 13, 14]), actual);
    }

    private static async Task<object> InvokeGetAsync(bool sonarr, LibraryRequestFilter filter, AppSettings settings)
    {
        Type endpointType = sonarr ? typeof(SonarrEndpoints) : typeof(RadarrEndpoints);
        MethodInfo handler = endpointType.GetMethod("GetLibraryAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] arguments =
        [
            "first", filter.AiredFilter, filter.MonitoredFilter, null, null, null, null, null, null,
            filter.ScoreMin, filter.ScoreMax, filter.AiredFrom, filter.AiredTo,
            new StubSettingsService(settings), CancellationToken.None
        ];
        if (sonarr)
        {
            var task = (Task<Results<Ok<SonarrLibraryResponse>, NotFound<ProblemDetails>>>)handler.Invoke(null, arguments)!;
            return (await task).Result;
        }
        var radarrTask = (Task<Results<Ok<RadarrLibraryResponse>, NotFound<ProblemDetails>>>)handler.Invoke(null, arguments)!;
        return (await radarrTask).Result;
    }

    private static string MatchingValue(string field) => field switch
    {
        "status" => "released", "genre" => "drama", "tag" => "favorite", "profile" => "HD",
        "audio" => "English", _ => "German"
    };

    private static string AlternativeValue(string field) => field switch
    {
        "status" => "announced", "genre" => "comedy", "tag" => "other", "profile" => "SD", _ => "French"
    };

    private static void SetListFilter(LibraryRequestFilter filter, string field, string[]? values)
    {
        switch (field)
        {
            case "status": filter.Statuses = values; break;
            case "genre": filter.Genres = values; break;
            case "tag": filter.Tags = values; break;
            case "profile": filter.Profiles = values; break;
            case "audio": filter.AudioLanguages = values; break;
            case "subtitle": filter.SubtitleLanguages = values; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

    private static void ApplyFilter(LibraryRequestFilter filter, string field)
    {
        if (ListFilters.Contains(field))
        {
            SetListFilter(filter, field, [MatchingValue(field).ToUpperInvariant()]);
            return;
        }
        switch (field)
        {
            case "monitored": case "notMonitored": filter.MonitoredFilter = field; filter.EpisodeMonitoredFilter = field; break;
            case "aired": case "notAired": filter.AiredFilter = field; break;
            case "scoreMin": filter.ScoreMin = 100; break;
            case "scoreMax": filter.ScoreMax = 100; break;
            case "scoreExact": filter.ScoreMin = 100; filter.ScoreMax = 100; break;
            case "airedFrom": filter.AiredFrom = "2020-06-15"; break;
            case "airedTo": filter.AiredTo = "2020-06-15"; break;
            case "airedRange": filter.AiredFrom = "2020-06-15"; filter.AiredTo = "2020-06-15"; break;
            case "invalidDates": filter.AiredFrom = "invalid"; filter.AiredTo = "invalid"; break;
            case "reversedScore": filter.ScoreMin = 101; filter.ScoreMax = 99; break;
            case "reversedDates": filter.AiredFrom = "2020-06-16"; filter.AiredTo = "2020-06-14"; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

    private static AppSettings CreateSettings(DateTimeOffset? matchingDate = null)
    {
        AppSettings settings = new()
        {
            RadarrInstances = [new() { Id = "first", Name = "First instance" }],
            SonarrInstances = [new() { Id = "first", Name = "First instance" }]
        };
        RadarrInstanceSnapshot radarr = new() { InstanceId = "first", InstanceName = "Snapshot instance", SyncedAt = SyncedAt };
        SonarrInstanceSnapshot sonarr = new() { InstanceId = "first", InstanceName = "Snapshot instance", SyncedAt = SyncedAt };
        foreach (int id in Enumerable.Range(1, 15).Reverse())
        {
            DateTimeOffset? date = id switch
            {
                9 => new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero),
                10 => null, 13 => AirDate.AddDays(-1), 14 => AirDate.AddDays(1), _ => matchingDate ?? AirDate
            };
            int score = id switch { 11 => 99, 12 => 101, _ => 100 };
            MediaFileSnapshot file = new()
            {
                CustomFormatScore = score,
                AudioLanguages = id == 15 ? [] : [id == 6 ? "French" : "English"],
                SubtitleLanguages = id == 15 ? [] : [id == 7 ? "French" : "German"],
                Size = id == 15 ? null : 1024, Resolution = "1080p"
            };
            radarr.Movies.Add(new RadarrMovieSnapshot
            {
                MovieId = id, Title = $"Title {id:D2}", TitleSlug = $"slug-{id}",
                Status = id == 2 ? "announced" : "released",
                Genres = [id == 3 ? "comedy" : "drama"], Tags = [id == 4 ? "other" : "favorite"],
                Profile = new() { Name = id == 5 ? "SD" : "HD" }, Monitored = id != 8,
                HasAired = id is not (9 or 10), AirDateUtc = date, File = file
            });
            sonarr.Series.Add(new SonarrSeriesSnapshot
            {
                SeriesId = id, Title = $"Title {id:D2}", TitleSlug = $"slug-{id}",
                Status = id == 2 ? "announced" : "released",
                Genres = [id == 3 ? "comedy" : "drama"], Tags = [id == 4 ? "other" : "favorite"],
                Profile = new() { Name = id == 5 ? "SD" : "HD" }, Monitored = id != 8,
                Seasons = [new() { SeasonNumber = 1, Episodes = [new() { EpisodeNumber = 1, Monitored = id != 8, AirDateUtc = date, File = file }] }]
            });
        }
        settings.RadarrSnapshots.Add(radarr);
        settings.SonarrSnapshots.Add(sonarr);
        return settings;
    }

    private static SonarrEpisodeSnapshot CreateEpisode(int number, int score, bool hasFile = true) => new()
    {
        EpisodeNumber = number, Title = $"Episode {number}", AirDateUtc = AirDate,
        HasFile = hasFile, File = new() { CustomFormatScore = score, Size = hasFile ? 1024 : null }
    };

    private static IEnumerable<int> GetIds(bool sonarr, object response)
    {
        if (sonarr)
            return Assert.Single(Assert.IsType<Ok<SonarrLibraryBatchResponse>>(response).Value!.Instances).Rows.Select(s => s.SeriesId);
        return Assert.Single(Assert.IsType<Ok<RadarrLibraryBatchResponse>>(response).Value!.Instances).Rows.Select(m => m.MovieId);
    }

    private static Task<object> InvokeAsync(bool sonarr, LibraryRequestFilter filter, AppSettings settings) =>
        InvokeAsync(sonarr, filter, new StubSettingsService(settings), CancellationToken.None);

    private static async Task<object> InvokeAsync(bool sonarr, LibraryRequestFilter filter, ISettingsService settingsService, CancellationToken token)
    {
        Type endpointType = sonarr ? typeof(SonarrEndpoints) : typeof(RadarrEndpoints);
        MethodInfo handler = endpointType.GetMethod("PostLibraryAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        if (sonarr)
        {
            var task = (Task<Results<Ok<SonarrLibraryBatchResponse>, NotFound<ProblemDetails>>>)handler.Invoke(null, [filter, settingsService, token])!;
            return (await task).Result;
        }
        var radarrTask = (Task<Results<Ok<RadarrLibraryBatchResponse>, NotFound<ProblemDetails>>>)handler.Invoke(null, [filter, settingsService, token])!;
        return (await radarrTask).Result;
    }

    private sealed class StubSettingsService(AppSettings settings) : ISettingsService
    {
        public int GetCallCount { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<AppSettings> GetAsync(CancellationToken cancellationToken)
        {
            GetCallCount++;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(settings);
        }

        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Library queries must not save settings.");
    }
}
