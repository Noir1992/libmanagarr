using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibManagarr.Api.Tests.Services;

/// <summary>
/// Creating, editing and removing one managed link. Both Arr instances are stubbed and the file system
/// is a scripted fake, so each test says which answer arrived and which entry was standing where. What
/// no fake can prove — that a real file system keeps a symlink — is proven against a real one in its own
/// test stage.
/// </summary>
public sealed class SymlinkServiceTests
{
    // The scenario itself is shared with the register's endpoint tests, so that both are answered by the
    // same working configuration; these names are what keeps the test bodies readable.
    private const string RadarrInstanceId = SymlinkFixture.RadarrInstanceId;
    private const string SonarrInstanceId = SymlinkFixture.SonarrInstanceId;
    private const string StorageAreaId = SymlinkFixture.StorageAreaId;
    private const int ConfigRevision = SymlinkFixture.ConfigRevision;
    private const int MovieId = SymlinkFixture.MovieId;
    private const int RadarrFileId = SymlinkFixture.RadarrFileId;
    private const int SeriesId = SymlinkFixture.SeriesId;
    private const int EpisodeId = SymlinkFixture.EpisodeId;
    private const string MovieFolder = SymlinkFixture.MovieFolder;
    private const string SeriesFolder = SymlinkFixture.SeriesFolder;
    private const string SourceFileName = SymlinkFixture.SourceFileName;
    private const string LinkName = SymlinkFixture.LinkName;

    private static readonly DateTimeOffset Start = new(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);
    private static readonly SpecialMoviePair Pair = new(RadarrInstanceId, MovieId, SonarrInstanceId, SeriesId, EpisodeId);

    private readonly InMemorySymlinkStore store = new();
    private readonly FakeTimeProvider clock = new(Start);
    private readonly SymlinkSetupRequests setupRequests = new();

    public SymlinkServiceTests()
    {
        // A link can only be created in an area whose probe passed for the current configuration, so every
        // test starts from that answer and the two tests about the probe itself replace it first.
        SeedCapability(SymlinkCapabilityStatus.Available, ConfigRevision);
    }

    [Fact]
    public async Task Disabled_RejectsManualCreationBeforeAnyArrOrFilesystemAction()
    {
        AppSettings settings = Settings();
        settings.Symlinks.Enabled = false;
        StubArrHandler handler = new([]);
        FakeLinkFileSystem files = new();
        SymlinkService service = CreateService(handler, files, settings);
        ApiProblemException error = await Assert.ThrowsAsync<ApiProblemException>(() =>
            service.CreateAsync(Pair, RadarrFileId, ConfigRevision, null, CancellationToken.None));
        Assert.Equal(409, error.StatusCode);
        Assert.Empty(handler.Requests);
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(await store.GetLinksAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ThePreviewDescribesTheEntryThatWouldBeReserved()
    {
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), new FakeLinkFileSystem());

        SymlinkLinkPlan plan = await service.PreviewAsync(Pair, RadarrFileId, CancellationToken.None);

        Assert.True(plan.Runnable);
        Assert.Empty(plan.Blockers);
        Assert.Empty(plan.Warnings);
        SymlinkLink draft = plan.Draft;
        Assert.Equal(string.Empty, draft.Id);
        Assert.Equal(RadarrInstanceId, draft.RadarrInstanceId);
        Assert.Equal(MovieId, draft.RadarrMovieId);
        Assert.Equal(RadarrFileId, draft.RadarrFileId);
        Assert.Equal(SonarrInstanceId, draft.SonarrInstanceId);
        Assert.Equal(SeriesId, draft.SonarrSeriesId);
        Assert.Equal(EpisodeId, draft.SonarrEpisodeId);
        Assert.Equal(0, draft.SeasonNumber);
        Assert.Equal(5, draft.EpisodeNumber);
        Assert.Equal(StorageAreaId, draft.StorageAreaId);
        Assert.Equal(SourcePath(), draft.SourcePath);
        Assert.Equal(ExpectedLinkPath(), draft.LinkPath);
        Assert.Equal(SymlinkOperationStatus.Pending, draft.OperationStatus);
        Assert.Equal(SymlinkSetupStep.LinkCreated, draft.SetupStep);
        // Confirming the pair once is what authorizes following later upgrades of the same source.
        Assert.True(draft.AutoFollowUp);
        Assert.Equal(Start, draft.CreatedAt);
        // The name is scene-style so the scan attributes the file to exactly this special, and it is
        // built from the series folder rather than the title, because the folder name is already legal.
        Assert.Equal(LinkName, Path.GetFileName(draft.LinkPath));
        Assert.Equal(SymlinkFixture.SourceVersion(RadarrFileId, SymlinkFixture.SourceSize), draft.SourceVersion);
        Assert.Equal("radarr", draft.ExpectedMetadata!.Origin);
        Assert.Equal("WFFF", draft.ExpectedMetadata!.ReleaseGroup);
        Assert.Equal("WEBDL-1080p", draft.ExpectedMetadata!.Quality);
        Assert.Equal(["German"], draft.ExpectedMetadata!.Languages);
        Assert.Equal(5_368_709_120L, draft.ExpectedMetadata!.Size);
    }

    [Fact]
    public async Task ALinkWithoutAVerifiedSonarrViewIsWarnedAboutButNotRefused()
    {
        AppSettings settings = Settings();
        settings.Symlinks.ReaderViews = [];
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), new FakeLinkFileSystem(), settings);

        SymlinkLinkPlan plan = await service.PreviewAsync(Pair, RadarrFileId, CancellationToken.None);

        Assert.True(plan.Runnable);
        SymlinkFinding warning = Assert.Single(plan.Warnings);
        Assert.Equal("reader-view-unverified", warning.Code);
        // An unproven view of the same files is a decision for the operator, so it stays visible rather
        // than silently becoming a refusal or, worse, silently becoming a proof.
        Assert.Contains("unproven", warning.Detail);
    }

    [Fact]
    public async Task AStorageAreaWhoseCapabilityTestAgedOutIsAutomaticallyRechecked()
    {
        SeedCapability(SymlinkCapabilityStatus.Available, ConfigRevision - 1);
        await AssertBlockedAsync(HappyRoutes(), "capability-unavailable", StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task AStorageAreaWithoutWorkingSymlinksIsRefused()
    {
        SeedCapability(SymlinkCapabilityStatus.PermissionDenied, ConfigRevision);
        await AssertBlockedAsync(HappyRoutes(), "capability-unavailable", StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task AnEpisodeThatAlreadyHasAFileIsRefused()
    {
        Dictionary<string, List<string>> routes = HappyRoutes();
        routes["GET /api/v3/episode/5"] =
        [
            $$"""{ "id": {{EpisodeId}}, "seriesId": {{SeriesId}}, "seasonNumber": 0, "episodeNumber": 5, "hasFile": true, "episodeFileId": 88, "monitored": true }"""
        ];

        await AssertBlockedAsync(routes, "sonarr-episode-has-file");
    }

    [Fact]
    public async Task ASpecialIsStillOfferedWhenSonarrNamesItsMissingFileAsZero()
    {
        Dictionary<string, List<string>> routes = HappyRoutes();
        routes["GET /api/v3/episode/5"] =
        [
            // Measured on Sonarr 4.0: an episode without a file is answered with episodeFileId 0 while
            // hasFile says false. Reading the 0 as a file would refuse the special forever, because the
            // file that is supposed to arrive through the link is the very thing that is missing.
            $$"""{ "id": {{EpisodeId}}, "seriesId": {{SeriesId}}, "seasonNumber": 0, "episodeNumber": 5, "hasFile": false, "episodeFileId": 0, "monitored": true }"""
        ];
        SymlinkService service = CreateService(new StubArrHandler(routes), new FakeLinkFileSystem());

        SymlinkLinkPlan plan = await service.PreviewAsync(Pair, RadarrFileId, CancellationToken.None);

        Assert.True(plan.Runnable);
        Assert.Empty(plan.Blockers);
    }

    [Fact]
    public async Task AnEpisodeThatMovedToAnotherSeriesIsRefused()
    {
        Dictionary<string, List<string>> routes = HappyRoutes();
        routes["GET /api/v3/episode/5"] =
        [
            $$"""{ "id": {{EpisodeId}}, "seriesId": 99, "seasonNumber": 0, "episodeNumber": 5, "hasFile": false, "monitored": true }"""
        ];

        await AssertBlockedAsync(routes, "sonarr-episode-moved");
    }

    [Fact]
    public async Task ARegularEpisodeIsRefusedBecauseOnlySpecialsAreManaged()
    {
        Dictionary<string, List<string>> routes = HappyRoutes();
        routes["GET /api/v3/episode/5"] =
        [
            $$"""{ "id": {{EpisodeId}}, "seriesId": {{SeriesId}}, "seasonNumber": 2, "episodeNumber": 5, "hasFile": false, "monitored": true }"""
        ];

        await AssertBlockedAsync(routes, "sonarr-episode-not-special");
    }

    [Fact]
    public async Task ASeriesWithoutAFolderIsRefused()
    {
        Dictionary<string, List<string>> routes = HappyRoutes();
        routes["GET /api/v3/series/12"] =
        [
            $$"""{ "id": {{SeriesId}}, "title": "Kabaneri of the Iron Fortress" }"""
        ];

        await AssertBlockedAsync(routes, "sonarr-series-pathless");
    }

    [Fact]
    public async Task ASeriesFolderNoMappingCoversIsRefused()
    {
        Dictionary<string, List<string>> routes = HappyRoutes();
        routes["GET /api/v3/series/12"] =
        [
            $$"""{ "id": {{SeriesId}}, "title": "Kabaneri of the Iron Fortress", "path": {{Quote(@"E:\other\Kabaneri")}} }"""
        ];

        await AssertBlockedAsync(routes, "series-not-mapped");
    }

    [Fact]
    public async Task ASourceTheInstanceReplacedAfterTheSuggestionIsRefused()
    {
        Dictionary<string, List<string>> routes = HappyRoutes();
        routes["GET /api/v3/moviefile"] =
        [
            $$"""[ { "id": 7777, "movieId": {{MovieId}}, "path": {{Quote(MoviesInstance($"{MovieFolder}/Other.mkv"))}}, "size": 1000 } ]"""
        ];

        await AssertBlockedAsync(routes, "radarr-file-changed");
    }

    [Fact]
    public async Task ASpecialWithSeveralPossibleSourcesIsRefusedUntilOneIsChosen()
    {
        Dictionary<string, List<string>> routes = HappyRoutes();
        routes["GET /api/v3/moviefile"] =
        [
            $$"""
              [ { "id": 7777, "movieId": {{MovieId}}, "path": {{Quote(MoviesInstance($"{MovieFolder}/One.mkv"))}}, "size": 1000 },
                { "id": 8888, "movieId": {{MovieId}}, "path": {{Quote(MoviesInstance($"{MovieFolder}/Two.mkv"))}}, "size": 2000 } ]
              """
        ];
        StubArrHandler handler = new(routes);
        SymlinkService service = CreateService(handler, new FakeLinkFileSystem());

        SymlinkLinkPlan plan = await service.PreviewAsync(Pair, radarrFileId: null, CancellationToken.None);

        Assert.False(plan.Runnable);
        Assert.Equal("radarr-file-undetermined", Assert.Single(plan.Blockers).Code);
        Assert.Empty(await store.GetLinksAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ASourceNoMappingCoversIsRefused()
    {
        AppSettings settings = Settings();
        settings.Symlinks.PathMappings = settings.Symlinks.PathMappings
            .Where(mapping => mapping.Kind != ArrApplicationKind.Radarr)
            .ToList();
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), new FakeLinkFileSystem(), settings);

        SymlinkLinkPlan plan = await service.PreviewAsync(Pair, RadarrFileId, CancellationToken.None);

        Assert.False(plan.Runnable);
        Assert.Equal("source-not-mapped", Assert.Single(plan.Blockers).Code);
    }

    [Fact]
    public async Task ASourceOutsideEveryMovieRootIsRefused()
    {
        // The mapping exists, but it leads somewhere no storage area claims — which is exactly the case
        // the allow list is for, because an equal looking path is not proof of the same shelf.
        AppSettings settings = Settings();
        settings.Symlinks.PathMappings.Single(mapping => mapping.Kind == ArrApplicationKind.Radarr).LibManagarrRoot =
            LibManagarr("elsewhere");
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), new FakeLinkFileSystem(), settings);

        SymlinkLinkPlan plan = await service.PreviewAsync(Pair, RadarrFileId, CancellationToken.None);

        Assert.False(plan.Runnable);
        Assert.Equal("source-outside-storage-area", Assert.Single(plan.Blockers).Code);
    }

    [Fact]
    public async Task ASeriesAndSourceInDifferentStorageAreasAreRefused()
    {
        AppSettings settings = Settings();
        settings.Symlinks.StorageAreas.Add(new MediaStorageArea
        {
            Id = "second",
            Name = "Second",
            MoviesRoot = LibManagarr("other-movies"),
            SeriesRoot = LibManagarr("other-series")
        });
        settings.Symlinks.PathMappings.Single(mapping => mapping.Kind == ArrApplicationKind.Sonarr).LibManagarrRoot =
            LibManagarr("other-series");
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), new FakeLinkFileSystem(), settings);

        SymlinkLinkPlan plan = await service.PreviewAsync(Pair, RadarrFileId, CancellationToken.None);

        // Two shelves can be two mounts, and a link across them is not covered by the probe that passed.
        Assert.False(plan.Runnable);
        Assert.Equal("no-common-storage-area", Assert.Single(plan.Blockers).Code);
    }

    [Fact]
    public async Task AnEntryAlreadySittingAtTheLinkPathIsRefused()
    {
        FakeLinkFileSystem files = new();
        files.Entries[ExpectedLinkPath()] = null;
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), files);

        SymlinkLinkPlan plan = await service.PreviewAsync(Pair, RadarrFileId, CancellationToken.None);

        Assert.False(plan.Runnable);
        Assert.Equal("link-path-taken", Assert.Single(plan.Blockers).Code);
        Assert.Empty(files.CreatedLinks);
    }

    [Fact]
    public async Task AnInstanceThatDoesNotAnswerIsAnUnavailableAndNotAWrongPair()
    {
        StubArrHandler handler = new(HappyRoutes()) { Throws = new HttpRequestException("connection refused") };
        SymlinkService service = CreateService(handler, new FakeLinkFileSystem());

        SymlinkLinkPlan plan = await service.PreviewAsync(Pair, RadarrFileId, CancellationToken.None);

        Assert.False(plan.Runnable);
        Assert.Equal("arr-unreachable", Assert.Single(plan.Blockers).Code);

        ApiProblemException problem = await Assert.ThrowsAsync<ApiProblemException>(
            () => service.CreateAsync(Pair, RadarrFileId, ConfigRevision, null, CancellationToken.None));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.Empty(await store.GetLinksAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TheConfirmWritesTheLinkAndConfirmsTheEntryAndTheSuggestion()
    {
        await SeedSuggestionAsync();
        FakeLinkFileSystem files = new();
        files.Entries[SourcePath()] = null;
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), files);

        SymlinkLink link = await service.CreateAsync(Pair, RadarrFileId, ConfigRevision, "suggestion-1", CancellationToken.None);

        Assert.NotEqual(string.Empty, link.Id);
        Assert.Equal(SymlinkOperationStatus.Active, link.OperationStatus);
        Assert.Equal(SymlinkSetupStep.MonitoringDisabled, link.SetupStep);
        Assert.Equal("suggestion-1", link.SuggestionId);
        Assert.Equal(ExpectedLinkPath(), link.LinkPath);
        Assert.Equal(SourcePath(), link.SourcePath);
        Assert.Empty(link.Findings);
        Assert.Equal(1, link.Revision);

        // The specials folder is created when the series has none yet, and the link goes into it.
        Assert.Equal([Path.GetDirectoryName(ExpectedLinkPath())!], files.CreatedFolders);
        Assert.Equal([ExpectedLinkPath()], files.CreatedLinks);
        // Relative, because that is the shape that survives the same shelf being mounted elsewhere.
        Assert.False(Path.IsPathRooted(link.LinkText));
        Assert.Equal(
            Path.GetFullPath(SourcePath()),
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(link.LinkPath)!, link.LinkText)));

        SymlinkSuggestion suggestion = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
        Assert.Equal(SymlinkSuggestionStatus.Completed, suggestion.Status);
        Assert.Equal(link.Id, suggestion.LinkId);
        Assert.Equal(Start, suggestion.DecidedAt);
        // What Sonarr still has to do with the entry is owed work, so the write asks for a pass instead of
        // holding this call open until an instance answers.
        Assert.True(await IsPassRequestedAsync());
    }

    [Fact]
    public async Task ARefusedWriteKeepsTheOwedEntryAndTheOpenSuggestion()
    {
        await SeedSuggestionAsync();
        FakeLinkFileSystem files = new();
        files.Entries[SourcePath()] = null;
        files.RefusedWrites.Add(ExpectedLinkPath());
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), files);

        ApiProblemException problem = await Assert.ThrowsAsync<ApiProblemException>(
            () => service.CreateAsync(Pair, RadarrFileId, ConfigRevision, "suggestion-1", CancellationToken.None));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        SymlinkLink owed = Assert.Single(await store.GetLinksAsync(CancellationToken.None));
        // The row is what makes the operation resumable, so it stays — as a conflict, not as an active entry.
        Assert.Equal(SymlinkOperationStatus.Conflict, owed.OperationStatus);
        Assert.Equal(SymlinkSetupStep.LinkCreated, owed.SetupStep);
        Assert.Equal("link-not-created", Assert.Single(owed.Findings).Code);
        SymlinkSuggestion suggestion = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
        Assert.Equal(SymlinkSuggestionStatus.Creating, suggestion.Status);
        Assert.Empty(files.Removed);
    }

    [Fact]
    public async Task ASecondEntryForTheSameSpecialIsRefusedBeforeAnythingIsRead()
    {
        await SeedLinkAsync();
        StubArrHandler handler = new(HappyRoutes());
        FakeLinkFileSystem files = new();
        SymlinkService service = CreateService(handler, files);

        ApiProblemException problem = await Assert.ThrowsAsync<ApiProblemException>(
            () => service.CreateAsync(Pair, RadarrFileId, ConfigRevision, null, CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        // The register already answers this, so neither instance is asked and no file is touched.
        Assert.Empty(handler.Requests);
        Assert.Empty(files.CreatedLinks);
    }

    [Fact]
    public async Task AConfirmFromAnOutdatedPreviewIsRefused()
    {
        StubArrHandler handler = new(HappyRoutes());
        FakeLinkFileSystem files = new();
        SymlinkService service = CreateService(handler, files);

        ApiProblemException problem = await Assert.ThrowsAsync<ApiProblemException>(
            () => service.CreateAsync(Pair, RadarrFileId, ConfigRevision + 1, null, CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Contains("path configuration changed", problem.Title);
        Assert.Empty(handler.Requests);
        Assert.Empty(await store.GetLinksAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DeletingRemovesTheLinkAsALinkWithoutRejectingThePair()
    {
        SymlinkLink link = await SeedLinkAsync();
        FakeLinkFileSystem files = new();
        files.Entries[link.LinkPath] = link.LinkText;
        files.Entries[link.SourcePath] = null;
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), files);

        Assert.True(await service.DeleteAsync(link.Id, link.Revision, CancellationToken.None));

        Assert.Equal(SymlinkOperationStatus.CleanupPending, (await StoredAsync(link.Id)).OperationStatus);
        Assert.Equal(SymlinkSetupOutcome.Completed, await service.FinishDeletionAsync(await StoredAsync(link.Id), CancellationToken.None));
        Assert.Null(await store.GetLinkAsync(link.Id, CancellationToken.None));
        Assert.Equal([link.LinkPath], files.Removed);
        // The source is a movie file someone else paid for: it is never part of removing a link.
        Assert.DoesNotContain(files.Removed, path => path == link.SourcePath);
        Assert.True(files.Entries.ContainsKey(link.SourcePath));
        // Removing a managed link is independent of rejecting a suggestion.
        Assert.False(await store.IsRejectedAsync(Pair, CancellationToken.None));
    }

    [Fact]
    public async Task DeletingAnEntryThatIsNotALinkKeepsTheEntryAndTheRow()
    {
        SymlinkLink link = await SeedLinkAsync();
        FakeLinkFileSystem files = new();
        // A copy took the place of the link. Deleting it would delete a file this feature never made.
        files.Entries[link.LinkPath] = null;
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), files);

        ApiProblemException problem = await Assert.ThrowsAsync<ApiProblemException>(
            () => service.DeleteAsync(link.Id, link.Revision, CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal([link.LinkPath], files.Entries.Keys);
        SymlinkLink stored = await StoredAsync(link.Id);
        Assert.Equal(SymlinkOperationStatus.Conflict, stored.OperationStatus);
        Assert.Equal("delete-entry-not-a-link", Assert.Single(stored.Findings).Code);
        Assert.False(await store.IsRejectedAsync(Pair, CancellationToken.None));
    }

    [Fact]
    public async Task DeletingAnUnknownEntryIsAnsweredAsMissing()
    {
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), new FakeLinkFileSystem());

        Assert.False(await service.DeleteAsync("absent", expectedRevision: 0, CancellationToken.None));
    }

    [Fact]
    public async Task AWriteAgainstAnEntryThatChangedInBetweenIsRefused()
    {
        SymlinkLink link = await SeedLinkAsync();
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), new FakeLinkFileSystem());

        ApiProblemException edit = await Assert.ThrowsAsync<ApiProblemException>(
            () => service.SetAutoFollowUpAsync(link.Id, enabled: false, link.Revision + 5, CancellationToken.None));
        Assert.Equal(StatusCodes.Status409Conflict, edit.StatusCode);

        ApiProblemException delete = await Assert.ThrowsAsync<ApiProblemException>(
            () => service.DeleteAsync(link.Id, link.Revision + 5, CancellationToken.None));
        Assert.Equal(StatusCodes.Status409Conflict, delete.StatusCode);

        SymlinkLink stored = await StoredAsync(link.Id);
        Assert.True(stored.AutoFollowUp);
        Assert.Equal(link.Revision, stored.Revision);
    }

    [Fact]
    public async Task FollowingLaterUpgradesCanBeWithdrawnPerEntry()
    {
        SymlinkLink link = await SeedLinkAsync();
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), new FakeLinkFileSystem());

        SymlinkLink? updated = await service.SetAutoFollowUpAsync(link.Id, enabled: false, link.Revision, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.False(updated!.AutoFollowUp);
        Assert.Equal(link.Revision + 1, updated.Revision);
        Assert.False((await StoredAsync(link.Id)).AutoFollowUp);
    }

    [Fact]
    public async Task EditingAnUnknownEntryIsAnsweredAsMissing()
    {
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), new FakeLinkFileSystem());

        Assert.Null(await service.SetAutoFollowUpAsync("absent", enabled: false, 0, CancellationToken.None));
    }

    [Fact]
    public async Task ARowThatOwesItsLinkAdoptsAnEntryThatPointsAtTheSameSource()
    {
        SymlinkLink owed = await SeedOwedRowAsync();
        FakeLinkFileSystem files = new();
        // The run that died made this entry and never got to say so here, so the target it points at is
        // the only proof that this entry and this row belong together.
        files.Entries[owed.LinkPath] = owed.LinkText;
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), files);

        Assert.Equal(SymlinkSetupOutcome.Completed, await service.FinishCreationAsync(owed, CancellationToken.None));

        SymlinkLink stored = await StoredAsync(owed.Id);
        Assert.Equal(SymlinkOperationStatus.Active, stored.OperationStatus);
        Assert.Equal(SymlinkSetupStep.MonitoringDisabled, stored.SetupStep);
        // Adopting is not rewriting: nothing was written and nothing of that entry was touched.
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(files.Removed);
    }

    [Fact]
    public async Task ARowThatOwesItsLinkWritesTheMissingOneAndLetsGoOfItsConflict()
    {
        SymlinkLink owed = await SeedOwedRowAsync(conflictWith: "link-not-created");
        FakeLinkFileSystem files = new();
        files.Entries[SourcePath()] = null;
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), files);

        Assert.Equal(SymlinkSetupOutcome.Completed, await service.FinishCreationAsync(owed, CancellationToken.None));

        Assert.Equal([owed.LinkPath], files.CreatedLinks);
        SymlinkLink stored = await StoredAsync(owed.Id);
        Assert.Equal(SymlinkOperationStatus.Active, stored.OperationStatus);
        // The obstacle is gone once the link is proven, so the finding that named it does not stay behind.
        Assert.Empty(stored.Findings);
        Assert.True(await IsPassRequestedAsync());
    }

    [Fact]
    public async Task AnEntryThatPointsSomewhereElseIsNotAdoptedAndStaysAsItIs()
    {
        SymlinkLink owed = await SeedOwedRowAsync();
        FakeLinkFileSystem files = new();
        files.Entries[owed.LinkPath] = @"..\movies\a film nobody asked for.mkv";
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), files);

        Assert.Equal(SymlinkSetupOutcome.Blocked, await service.FinishCreationAsync(owed, CancellationToken.None));

        SymlinkLink stored = await StoredAsync(owed.Id);
        Assert.Equal(SymlinkOperationStatus.Conflict, stored.OperationStatus);
        Assert.Equal("link-entry-not-ours", Assert.Single(stored.Findings).Code);
        Assert.Equal([owed.LinkPath], files.Entries.Keys);
        Assert.Empty(files.Removed);
    }

    [Fact]
    public async Task AnOwedDeletionIsFinishedWithoutRejectingThePair()
    {
        SymlinkLink owed = await SeedOwedRowAsync(SymlinkOperationStatus.DeletePending, SymlinkSetupStep.Completed);
        FakeLinkFileSystem files = new();
        files.Entries[owed.LinkPath] = owed.LinkText;
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), files);

        Assert.Equal(SymlinkSetupOutcome.Completed, await service.FinishDeletionAsync(await StoredAsync(owed.Id), CancellationToken.None));

        Assert.Null(await store.GetLinkAsync(owed.Id, CancellationToken.None));
        Assert.Equal([owed.LinkPath], files.Removed);
        Assert.False(await store.IsRejectedAsync(Pair, CancellationToken.None));
        Assert.Empty(await store.GetRejectionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DeletionKeepsItsJournalUntilTheTargetedLibrarySyncSucceeds()
    {
        SymlinkLink link = await SeedLinkAsync();
        FakeLinkFileSystem files = new();
        files.Entries[link.LinkPath] = link.LinkText;
        files.Entries[link.SourcePath] = null;
        TargetedSyncStub sync = new();
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), files, snapshotSync: sync);
        Assert.True(await service.DeleteAsync(link.Id, link.Revision, CancellationToken.None));

        SymlinkLink journal = await StoredAsync(link.Id);
        await Assert.ThrowsAsync<HttpRequestException>(() => service.FinishDeletionAsync(journal, CancellationToken.None));
        Assert.Equal(SymlinkOperationStatus.CleanupPending, (await StoredAsync(link.Id)).OperationStatus);
        Assert.False(files.EntryExists(link.LinkPath));
        sync.Fail = false;
        Assert.Equal(SymlinkSetupOutcome.Completed, await service.FinishDeletionAsync(await StoredAsync(link.Id), CancellationToken.None));
        Assert.Null(await store.GetLinkAsync(link.Id, CancellationToken.None));
        Assert.Equal(2, sync.Calls);
        Assert.Equal(SonarrInstanceId, sync.InstanceId);
        Assert.Equal(SeriesId, sync.SeriesId);
        Assert.Equal([link.LinkPath], files.Removed);
        Assert.True(files.EntryExists(link.SourcePath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeletionTargetsOnlyItsOwnedSonarrFileWithoutARescan(bool ownedPath)
    {
        SymlinkLink link = await SeedLinkAsync();
        FakeLinkFileSystem files = new();
        files.Entries[link.LinkPath] = link.LinkText;
        files.Entries[link.SourcePath] = null;
        Dictionary<string, List<string>> routes = HappyRoutes();
        routes["GET /api/v3/episode/5"] =
        [
            """{"id":5,"seriesId":12,"episodeFileId":99,"hasFile":true}""",
            """{"id":5,"seriesId":12,"episodeFileId":0,"hasFile":false}"""
        ];
        string path = ownedPath ? SeriesInstance($"{SymlinkFixture.SeriesFolder}/Specials/{SymlinkFixture.LinkName}")
            : SeriesInstance("another-series/another-episode.mkv");
        routes["GET /api/v3/episodefile/99"] = [System.Text.Json.JsonSerializer.Serialize(new { id = 99, path })];
        StubArrHandler handler = new(routes);
        SymlinkService service = CreateService(handler, files);

        Assert.True(await service.DeleteAsync(link.Id, link.Revision, CancellationToken.None));
        Assert.Equal(SymlinkOperationStatus.CleanupPending, (await StoredAsync(link.Id)).OperationStatus);
        Assert.Equal(SymlinkSetupOutcome.Completed,
            await service.FinishDeletionAsync(await StoredAsync(link.Id), CancellationToken.None));

        Assert.Equal(ownedPath ? 1 : 0, handler.Requests.Count(request => request.Method == "DELETE"));
        Assert.DoesNotContain(handler.Requests, request => request.Path == "/api/v3/command");
        Assert.DoesNotContain(handler.Requests, request => request.Method == "DELETE" && request.Path != "/api/v3/episodefile/99");
        Assert.Null(await store.GetLinkAsync(link.Id, CancellationToken.None));
        Assert.Equal([link.LinkPath], files.Removed);
        Assert.True(files.EntryExists(link.SourcePath));
        Assert.Empty(await store.GetRejectionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AnOwedDeletionOfAnEntryThatIsNotALinkIsWrittenDownAgainInsteadOfRemoved()
    {
        SymlinkLink owed = await SeedOwedRowAsync(SymlinkOperationStatus.DeletePending, SymlinkSetupStep.Completed);
        FakeLinkFileSystem files = new();
        // A copy took the place of the link while the deletion was owed. Removing it would remove a file
        // this feature never made, so the row stays with the finding instead.
        files.Entries[owed.LinkPath] = null;
        SymlinkService service = CreateService(new StubArrHandler(HappyRoutes()), files);

        Assert.Equal(SymlinkSetupOutcome.Blocked, await service.FinishDeletionAsync(owed, CancellationToken.None));

        Assert.Equal("delete-entry-not-a-link", Assert.Single((await StoredAsync(owed.Id)).Findings).Code);
        Assert.Empty(files.Removed);
        Assert.False(await store.IsRejectedAsync(Pair, CancellationToken.None));
    }

    private sealed class TargetedSyncStub : ISnapshotSyncService
    {
        public bool Fail { get; set; } = true;
        public int Calls { get; private set; }
        public string? InstanceId { get; private set; }
        public int SeriesId { get; private set; }
        public Task<SyncInstanceResult> SyncSonarrSeriesAsync(string instanceId, int seriesId, CancellationToken token)
        {
            Calls++;
            InstanceId = instanceId;
            SeriesId = seriesId;
            return Fail ? Task.FromException<SyncInstanceResult>(new HttpRequestException("History unavailable"))
                : Task.FromResult(new SyncInstanceResult());
        }
        public Task<SyncInstanceResult> SyncAsync(SyncInstanceRequest request, CancellationToken token) =>
            throw new InvalidOperationException("Deletion must use a targeted series sync.");
    }

    private async Task AssertBlockedAsync(
        Dictionary<string, List<string>> routes,
        string expectedCode,
        int expectedStatus = StatusCodes.Status409Conflict)
    {
        FakeLinkFileSystem files = new();
        SymlinkService service = CreateService(new StubArrHandler(routes), files);

        SymlinkLinkPlan plan = await service.PreviewAsync(Pair, RadarrFileId, CancellationToken.None);
        Assert.False(plan.Runnable);
        SymlinkFinding blocker = Assert.Single(plan.Blockers);
        Assert.Equal(expectedCode, blocker.Code);

        // The preview says no and the create says the same no with the same reason: a blocker is not
        // something the interface can walk past by calling the other action.
        ApiProblemException problem = await Assert.ThrowsAsync<ApiProblemException>(
            () => service.CreateAsync(Pair, RadarrFileId, ConfigRevision, null, CancellationToken.None));
        Assert.Equal(expectedStatus, problem.StatusCode);
        Assert.Equal(blocker.Detail, problem.Detail);
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(await store.GetLinksAsync(CancellationToken.None));
    }

    private SymlinkService CreateService(StubArrHandler handler, ISymlinkFileSystem files, AppSettings? settings = null, ISnapshotSyncService? snapshotSync = null)
    {
        FixedSettings snapshot = new(settings ?? Settings());
        return new SymlinkService(
            store,
            snapshot,
            new MediaPathMappingService(snapshot),
            new SymlinkCapabilityService(store, snapshot, files, clock, NullLogger<SymlinkCapabilityService>.Instance),
            setupRequests,
            new RadarrClient(new HttpClient(handler)),
            new SonarrClient(new HttpClient(handler)),
            files,
            clock,
            NullLogger<SymlinkService>.Instance, snapshotSync: snapshotSync);
    }

    /// <summary>
    /// The in-memory register answers synchronously, which is what lets the fixture seed it from its
    /// constructor instead of repeating the call in every test.
    /// </summary>
    private void SeedCapability(SymlinkCapabilityStatus status, int configRevision) =>
        store.SaveCapabilityCheckAsync(new SymlinkCapabilityCheck
        {
            StorageAreaId = StorageAreaId,
            Status = status,
            Detail = status == SymlinkCapabilityStatus.Available
                ? "File symlinks work in the series root."
                : "Creating a link in the series root was denied.",
            CheckedAt = Start,
            ConfigRevision = configRevision
        }, CancellationToken.None).GetAwaiter().GetResult();

    private async Task SeedSuggestionAsync() =>
        await store.UpsertSuggestionAsync(new SymlinkSuggestion
        {
            Id = "suggestion-1",
            MatcherVersion = "1",
            Status = SymlinkSuggestionStatus.Pending,
            Pair = Pair,
            RadarrFileId = RadarrFileId,
            SeasonNumber = 0,
            EpisodeNumber = 5,
            ConfigRevision = ConfigRevision,
            CreatedAt = Start,
            UpdatedAt = Start
        }, CancellationToken.None);

    private Task<SymlinkLink> SeedLinkAsync() =>
        SeedRowAsync(SymlinkOperationStatus.Active, SymlinkSetupStep.Completed);

    /// <summary>A row an interrupted run left behind: registered, with its file-side half still owed.</summary>
    private Task<SymlinkLink> SeedOwedRowAsync(
        SymlinkOperationStatus operation = SymlinkOperationStatus.Pending,
        SymlinkSetupStep? setupStep = SymlinkSetupStep.LinkCreated,
        string? conflictWith = null) =>
        SeedRowAsync(operation, setupStep, conflictWith);

    private async Task<SymlinkLink> SeedRowAsync(
        SymlinkOperationStatus operation,
        SymlinkSetupStep? setupStep,
        string? conflictWith = null)
    {
        SymlinkLink link = new()
        {
            Id = "link-1",
            RadarrInstanceId = RadarrInstanceId,
            RadarrMovieId = MovieId,
            RadarrFileId = RadarrFileId,
            SonarrInstanceId = SonarrInstanceId,
            SonarrSeriesId = SeriesId,
            SonarrEpisodeId = EpisodeId,
            SeasonNumber = 0,
            EpisodeNumber = 5,
            StorageAreaId = StorageAreaId,
            LinkPath = ExpectedLinkPath(),
            LinkText = Path.GetRelativePath(Path.GetDirectoryName(ExpectedLinkPath())!, SourcePath()),
            SourcePath = SourcePath(),
            OperationStatus = operation,
            SetupStep = setupStep,
            AutoFollowUp = true,
            CreatedAt = Start,
            UpdatedAt = Start,
            Findings = conflictWith is null
                ? []
                : [new SymlinkFinding(conflictWith, "The write of the link was refused.", Start)]
        };
        await store.InsertLinkAsync(link, CancellationToken.None);
        return await StoredAsync("link-1");
    }

    /// <summary>
    /// Whether a setup pass was asked for. The signal holds one pending request, so one that was written
    /// is read at once while a missing one leaves the wait open — which is what the timeout is for.
    /// </summary>
    private async Task<bool> IsPassRequestedAsync()
    {
        Task signaled = setupRequests.WaitForRequestAsync(CancellationToken.None);
        return await Task.WhenAny(signaled, Task.Delay(TimeSpan.FromSeconds(1))) == signaled;
    }

    private async Task<SymlinkLink> StoredAsync(string id) =>
        await store.GetLinkAsync(id, CancellationToken.None) ?? throw new InvalidOperationException($"Entry {id} is missing.");

    private static Dictionary<string, List<string>> HappyRoutes() => SymlinkFixture.HappyRoutes();

    private static AppSettings Settings() => SymlinkFixture.Settings();

    private static string SourcePath() => SymlinkFixture.SourcePath();

    private static string ExpectedLinkPath() => SymlinkFixture.ExpectedLinkPath();

    private static string MoviesInstance(string relative) => SymlinkFixture.MoviesInstance(relative);

    private static string SeriesInstance(string relative) => SymlinkFixture.SeriesInstance(relative);

    private static string Instance(string shelf, string relative) => SymlinkTestPaths.Instance(shelf, relative);

    private static string LibManagarr(string relative) => SymlinkTestPaths.LibManagarr(relative);

    private static string Quote(string? value) => SymlinkTestPaths.Quote(value);
}
