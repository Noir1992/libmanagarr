using Microsoft.Extensions.Logging.Abstractions;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

/// <summary>
/// What the register has to remember about the pairs the matcher found. The pairing itself is proven in
/// <see cref="SpecialMovieMatchingRulesTests"/>; here the pair is only the input, and the subject is the
/// plan's list: reported once, decline survives the next sync, a movie without a file only waits,
/// unchanged data is not searched again, and a vanished pair is announced instead of dropping out.
/// </summary>
public sealed class SymlinkSuggestionServiceTests
{
    private const string SonarrInstance = "sonarr-1";
    private const string RadarrInstance = "radarr-1";
    private const string SeriesTitle = "Kabaneri of the Iron Fortress";
    private const string SpecialTitle = "Kabaneri of the Iron Fortress: The Battle of Unato";

    private static readonly DateTimeOffset Start = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider clock = new(Start);
    private readonly InMemorySymlinkStore store = new();

    [Fact]
    public async Task Disabled_LeavesSuggestionsUnchangedUntilReenabled()
    {
        AppSettings settings = Settings();
        await Service(settings).RefreshAsync(force: true, CancellationToken.None);
        SymlinkSuggestion original = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
        settings.Symlinks.Enabled = false;
        Assert.False((await Service(settings).RefreshAsync(force: true, CancellationToken.None)).DidWork);
        SymlinkSuggestion paused = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
        Assert.Equal(original.Revision, paused.Revision);
        Assert.Equal(original.Status, paused.Status);
        settings.Symlinks.Enabled = true;
        Assert.Equal(2, (await Service(settings).RefreshAsync(force: true, CancellationToken.None)).InstancesConsidered);
    }

    [Fact]
    public async Task Refresh_FindsThePairOnceAndStoresTheReasonsThatProducedIt()
    {
        AppSettings settings = Settings();

        SymlinkMatchingRun run = await Service(settings).RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal(2, run.InstancesConsidered);
        Assert.Equal(1, run.Created);
        Assert.Equal(0, run.Updated);
        Assert.True(run.DidWork);
        SymlinkSuggestion suggestion = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
        Assert.Equal(SymlinkSuggestionStatus.Pending, suggestion.Status);
        Assert.Equal(new SpecialMoviePair(RadarrInstance, 70, SonarrInstance, 12, 5), suggestion.Pair);
        Assert.Equal(900, suggestion.RadarrFileId);
        Assert.Equal(0, suggestion.SeasonNumber);
        Assert.Equal(3, suggestion.EpisodeNumber);
        Assert.Equal("1", suggestion.MatcherVersion);
        Assert.Contains(SpecialMovieMatchingRules.IdenticalTitleCode, suggestion.Reasons.Select(reason => reason.Code));
        Assert.Equal(Start, suggestion.CreatedAt);
        Assert.Equal(Start, suggestion.UpdatedAt);
    }

    [Fact]
    public async Task Refresh_WithoutAMatchingMovieStoresNothingAndSaysSo()
    {
        AppSettings settings = Settings(movies: [Film("Kabaneri of the Iron Fortress: Raging Break")]);

        SymlinkMatchingRun run = await Service(settings).RefreshAsync(force: false, CancellationToken.None);

        Assert.False(run.DidWork);
        Assert.Empty(await store.GetSuggestionsAsync(CancellationToken.None));
        // The instance is still marked as seen, so the next sync of unchanged data stays quiet.
        Assert.Equal(2, (await store.GetMatchingStatesAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Refresh_AgainstUnchangedSnapshotsIsNotDoneAgain()
    {
        AppSettings settings = Settings();
        SymlinkSuggestionService service = Service(settings);
        await service.RefreshAsync(force: false, CancellationToken.None);
        SymlinkSuggestion first = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));

        clock.Advance(TimeSpan.FromMinutes(10));
        SymlinkMatchingRun second = await service.RefreshAsync(force: false, CancellationToken.None);

        Assert.False(second.DidWork);
        Assert.Equal(0, second.Created + second.Updated + second.MarkedStale);
        SymlinkSuggestion unchanged = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
        Assert.Equal(first.Id, unchanged.Id);
        Assert.Equal(first.UpdatedAt, unchanged.UpdatedAt);

        // The manual action is the one that ignores the stored signature, because the user just asked.
        clock.Advance(TimeSpan.FromMinutes(5));
        SymlinkMatchingRun forced = await service.RefreshAsync(force: true, CancellationToken.None);
        Assert.Equal(1, forced.Updated);
        Assert.Equal(Start + TimeSpan.FromMinutes(15), (await store.GetSuggestionsAsync(CancellationToken.None)).Single().UpdatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Refresh_AChangedConfigurationRevisionIsLookedAtAgain(int storedMatcherVersionOffset)
    {
        AppSettings settings = Settings();
        SymlinkSuggestionService service = Service(settings);
        await service.RefreshAsync(force: false, CancellationToken.None);
        foreach (SymlinkMatchingState state in await store.GetMatchingStatesAsync(CancellationToken.None))
        {
            // The stored answer belongs to an older path configuration, so its preview may name a source
            // that is not there anymore.
            await store.SaveMatchingStateAsync(
                WithRevision(state, configRevision: state.ConfigRevision + 1, matcherVersion: SpecialMovieMatchingRules.MatcherVersion + storedMatcherVersionOffset),
                CancellationToken.None);
        }

        SymlinkMatchingRun run = await service.RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal(1, run.Updated);
        Assert.Equal(0, run.Created);
    }

    [Fact]
    public async Task Refresh_ANewMovieIsCheckedAgainstTheSpecialsThatAreAlreadyThere()
    {
        AppSettings settings = Settings(movies: []);
        SymlinkSuggestionService service = Service(settings);
        Assert.False((await service.RefreshAsync(force: false, CancellationToken.None)).DidWork);

        clock.Advance(TimeSpan.FromMinutes(1));
        // Only Radarr moved. The specials of the other instance are the half that was already known, and
        // this direction has to work without Sonarr being touched again.
        settings.RadarrSnapshots[0].Movies.Add(Film(SpecialTitle));
        SymlinkMatchingRun run = await service.RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal(1, run.Created);
        Assert.Equal(SymlinkSuggestionStatus.Pending, Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Refresh_AChangeInARegularSeasonIsNotMatchingData()
    {
        AppSettings settings = Settings();
        SonarrSeriesSnapshot series = settings.SonarrSnapshots[0].Series[0];
        series.Seasons.Add(new SonarrSeasonSnapshot
        {
            SeasonNumber = 1,
            Episodes = [Episode(episodeId: 60, title: "Kassa", seasonNumber: 1)]
        });
        SymlinkSuggestionService service = Service(settings);
        await service.RefreshAsync(force: false, CancellationToken.None);

        clock.Advance(TimeSpan.FromMinutes(1));
        series.Seasons[1].Episodes[0].Title = "Kassa Renamed";
        series.Seasons[1].Episodes[0].File = new MediaFileSnapshot { Id = 610, Path = @"\\nas\sonarr\andor\s01e01.mkv" };

        Assert.False((await service.RefreshAsync(force: false, CancellationToken.None)).DidWork);
    }

    [Fact]
    public async Task Refresh_AMovieWithoutAFileWaitsAndTheSameRowIsReadyWhenTheFileArrives()
    {
        AppSettings settings = Settings(movies: [Film(SpecialTitle, hasFile: false)]);
        SymlinkSuggestionService service = Service(settings);

        await service.RefreshAsync(force: false, CancellationToken.None);
        SymlinkSuggestion waiting = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
        Assert.Equal(SymlinkSuggestionStatus.WaitingForFile, waiting.Status);
        Assert.Null(waiting.RadarrFileId);

        clock.Advance(TimeSpan.FromMinutes(1));
        settings.RadarrSnapshots[0].Movies[0] = Film(SpecialTitle);
        await service.RefreshAsync(force: false, CancellationToken.None);

        SymlinkSuggestion ready = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
        // The row the user is already looking at becomes actionable; no second question for the same pair.
        Assert.Equal(waiting.Id, ready.Id);
        Assert.Equal(SymlinkSuggestionStatus.Pending, ready.Status);
        Assert.Equal(900, ready.RadarrFileId);
        Assert.Equal(waiting.CreatedAt, ready.CreatedAt);
    }

    [Fact]
    public async Task Refresh_AReplacedRadarrFileUpdatesTheRowInsteadOfAskingTwice()
    {
        AppSettings settings = Settings();
        SymlinkSuggestionService service = Service(settings);
        await service.RefreshAsync(force: false, CancellationToken.None);
        SymlinkSuggestion before = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));

        clock.Advance(TimeSpan.FromHours(1));
        // A Radarr upgrade replaces the file under the same movie. The pairing was already reported, so
        // the row has to follow the new file rather than start a second decision.
        settings.RadarrSnapshots[0].Movies[0] = Film(SpecialTitle, fileId: 999);
        SymlinkMatchingRun run = await service.RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal(1, run.Updated);
        SymlinkSuggestion after = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(999, after.RadarrFileId);
    }

    [Fact]
    public async Task Refresh_ADeclinedPairIsLeftAloneAndCountedAsSkipped()
    {
        AppSettings settings = Settings();
        SymlinkSuggestionService service = Service(settings);
        await service.RefreshAsync(force: false, CancellationToken.None);
        SymlinkSuggestion suggestion = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
        Assert.True(await service.RejectAsync(suggestion.Id, "not the same film", CancellationToken.None));

        clock.Advance(TimeSpan.FromHours(1));
        settings.RadarrSnapshots[0].Movies[0] = Film(SpecialTitle, fileId: 999);
        SymlinkMatchingRun run = await service.RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal(1, run.SkippedRejected);
        Assert.Equal(0, run.Created + run.Updated);
        SymlinkSuggestion declined = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
        Assert.Equal(SymlinkSuggestionStatus.Rejected, declined.Status);
        // The file the row was made for is gone, but a decline is keyed on the pair, so the answer stands.
        Assert.Equal(900, declined.RadarrFileId);
        Assert.Equal("not the same film", (await store.GetRejectionsAsync(CancellationToken.None)).Single().Detail);
    }

    [Fact]
    public async Task Reactivate_BringsThePairBackWithReasonsFromToday()
    {
        AppSettings settings = Settings();
        SymlinkSuggestionService service = Service(settings);
        await service.RefreshAsync(force: false, CancellationToken.None);
        SymlinkSuggestion declined = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
        await service.RejectAsync(declined.Id, "not the same film", CancellationToken.None);

        clock.Advance(TimeSpan.FromDays(1));
        Assert.True(await service.ReactivateAsync(declined.Pair, CancellationToken.None));

        // The answered row stays as the record that this pair was reported once, next to a new row whose
        // reasons come from today's snapshots instead of the text that was just declined.
        List<SymlinkSuggestion> rows = [.. await store.GetSuggestionsAsync(CancellationToken.None)];
        Assert.Equal(2, rows.Count);
        SymlinkSuggestion reopened = rows.Single(row => row.Status == SymlinkSuggestionStatus.Pending);
        Assert.NotEqual(declined.Id, reopened.Id);
        Assert.Contains(SpecialMovieMatchingRules.IdenticalTitleCode, reopened.Reasons.Select(reason => reason.Code));
        Assert.Equal(Start + TimeSpan.FromDays(1), reopened.CreatedAt);
        Assert.Empty(await store.GetRejectionsAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(SymlinkSuggestionStatus.Creating)]
    [InlineData(SymlinkSuggestionStatus.Completed)]
    public async Task Reactivate_DeletedLinkHistoryDoesNotHideThePair(SymlinkSuggestionStatus previousStatus)
    {
        AppSettings settings = Settings();
        SymlinkSuggestion previous = Suggestion("old-link-suggestion", previousStatus, movieId: 70);
        previous.LinkId = "deleted-link";
        await store.UpsertSuggestionAsync(previous, CancellationToken.None);
        await store.RejectAsync(previous.Pair, "The managed link of this pair was removed on purpose.", Start, CancellationToken.None);

        Assert.True(await Service(settings).ReactivateAsync(previous.Pair, CancellationToken.None));

        Assert.Empty(await store.GetRejectionsAsync(CancellationToken.None));
        SymlinkSuggestion reopened = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None),
            row => row.Status == SymlinkSuggestionStatus.Pending);
        Assert.Equal(previous.Pair, reopened.Pair);
        Assert.Null(reopened.LinkId);
        Assert.Contains(SpecialMovieMatchingRules.IdenticalTitleCode, reopened.Reasons.Select(reason => reason.Code));
    }

    [Fact]
    public async Task Reactivate_APairNobodyDeclinedIsNotFoundAndChangesNothing()
    {
        AppSettings settings = Settings();

        Assert.False(await Service(settings).ReactivateAsync(
            new SpecialMoviePair(RadarrInstance, 70, SonarrInstance, 12, 5), CancellationToken.None));

        Assert.Empty(await store.GetSuggestionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Reject_AnUnknownSuggestionIsNotFoundAndNothingIsStored()
    {
        Assert.False(await Service(Settings()).RejectAsync("suggestion-9", "typo", CancellationToken.None));
        Assert.Empty(await store.GetRejectionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Refresh_APairTheSnapshotsNoLongerContainIsMarkedStale()
    {
        AppSettings settings = Settings();
        SymlinkSuggestionService service = Service(settings);
        await service.RefreshAsync(force: false, CancellationToken.None);

        clock.Advance(TimeSpan.FromHours(1));
        settings.RadarrSnapshots[0].Movies.Clear();
        SymlinkMatchingRun run = await service.RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal(1, run.MarkedStale);
        SymlinkSuggestion stale = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
        // "The movie is gone" and "nobody looked yet" have to stay apart in the register.
        Assert.Equal(SymlinkSuggestionStatus.Stale, stale.Status);
        Assert.Contains("no longer contain", stale.Detail, StringComparison.Ordinal);

        clock.Advance(TimeSpan.FromHours(1));
        Assert.False((await service.RefreshAsync(force: false, CancellationToken.None)).DidWork);
        Assert.Equal(SymlinkSuggestionStatus.Stale, (await store.GetSuggestionsAsync(CancellationToken.None)).Single().Status);
    }

    [Fact]
    public async Task Refresh_ASpecialThatLostItsMovieAndGotANewOneIsNotReportedTwiceAsTheSameRow()
    {
        AppSettings settings = Settings();
        SymlinkSuggestionService service = Service(settings);
        await service.RefreshAsync(force: false, CancellationToken.None);

        clock.Advance(TimeSpan.FromHours(1));
        settings.RadarrSnapshots[0].Movies.Clear();
        await service.RefreshAsync(force: false, CancellationToken.None);

        clock.Advance(TimeSpan.FromHours(1));
        settings.RadarrSnapshots[0].Movies.Add(Film(SpecialTitle, movieId: 71));
        await service.RefreshAsync(force: false, CancellationToken.None);

        List<SymlinkSuggestion> rows = [.. await store.GetSuggestionsAsync(CancellationToken.None)];
        Assert.Equal(2, rows.Count);
        Assert.Equal(SymlinkSuggestionStatus.Stale, rows.Single(row => row.Pair.RadarrMovieId == 70).Status);
        Assert.Equal(SymlinkSuggestionStatus.Pending, rows.Single(row => row.Pair.RadarrMovieId == 71).Status);
    }

    [Fact]
    public async Task Refresh_ManagedPairsAreSkippedButDeletedLinksCanBeSuggestedAgain()
    {
        AppSettings settings = Settings(movies: [Film(SpecialTitle, movieId: 70), Film(SpecialTitle, movieId: 71)]);
        await store.InsertLinkAsync(new SymlinkLink
        {
            Id = "link-1",
            RadarrInstanceId = RadarrInstance,
            RadarrMovieId = 70,
            RadarrFileId = 900,
            SonarrInstanceId = SonarrInstance,
            SonarrSeriesId = 12,
            SonarrEpisodeId = 5,
            SeasonNumber = 0,
            EpisodeNumber = 3,
            StorageAreaId = "area-1",
            LinkPath = @"C:\media\series\kabaneri\kabaneri s00e03.mkv",
            LinkText = "..\\..\\movies\\unato\\unato.mkv",
            SourcePath = @"C:\media\movies\unato\unato.mkv",
            OperationStatus = SymlinkOperationStatus.Active,
            CreatedAt = clock.GetUtcNow(),
            UpdatedAt = clock.GetUtcNow()
        }, CancellationToken.None);
        SymlinkSuggestion completed = Suggestion("suggestion-done", SymlinkSuggestionStatus.Completed, movieId: 71);
        completed.DecidedAt = clock.GetUtcNow();
        await store.UpsertSuggestionAsync(completed, CancellationToken.None);

        SymlinkMatchingRun run = await Service(settings).RefreshAsync(force: false, CancellationToken.None);

        // Only the existing managed link reserves its pair; historic outcomes remain as history.
        Assert.Equal(1, run.Created);
        List<SymlinkSuggestion> rows = [.. await store.GetSuggestionsAsync(CancellationToken.None)];
        Assert.Equal(2, rows.Count);
        Assert.Equal(SymlinkSuggestionStatus.Completed, rows.Single(row => row.Id == completed.Id).Status);
        Assert.Equal(71, rows.Single(row => row.Status == SymlinkSuggestionStatus.Pending).Pair.RadarrMovieId);
    }

    [Fact]
    public async Task FinishingDeletionAutomaticallyReopensTheSuggestionDespiteUnchangedSnapshots()
    {
        AppSettings settings = Settings();
        settings.SonarrInstances = [new ArrInstanceSettings { Id = SonarrInstance, BaseUrl = "http://sonarr.local", ApiKey = "key" }];
        FixedSettings configured = new(settings);
        SymlinkSuggestionService matching = Service(settings);
        await matching.RefreshAsync(false, CancellationToken.None);
        SymlinkSuggestion previous = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
        previous.Status = SymlinkSuggestionStatus.Completed;
        previous.LinkId = "deleted-link";
        Assert.True(await store.UpdateSuggestionAsync(previous, previous.Revision, CancellationToken.None));
        SymlinkLink journal = new()
        {
            Id = "deleted-link", RadarrInstanceId = RadarrInstance, RadarrMovieId = 70,
            SonarrInstanceId = SonarrInstance, SonarrSeriesId = 12, SonarrEpisodeId = 5,
            LinkPath = SymlinkTestPaths.LibManagarr("series/Specials/special.mkv"),
            OperationStatus = SymlinkOperationStatus.CleanupPending, SetupStep = SymlinkSetupStep.Completed,
            CreatedAt = Start, UpdatedAt = Start
        };
        await store.InsertLinkAsync(journal, CancellationToken.None);
        Assert.False((await matching.RefreshAsync(false, CancellationToken.None)).DidWork);
        FakeLinkFileSystem files = new();
        StubArrHandler handler = new(SymlinkFixture.HappyRoutes());
        SymlinkService links = new(store, configured, new MediaPathMappingService(configured),
            new SymlinkCapabilityService(store, configured, files, clock, NullLogger<SymlinkCapabilityService>.Instance),
            new SymlinkSetupRequests(), new RadarrClient(new HttpClient(handler)), new SonarrClient(new HttpClient(handler)),
            files, clock, NullLogger<SymlinkService>.Instance, matching: matching);

        using IDisposable operation = await SymlinkOperationGate.EnterAsync(CancellationToken.None);
        Assert.Equal(SymlinkSetupOutcome.Completed, await links.FinishDeletionAsync(journal, CancellationToken.None));

        Assert.Null(await store.GetLinkAsync(journal.Id, CancellationToken.None));
        SymlinkSuggestion reopened = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None),
            row => row.Status == SymlinkSuggestionStatus.Pending);
        Assert.Equal(previous.Pair, reopened.Pair);
        Assert.Null(reopened.LinkId);
        Assert.Empty(await store.GetRejectionsAsync(CancellationToken.None));
        Assert.DoesNotContain(handler.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task Refresh_EveryMovieClaimingTheSameSpecialIsListedAsAnAlternativeOfTheOthers()
    {
        AppSettings settings = Settings(movies:
        [
            Film(SpecialTitle, movieId: 70),
            Film(SpecialTitle, movieId: 71, fileId: 901),
            Film("Kabaneri of the Iron Fortress — The Battle of Unato!", movieId: 72, hasFile: false)
        ]);

        SymlinkMatchingRun run = await Service(settings).RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal(3, run.Created);
        List<SymlinkSuggestion> rows = [.. await store.GetSuggestionsAsync(CancellationToken.None)];
        // Title equality alone is not an identity, so nothing is picked: every pair shows the two rivals.
        Assert.All(rows, row => Assert.Equal(2, row.Alternatives.Count));
        SymlinkSuggestion withFile = rows.Single(row => row.RadarrFileId == 900);
        Assert.Equal([71, 72], withFile.Alternatives.Select(alternative => alternative.RadarrMovieId));
        SymlinkSuggestionAlternative rivalWithoutFile = withFile.Alternatives.Single(alternative => alternative.RadarrMovieId == 72);
        Assert.Null(rivalWithoutFile.RadarrFileId);
        Assert.Contains("no file of its own", rivalWithoutFile.Detail, StringComparison.Ordinal);
        Assert.Equal(901, withFile.Alternatives.Single(alternative => alternative.RadarrMovieId == 71).RadarrFileId);
    }

    [Fact]
    public async Task Refresh_OnlyOneMovieForASpecialHasNoAlternativesToShow()
    {
        SymlinkSuggestion suggestion = Assert.Single(await Suggestions(Settings()));

        Assert.Empty(suggestion.Alternatives);
    }

    [Fact]
    public async Task Refresh_WhenSonarrAlreadyHasTheSpecialThatIsSaidOutOfLoud()
    {
        SonarrEpisodeSnapshot withFile = Episode(5, SpecialTitle);
        withFile.HasFile = true;
        withFile.File = new MediaFileSnapshot { Id = 500, Path = @"\\nas\sonarr\kabaneri\s00e03.mkv" };

        SymlinkSuggestion suggestion = Assert.Single(await Suggestions(Settings(specials: [withFile])));

        SymlinkSuggestionReason finding = Assert.Single(
            suggestion.Reasons, reason => reason.Code == SpecialMovieMatchingRules.EpisodeHasFileCode);
        Assert.Contains("inspected when offers are loaded", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("no existing file or link is automatically adopted or overwritten", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_StopsAfterTheFirstFailureAndLeavesTheInstanceDueForTheNextPass()
    {
        AppSettings settings = Settings();
        store.UpsertThrows = new IOException("The register could not be written.");

        await Assert.ThrowsAsync<IOException>(() => Service(settings).RefreshAsync(force: false, CancellationToken.None));

        // The signature is written by the last step of a pass, so an interrupted one cannot make the work
        // look done — the next sync asks again and the candidate is not lost.
        Assert.Empty(await store.GetMatchingStatesAsync(CancellationToken.None));
        store.UpsertThrows = null;
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(1, (await Service(settings).RefreshAsync(force: false, CancellationToken.None)).Created);
        Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Refresh_RecordsOneSignaturePerInstanceOfBothApplications()
    {
        AppSettings settings = Settings();

        SymlinkMatchingRun run = await Service(settings).RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal(2, run.InstancesConsidered);
        List<SymlinkMatchingState> states = [.. await store.GetMatchingStatesAsync(CancellationToken.None)];
        Assert.Equal([RadarrInstance, SonarrInstance], states.Select(state => state.InstanceId));
        Assert.Equal(ArrApplicationKind.Radarr, states[0].Kind);
        Assert.Equal(ArrApplicationKind.Sonarr, states[1].Kind);
        Assert.All(states, state =>
        {
            Assert.NotEmpty(state.Signature);
            Assert.Equal(SpecialMovieMatchingRules.MatcherVersion, state.MatcherVersion);
            Assert.Equal(0, state.ConfigRevision);
            Assert.Equal(Start, state.CheckedAt);
        });
    }

    private async Task<IReadOnlyList<SymlinkSuggestion>> Suggestions(AppSettings settings)
    {
        await Service(settings).RefreshAsync(force: false, CancellationToken.None);
        return await store.GetSuggestionsAsync(CancellationToken.None);
    }

    private SymlinkSuggestionService Service(AppSettings settings) =>
        new(new FixedSettings(settings), store, clock, NullLogger<SymlinkSuggestionService>.Instance);

    private static SymlinkMatchingState WithRevision(SymlinkMatchingState state, int configRevision, int matcherVersion) => new()
    {
        InstanceId = state.InstanceId,
        Kind = state.Kind,
        Signature = state.Signature,
        MatcherVersion = matcherVersion,
        ConfigRevision = configRevision,
        CheckedAt = state.CheckedAt
    };

    private static SymlinkSuggestion Suggestion(string id, SymlinkSuggestionStatus status, int movieId) => new()
    {
        Id = id,
        MatcherVersion = "1",
        Status = status,
        Pair = new SpecialMoviePair(RadarrInstance, movieId, SonarrInstance, 12, 5),
        RadarrFileId = 900,
        SeasonNumber = 0,
        EpisodeNumber = 3,
        Reasons = [new SymlinkSuggestionReason(SpecialMovieMatchingRules.IdenticalTitleCode, "The special and the movie have the same title.")],
        ConfigRevision = 0,
        CreatedAt = Start,
        UpdatedAt = Start
    };

    private static AppSettings Settings(
        SonarrEpisodeSnapshot[]? specials = null,
        RadarrMovieSnapshot[]? movies = null) => new()
    {
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
                        Seasons = [new SonarrSeasonSnapshot { SeasonNumber = 0, Episodes = [.. specials ?? [Episode(5, SpecialTitle)]] }]
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
                Movies = [.. movies ?? [Film(SpecialTitle)]]
            }
        ]
    };

    private static SonarrEpisodeSnapshot Episode(int episodeId, string title, int seasonNumber = 0) => new()
    {
        EpisodeId = episodeId,
        // The season-0 special of the Kabaneri example is episode 3 of that series.
        EpisodeNumber = seasonNumber == 0 ? 3 : 1,
        Title = title,
        HasFile = false,
        AirDateUtc = new DateTimeOffset(2019, 12, 30, 0, 0, 0, TimeSpan.Zero)
    };

    private static RadarrMovieSnapshot Film(
        string title,
        int movieId = 70,
        int? fileId = 900,
        bool hasFile = true) => new()
    {
        MovieId = movieId,
        Title = title,
        HasFile = hasFile,
        AirDateUtc = new DateTimeOffset(2019, 12, 30, 0, 0, 0, TimeSpan.Zero),
        File = new MediaFileSnapshot
        {
            Id = hasFile ? fileId : null,
            Path = hasFile ? $@"\\nas\radarr\kabaneri\unato-{movieId}.mkv" : null,
            Duration = TimeSpan.FromMinutes(110)
        }
    };

    private sealed class FixedSettings(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) => Task.FromResult(value);
    }
}
