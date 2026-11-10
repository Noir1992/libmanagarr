using System.Text.Json;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibManagarr.Api.Tests.Services;

public sealed class SymlinkHardeningTests
{
    private readonly InMemorySymlinkStore store = new();
    private readonly FakeLinkFileSystem files = new();
    private readonly AppSettings settings = SymlinkFixture.Settings();
    private readonly SymlinkSetupRequests requests = new();
    private static SpecialMoviePair Pair => new(SymlinkFixture.RadarrInstanceId, 70, SymlinkFixture.SonarrInstanceId, 12, 5);

    public SymlinkHardeningTests()
    {
        files.Entries[SymlinkFixture.SourcePath()] = null;
        store.SaveCapabilityCheckAsync(new SymlinkCapabilityCheck { StorageAreaId = "main", Status = SymlinkCapabilityStatus.Available,
            CheckedAt = DateTimeOffset.UtcNow, ConfigRevision = settings.Symlinks.Revision }, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void UnixColonNamesAreValidWhileWindowsAlternateDataStreamsAreRefused()
    {
        Assert.True(ArrPathRules.TryParse("/media/movies/Film: Part 2.mkv", out _, out _));
        Assert.False(ArrPathRules.TryParse(@"C:\media\film.mkv:stream", out _, out _));
    }

    [Fact]
    public async Task DeletionRefusesASymlinkWhoseTargetWasChangedExternally()
    {
        SymlinkService service = Service();
        SymlinkLink link = await service.CreateAsync(Pair, 4321, settings.Symlinks.Revision, null, CancellationToken.None);
        files.Entries[link.LinkPath] = "foreign.mkv";
        ApiProblemException problem = await Assert.ThrowsAsync<ApiProblemException>(() => service.DeleteAsync(link.Id, link.Revision, CancellationToken.None));
        Assert.Equal(409, problem.StatusCode);
        Assert.Empty(files.Removed);
        Assert.Equal("foreign.mkv", files.ReadLinkTarget(link.LinkPath));
        Assert.NotNull(await store.GetLinkAsync(link.Id, CancellationToken.None));
    }

    [Fact]
    public async Task TargetedCleanupDoesNotDropTheJournalWhileSonarrStillAssociatesTheRemovedLink()
    {
        Dictionary<string, List<string>> routes = SymlinkFixture.HappyRoutes();
        SymlinkService service = Service(new StubArrHandler(routes));
        SymlinkLink link = await service.CreateAsync(Pair, 4321, settings.Symlinks.Revision, null, CancellationToken.None);
        await service.DeleteAsync(link.Id, link.Revision, CancellationToken.None);
        routes["GET /api/v3/episode/5"] = ["""{"id":5,"seriesId":12,"episodeFileId":99,"hasFile":true}"""];
        routes["GET /api/v3/episodefile/99"] = ["{\"id\":99,\"path\":" + JsonSerializer.Serialize(SymlinkFixture.SeriesInstance($"{SymlinkFixture.SeriesFolder}/Specials/{SymlinkFixture.LinkName}")) + "}"];
        Assert.Equal(SymlinkSetupOutcome.Waiting, await service.FinishDeletionAsync((await store.GetLinkAsync(link.Id, CancellationToken.None))!, CancellationToken.None));
        Assert.Equal(SymlinkOperationStatus.CleanupPending, (await store.GetLinkAsync(link.Id, CancellationToken.None))!.OperationStatus);
        routes["GET /api/v3/episode/5"] = ["""{"id":5,"hasFile":false,"episodeFileId":0}"""];
        Assert.Equal(SymlinkSetupOutcome.Completed, await service.FinishDeletionAsync((await store.GetLinkAsync(link.Id, CancellationToken.None))!, CancellationToken.None));
        Assert.Null(await store.GetLinkAsync(link.Id, CancellationToken.None));
        Assert.Single(files.Removed);
        Assert.True(files.EntryExists(link.SourcePath));
    }

    [Fact]
    public async Task LinkedSpecialsDirectoryBlocksThePreviewAndMutation()
    {
        string parent = Path.GetDirectoryName(SymlinkFixture.ExpectedLinkPath())!;
        files.Entries[parent] = SymlinkTestPaths.LibManagarr("outside");
        SymlinkService service = Service();
        SymlinkLinkPlan plan = await service.PreviewAsync(Pair, 4321, CancellationToken.None);
        Assert.False(plan.Runnable);
        Assert.Contains(plan.Blockers, finding => finding.Code == "linked-parent-directory");
        await Assert.ThrowsAsync<ApiProblemException>(() => service.CreateAsync(Pair, 4321, settings.Symlinks.Revision, null, CancellationToken.None));
        Assert.Empty(files.CreatedLinks);
    }

    [Fact]
    public async Task AProbeFromAnotherProcessDoesNotAuthorizeResumingACreation()
    {
        SymlinkService service = Service();
        SymlinkLink link = await service.CreateAsync(Pair, 4321, settings.Symlinks.Revision, null, CancellationToken.None);
        files.Entries.Remove(link.LinkPath);
        link.SetupStep = SymlinkSetupStep.LinkCreated;
        await store.UpdateLinkAsync(link, link.Revision, CancellationToken.None);
        await store.SaveCapabilityCheckAsync(new SymlinkCapabilityCheck { StorageAreaId = "main", Status = SymlinkCapabilityStatus.Available,
            CheckedAt = DateTimeOffset.UtcNow, ConfigRevision = settings.Symlinks.Revision, ProcessId = "previous-process" }, CancellationToken.None);
        Assert.Equal(SymlinkSetupOutcome.Waiting, await service.FinishCreationAsync((await store.GetLinkAsync(link.Id, CancellationToken.None))!, CancellationToken.None));
        Assert.Single(files.CreatedLinks);
        Assert.False(files.EntryExists(link.LinkPath));
    }

    [Theory]
    [InlineData("path")]
    [InlineData("group")]
    [InlineData("quality")]
    [InlineData("revision")]
    [InlineData("languages")]
    [InlineData("mediaInfo")]
    public void SameIdAndSizeDoNotHideSourceChanges(string changed)
    {
        RadarrMovieFileResource source = Source();
        string previous = SymlinkService.SourceVersionOf(source);
        switch (changed)
        {
            case "path": source.Path += ".new"; break;
            case "group": source.ReleaseGroup = "NEW"; break;
            case "quality": source.Quality!.Quality!.Name = "Bluray-1080p"; break;
            case "revision": source.Quality!.Revision = new(2, 0, true); break;
            case "languages": source.Languages = [new() { Name = "Japanese" }]; break;
            case "mediaInfo": source.MediaInfo = new() { VideoCodec = "AV1" }; break;
        }
        Assert.NotEqual(previous, SymlinkService.SourceVersionOf(source));
        Assert.Equal(4321, source.Id);
        Assert.Equal(SymlinkFixture.SourceSize, source.Size);
    }

    [Fact]
    public async Task ConfirmationRejectsASourceChangedSincePreviewEvenIfTheFileIdSurvives()
    {
        SymlinkService service = Service();
        SymlinkLinkPlan preview = await service.PreviewAsync(Pair, 4321, CancellationToken.None);
        Dictionary<string, List<string>> routes = SymlinkFixture.HappyRoutes();
        routes["GET /api/v3/moviefile"][0] = routes["GET /api/v3/moviefile"][0].Replace("WFFF", "CHANGED", StringComparison.Ordinal);
        ApiProblemException problem = await Assert.ThrowsAsync<ApiProblemException>(() => Service(new StubArrHandler(routes))
            .CreateAsync(Pair, 4321, settings.Symlinks.Revision, null, CancellationToken.None, preview.Draft.SourceVersion));
        Assert.Equal(409, problem.StatusCode);
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(await store.GetLinksAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EditedPairIsReservedAndResumesAfterTheOldSeriesCleanup()
    {
        SymlinkService service = Service();
        SymlinkLink original = await service.CreateAsync(Pair, 4321, settings.Symlinks.Revision, null, CancellationToken.None);
        original.SetupStep = SymlinkSetupStep.Completed;
        await store.UpdateLinkAsync(original, original.Revision, CancellationToken.None);
        original = (await store.GetLinkAsync(original.Id, CancellationToken.None))!;
        Dictionary<string, List<string>> routes = SymlinkFixture.HappyRoutes();
        routes["GET /api/v3/episode/6"] = ["""{ "id":6,"seriesId":12,"seasonNumber":0,"episodeNumber":6,"hasFile":false }"""];
        service = Service(new StubArrHandler(routes));
        SpecialMoviePair next = Pair with { SonarrEpisodeId = 6 };
        SymlinkLinkPlan plan = await service.PreviewAsync(next, 4321, CancellationToken.None, original.Id);
        SymlinkLink edited = (await service.EditAsync(original.Id, new SaveSymlinkLinkRequest { RadarrInstanceId = next.RadarrInstanceId,
            RadarrMovieId = next.RadarrMovieId, SonarrInstanceId = next.SonarrInstanceId, SonarrSeriesId = 12, SonarrEpisodeId = 6,
            RadarrFileId = 4321, Revision = original.Revision, ConfigRevision = settings.Symlinks.Revision,
            SourceVersion = plan.Draft.SourceVersion }, CancellationToken.None))!;
        Assert.Equal(Pair, edited.Pair);
        Assert.Equal(next, edited.PendingUpdate!.Pair);
        await Assert.ThrowsAsync<ApiProblemException>(() => service.CreateAsync(next, 4321, settings.Symlinks.Revision, null, CancellationToken.None));
        Assert.Equal(SymlinkSetupOutcome.Waiting, await service.FinishUpdateAsync(edited, CancellationToken.None));
        SymlinkLink interrupted = (await store.GetLinkAsync(original.Id, CancellationToken.None))!;
        Assert.NotNull(interrupted.PendingUpdate!.CleanupCommandId);
        Assert.Equal(SymlinkSetupOutcome.Completed, await Service(new StubArrHandler(routes)).FinishUpdateAsync(interrupted, CancellationToken.None));
        SymlinkLink done = (await store.GetLinkAsync(original.Id, CancellationToken.None))!;
        Assert.Equal(next, done.Pair);
        Assert.Null(done.PendingUpdate);
        Assert.Equal(SymlinkSetupStep.MonitoringDisabled, done.SetupStep);
        Assert.False(files.EntryExists(original.LinkPath));
        Assert.True(files.EntryExists(done.LinkPath));
        Assert.True(files.EntryExists(original.SourcePath));
        Assert.Equal(2, files.CreatedLinks.Count);
    }

    [Fact]
    public async Task ANewUpgradeDuringNamingFollowsTheActualLinkWithoutWaitingForTheOldSource()
    {
        SymlinkService service = Service();
        SymlinkLink link = await service.CreateAsync(Pair, 4321, settings.Symlinks.Revision, null, CancellationToken.None);
        string previous = link.LinkPath;
        string moved = Path.Combine(Path.GetDirectoryName(previous)!, "named-special.mkv");
        string text = Path.GetRelativePath(Path.GetDirectoryName(moved)!, link.SourcePath);
        link.SetupStep = SymlinkSetupStep.Renamed;
        link.SonarrFileId = SymlinkFixture.SonarrEpisodeFileId;
        link.NamingMove = new(previous, moved, text, "901");
        files.Entries[moved] = text;
        files.Entries.Remove(previous);
        files.Entries.Remove(link.SourcePath); // Radarr already removed v1.
        await store.UpdateLinkAsync(link, link.Revision, CancellationToken.None);
        settings.RadarrSnapshots = SymlinkFixture.SettingsWithLibraryFile(SymlinkFixture.UpgradedRadarrFileId, SymlinkFixture.UpgradedSize).RadarrSnapshots;
        string upgraded = SymlinkTestPaths.LibManagarr($"movies/{SymlinkFixture.MovieFolder}/{SymlinkFixture.UpgradedFileName}");
        files.Entries[upgraded] = null;
        service = Service(new StubArrHandler(SymlinkFixture.UpgradeRoutes()));
        Assert.Equal(1, await service.MarkReplacedSourcesAsync(CancellationToken.None));
        SymlinkLink marked = (await store.GetLinkAsync(link.Id, CancellationToken.None))!;
        Assert.Null(marked.NamingMove);
        Assert.Equal(moved, marked.LinkPath);
        Assert.Equal(SymlinkSetupOutcome.Completed, await service.FinishUpdateAsync(marked, CancellationToken.None));
        SymlinkLink updated = (await store.GetLinkAsync(link.Id, CancellationToken.None))!;
        Assert.Equal(upgraded, updated.SourcePath);
        Assert.Equal(SymlinkSetupStep.MonitoringDisabled, updated.SetupStep);
        Assert.True(files.EntryExists(updated.LinkPath));
        Assert.False(files.EntryExists(moved));
        Assert.True(files.EntryExists(upgraded));
    }

    [Fact]
    public async Task LiveInstanceFailureCannotReuseAnOldHealthySnapshot()
    {
        SymlinkService service = Service();
        SymlinkLink link = await service.CreateAsync(Pair, 4321, settings.Symlinks.Revision, null, CancellationToken.None);
        link.SetupStep = SymlinkSetupStep.Completed;
        await store.UpdateLinkAsync(link, link.Revision, CancellationToken.None);
        StubArrHandler down = new(SymlinkFixture.HappyRoutes()) { Throws = new HttpRequestException("offline") };
        SymlinkLiveStateService live = new(new RadarrClient(new HttpClient(down)), new SonarrClient(new HttpClient(down)), TimeProvider.System);
        SymlinkHealthService health = new(store, new FixedSettings(settings), service, files, requests, TimeProvider.System,
            NullLogger<SymlinkHealthService>.Instance, live);
        SymlinkHealthRun result = await health.CheckAllAsync(CancellationToken.None);
        Assert.Equal(1, result.Undetermined);
        SymlinkLink checkedLink = (await store.GetLinkAsync(link.Id, CancellationToken.None))!;
        Assert.Contains(checkedLink.Findings, finding => finding.Code == "arr-unreachable");
        Assert.Null(checkedLink.LastSuccessfulCheckAt);
        Assert.True(files.EntryExists(link.LinkPath));
    }

    private SymlinkService Service(StubArrHandler? handler = null)
    {
        handler ??= new(SymlinkFixture.HappyRoutes());
        FixedSettings configured = new(settings);
        return new(store, configured, new MediaPathMappingService(configured, files),
            new SymlinkCapabilityService(store, configured, files, TimeProvider.System, NullLogger<SymlinkCapabilityService>.Instance),
            requests, new RadarrClient(new HttpClient(handler)), new SonarrClient(new HttpClient(handler)), files, TimeProvider.System,
            NullLogger<SymlinkService>.Instance);
    }

    private static RadarrMovieFileResource Source() => JsonSerializer.Deserialize<List<RadarrMovieFileResource>>(
        SymlinkFixture.HappyRoutes()["GET /api/v3/moviefile"][0], new JsonSerializerOptions(JsonSerializerDefaults.Web))![0];
}
