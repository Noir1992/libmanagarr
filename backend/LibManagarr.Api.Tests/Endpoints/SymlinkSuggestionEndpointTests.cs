using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using LibManagarr.Api.Tests.Services;

namespace LibManagarr.Api.Tests.Endpoints;

/// <summary>
/// The suggestions and the two decisions the interface can make about them. What matters here is what the
/// response is built from: the stored row for the decision, the current snapshots for the names and the
/// source file, and the current configuration revision for whether the row may still be acted on.
/// </summary>
public sealed class SymlinkSuggestionEndpointTests
{
    private const string SonarrInstance = "sonarr-1";
    private const string RadarrInstance = "radarr-1";
    private const string SeriesTitle = "Kabaneri of the Iron Fortress";
    private const string SpecialTitle = "Kabaneri of the Iron Fortress: The Battle of Unato";
    private const string SourcePath = @"\\nas\radarr\kabaneri\unato.mkv";
    private const string SeriesPath = "/media/series/Kabaneri";

    private static readonly DateTimeOffset Start = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Suggestions_IncludeTheLiveFileCheckWithoutAdoptingOrChangingStoredRows()
    {
        AppSettings settings = ExistingEpisodeFileInspectorTests.Settings();
        SymlinkSuggestion suggestion = ExistingEpisodeFileInspectorTests.Suggestion();
        InMemorySymlinkStore store = await Store(suggestion);
        string before = System.Text.Json.JsonSerializer.Serialize(await store.GetSuggestionsAsync(CancellationToken.None));
        string source = Path.Combine(settings.Symlinks.StorageAreas[0].MoviesRoot, "Film", "movie.mkv");
        string episode = Path.Combine(settings.Symlinks.StorageAreas[0].SeriesRoot, "Show", "Specials", "s00e01.mkv");
        FakeLinkFileSystem files = new();
        files.Entries[source] = null;
        files.Entries[episode] = Path.GetRelativePath(Path.GetDirectoryName(episode)!, source);
        ExistingEpisodeFileInspector inspector = ExistingEpisodeFileInspectorTests.Inspector(settings, files,
            ExistingEpisodeFileInspectorTests.Handler(episode, source));
        Ok<IReadOnlyList<SymlinkSuggestionDto>> response = await Invoke<Ok<IReadOnlyList<SymlinkSuggestionDto>>>(
            "GetSuggestionsAsync", new StubSettingsService(settings), store, inspector, CancellationToken.None);
        ExistingEpisodeFileCheck check = Assert.Single(response.Value!).ExistingFileCheck!;
        Assert.Equal(ExistingEpisodeFileStatus.Matches, check.Status);
        Assert.Equal(source, check.ActualTarget);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(await store.GetSuggestionsAsync(CancellationToken.None)));
        Assert.Empty(await store.GetLinksAsync(CancellationToken.None));
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(files.Removed);
    }

    [Fact]
    public async Task Suggestions_FromAnOlderSnapshotHaveNoInventedSeriesPath()
    {
        AppSettings settings = Settings();
        SonarrSeriesSnapshot previous = settings.SonarrSnapshots[0].Series[0];
        settings.SonarrSnapshots[0].Series[0] = new SonarrSeriesSnapshot
        {
            SeriesId = previous.SeriesId, Title = previous.Title, Seasons = previous.Seasons
        };
        SymlinkSuggestionDto dto = Assert.Single(await GetSuggestions(settings, await Store(suggestion())));
        Assert.Equal(SeriesTitle, dto.SonarrSeriesTitle);
        Assert.Null(dto.SonarrSeriesPath);
    }
    private static readonly SpecialMoviePair Pair = new(RadarrInstance, 70, SonarrInstance, 12, 5);

    [Fact]
    public async Task Suggestions_AreTheStoredRowPlusTheNamesAndFileOfTheCurrentSnapshots()
    {
        AppSettings settings = Settings();

        SymlinkSuggestionDto dto = Assert.Single(await GetSuggestions(settings, await Store(suggestion())));

        Assert.Equal("suggestion-1", dto.Id);
        Assert.Equal(SymlinkSuggestionStatus.Pending, dto.Status);
        Assert.Equal(RadarrInstance, dto.RadarrInstanceId);
        Assert.Equal(70, dto.RadarrMovieId);
        Assert.Equal(900, dto.RadarrFileId);
        Assert.Equal(SourcePath, dto.RadarrFilePath);
        Assert.Equal(SpecialTitle, dto.RadarrMovieTitle);
        Assert.Equal(SonarrInstance, dto.SonarrInstanceId);
        Assert.Equal(SeriesTitle, dto.SonarrSeriesTitle);
        Assert.Equal(SeriesPath, dto.SonarrSeriesPath);
        Assert.Equal(SpecialTitle, dto.SonarrEpisodeTitle);
        Assert.Equal(0, dto.SeasonNumber);
        Assert.Equal(3, dto.EpisodeNumber);
        Assert.Equal(SpecialMovieMatchingRules.IdenticalTitleCode, dto.Reasons.Single().Code);
        Assert.Equal(0, dto.ConfigRevision);
        Assert.Equal("1", dto.MatcherVersion);
        Assert.False(dto.Stale);
        Assert.Equal(Start, dto.CreatedAt);
    }

    [Fact]
    public async Task Suggestions_AreNewestFirstAndStableWithinOneMoment()
    {
        InMemorySymlinkStore store = await Store(
            suggestion("suggestion-b", movieId: 70),
            suggestion("suggestion-a", movieId: 71),
            suggestion("suggestion-c", movieId: 72, at: Start + TimeSpan.FromMinutes(5)));

        IReadOnlyList<SymlinkSuggestionDto> view = await GetSuggestions(Settings(), store);

        // Rows found in the same pass must not swap places on every reload, or the list jumps under the
        // pointer while somebody is reading a reason.
        Assert.Equal(["suggestion-c", "suggestion-a", "suggestion-b"], view.Select(suggestion => suggestion.Id));
    }

    [Fact]
    public async Task Suggestions_AreMarkedStaleOnceThePathsTheyNamedHaveMoved()
    {
        AppSettings settings = Settings(revision: 4);

        SymlinkSuggestionDto dto = Assert.Single(await GetSuggestions(settings, await Store(suggestion(configRevision: 3))));

        // The preview of an older path configuration would offer a source that is not there anymore.
        Assert.Equal(3, dto.ConfigRevision);
        Assert.True(dto.Stale);
    }

    [Fact]
    public async Task Suggestions_ShowNoSourceFileOnceTheMovieHasADifferentOne()
    {
        // A Radarr upgrade replaced the file the row was made for. The path of the new file would be a lie
        // about what this suggestion offers, so the row keeps its decision and loses its preview.
        SymlinkSuggestionDto dto = Assert.Single(await GetSuggestions(Settings(), await Store(suggestion(fileId: 999))));

        Assert.Equal(999, dto.RadarrFileId);
        Assert.Null(dto.RadarrFilePath);
    }

    [Fact]
    public async Task Suggestions_KeepTheirRowWhenTheMovieLeftTheLibraryAndSaySoWithoutATitle()
    {
        AppSettings settings = Settings();
        settings.RadarrSnapshots[0].Movies.Clear();
        InMemorySymlinkStore store = await Store(suggestion(
            status: SymlinkSuggestionStatus.Stale,
            detail: "The current snapshots no longer contain this pair."));

        SymlinkSuggestionDto dto = Assert.Single(await GetSuggestions(settings, store));

        // The absence is the finding: the row stays readable and the missing name is not filled with a guess.
        Assert.Null(dto.RadarrMovieTitle);
        Assert.Null(dto.RadarrFilePath);
        Assert.Equal(SpecialTitle, dto.SonarrEpisodeTitle);
        Assert.Equal(SymlinkSuggestionStatus.Stale, dto.Status);
        Assert.Contains("no longer contain", dto.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_ReReadsTheSnapshotsAndReportsWhatThePassDid()
    {
        AppSettings settings = Settings();
        InMemorySymlinkStore store = new();

        SymlinkMatchingRun run = (await Invoke<Ok<SymlinkMatchingRun>>(
            "RefreshSuggestionsAsync", Matching(settings, store), CancellationToken.None)).Value!;

        Assert.Equal(2, run.InstancesConsidered);
        Assert.Equal(1, run.Created);
        Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Reject_AnswersThePairAndReturnsTheRowAsTheArchiveNowHoldsIt()
    {
        AppSettings settings = Settings();
        InMemorySymlinkStore store = await Store(suggestion());

        Results<Ok<SymlinkSuggestionDto>, NotFound> result = await Invoke<Results<Ok<SymlinkSuggestionDto>, NotFound>>(
            "RejectSuggestionAsync",
            "suggestion-1",
            new RejectSuggestionRequest { Detail = "  not the same film  " },
            new StubSettingsService(settings),
            Matching(settings, store),
            store,
            CancellationToken.None);

        SymlinkSuggestionDto dto = Assert.IsType<Ok<SymlinkSuggestionDto>>(result.Result).Value!;
        Assert.Equal(SymlinkSuggestionStatus.Rejected, dto.Status);
        Assert.Equal(Start, dto.DecidedAt);
        SymlinkRejection rejection = Assert.Single(await store.GetRejectionsAsync(CancellationToken.None));
        Assert.Equal(Pair, rejection.Pair);
        // The note is stored as it was written, and it belongs to the pair — not to the row.
        Assert.Equal("not the same film", rejection.Detail);
    }

    [Fact]
    public async Task Reject_AnUnknownSuggestionIsNotFoundAndNothingIsStored()
    {
        AppSettings settings = Settings();
        InMemorySymlinkStore store = await Store(suggestion());

        Results<Ok<SymlinkSuggestionDto>, NotFound> result = await Invoke<Results<Ok<SymlinkSuggestionDto>, NotFound>>(
            "RejectSuggestionAsync",
            "suggestion-9",
            new RejectSuggestionRequest { Detail = "typo" },
            new StubSettingsService(settings),
            Matching(settings, store),
            store,
            CancellationToken.None);

        Assert.IsType<NotFound>(result.Result);
        Assert.Empty(await store.GetRejectionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Rejections_AreArchivedWithTheNamesThatMadeThePairRecognisable()
    {
        AppSettings settings = Settings();
        InMemorySymlinkStore store = await Store(suggestion());
        await store.RejectAsync(Pair, "not the same film", Start, CancellationToken.None);

        SymlinkRejectionDto dto = Assert.Single(await GetRejections(settings, store));

        Assert.Equal(Pair.RadarrMovieId, dto.RadarrMovieId);
        Assert.Equal(Pair.SonarrEpisodeId, dto.SonarrEpisodeId);
        Assert.Equal(SpecialTitle, dto.RadarrMovieTitle);
        Assert.Equal(SpecialTitle, dto.SonarrEpisodeTitle);
        Assert.Equal("not the same film", dto.Detail);
        Assert.Equal(Start, dto.CreatedAt);
    }

    [Fact]
    public async Task Reactivate_AnUnknownPairIsNotFound()
    {
        AppSettings settings = Settings();

        Results<Ok, NotFound> result = await Invoke<Results<Ok, NotFound>>(
            "ReactivateRejectionAsync",
            Request(Pair with { RadarrMovieId = 71 }),
            Matching(settings, new InMemorySymlinkStore()),
            CancellationToken.None);

        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task Reactivate_AnswersThePairTheRequestNamesAndBringsItsRowBack()
    {
        AppSettings settings = Settings();
        InMemorySymlinkStore store = await Store(suggestion());
        await store.RejectAsync(Pair, "not the same film", Start, CancellationToken.None);

        Results<Ok, NotFound> result = await Invoke<Results<Ok, NotFound>>(
            "ReactivateRejectionAsync",
            Request(Pair),
            Matching(settings, store),
            CancellationToken.None);

        Assert.IsType<Ok>(result.Result);
        Assert.Empty(await store.GetRejectionsAsync(CancellationToken.None));
        // The pass the service runs has to be visible in the same answer, or the page would have to ask a
        // second time to see what came back.
        Assert.Contains(await store.GetSuggestionsAsync(CancellationToken.None),
            row => row.Status == SymlinkSuggestionStatus.Pending);
    }

    [Fact]
    public async Task Routes_AreMountedInTheAuthorizedSettingsGroupAndTheirServicesResolve()
    {
        // A route that was never mapped and one that is open to everyone both answer 404 or 401 for the
        // wrong reason, so the session check is what proves the wiring here.
        using AuthTestAppFactory factory = new();
        HttpClient cookieless = factory.CreateCookielessClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await cookieless.GetAsync("/api/settings/symlinks/suggestions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cookieless.GetAsync("/api/settings/symlinks/rejections")).StatusCode);

        using IServiceScope scope = factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SymlinkSuggestionService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ExistingEpisodeFileInspector>());
        // The pass runs outside the request that noticed a change, so the signal has to be one per process
        // while the pass itself is resolved per run. (The test host unregisters every hosted service on
        // purpose, so the background run itself is only visible in a real start of the app.)
        Assert.Same(factory.Services.GetRequiredService<SymlinkMatchingRequests>(), factory.Services.GetRequiredService<SymlinkMatchingRequests>());
    }

    private static async Task<IReadOnlyList<SymlinkSuggestionDto>> GetSuggestions(AppSettings settings, InMemorySymlinkStore store)
    {
        StubSettingsService settingsService = new(settings);
        using HttpClient http = new(new NeverAskedHandler());
        ExistingEpisodeFileInspector inspector = new(new SonarrClient(http), new RadarrClient(http),
            new MediaPathMappingService(settingsService), new FakeLinkFileSystem(), new FakeTimeProvider(Start));
        return (await Invoke<Ok<IReadOnlyList<SymlinkSuggestionDto>>>("GetSuggestionsAsync", settingsService, store, inspector, CancellationToken.None)).Value!;
    }

    private static async Task<IReadOnlyList<SymlinkRejectionDto>> GetRejections(AppSettings settings, InMemorySymlinkStore store) =>
        (await Invoke<Ok<IReadOnlyList<SymlinkRejectionDto>>>("GetRejectionsAsync", new StubSettingsService(settings), store, CancellationToken.None)).Value!;

    private static SymlinkSuggestionService Matching(AppSettings settings, InMemorySymlinkStore store) =>
        new(new StubSettingsService(settings), store, new FakeTimeProvider(Start), NullLogger<SymlinkSuggestionService>.Instance);

    /// <summary>Rows as a pass would have written them, so the test does not have to re-derive the pairing.</summary>
    private static async Task<InMemorySymlinkStore> Store(params SymlinkSuggestion[] rows)
    {
        InMemorySymlinkStore store = new();
        foreach (SymlinkSuggestion row in rows)
        {
            await store.UpsertSuggestionAsync(row, CancellationToken.None);
        }

        return store;
    }

    private static Task<T> Invoke<T>(string method, params object?[] arguments) =>
        (Task<T>)typeof(SymlinkEndpoints).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments)!;

    private static SpecialMoviePairRequest Request(SpecialMoviePair pair) => new()
    {
        RadarrInstanceId = pair.RadarrInstanceId,
        RadarrMovieId = pair.RadarrMovieId,
        SonarrInstanceId = pair.SonarrInstanceId,
        SonarrSeriesId = pair.SonarrSeriesId,
        SonarrEpisodeId = pair.SonarrEpisodeId
    };

    private static SymlinkSuggestion suggestion(
        string id = "suggestion-1",
        int movieId = 70,
        DateTimeOffset? at = null,
        int? fileId = 900,
        int configRevision = 0,
        SymlinkSuggestionStatus status = SymlinkSuggestionStatus.Pending,
        string? detail = null) => new()
    {
        Id = id,
        MatcherVersion = "1",
        Status = status,
        Pair = new SpecialMoviePair(RadarrInstance, movieId, SonarrInstance, 12, 5),
        RadarrFileId = fileId,
        SeasonNumber = 0,
        EpisodeNumber = 3,
        Reasons = [new SymlinkSuggestionReason(SpecialMovieMatchingRules.IdenticalTitleCode, "The special and the movie have the same title.")],
        Detail = detail,
        ConfigRevision = configRevision,
        CreatedAt = at ?? Start,
        UpdatedAt = at ?? Start
    };

    private static AppSettings Settings(int revision = 0) => new()
    {
        Symlinks = new SymlinkSettings { Revision = revision },
        SonarrSnapshots =
        [
            new SonarrInstanceSnapshot
            {
                InstanceId = SonarrInstance,
                InstanceName = "Sonarr",
                Series =
                [
                    new SonarrSeriesSnapshot
                    {
                        SeriesId = 12,
                        Title = SeriesTitle,
                        Path = SeriesPath,
                        Seasons =
                        [
                            new SonarrSeasonSnapshot
                            {
                                SeasonNumber = 0,
                                Episodes =
                                [
                                    new SonarrEpisodeSnapshot
                                    {
                                        EpisodeId = 5,
                                        EpisodeNumber = 3,
                                        Title = SpecialTitle,
                                        HasFile = false
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        ],
        RadarrSnapshots =
        [
            new RadarrInstanceSnapshot
            {
                InstanceId = RadarrInstance,
                InstanceName = "Radarr",
                Movies =
                [
                    new RadarrMovieSnapshot
                    {
                        MovieId = 70,
                        // The Radarr copy of the special carries the special title, which is what makes this
                        // a pair; a movie of only the series name is the counterexample the matcher refuses.
                        Title = SpecialTitle,
                        HasFile = true,
                        File = new MediaFileSnapshot { Id = 900, Path = SourcePath }
                    }
                ]
            }
        ]
    };

    private sealed class StubSettingsService(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) => Task.FromResult(value);
    }
}
