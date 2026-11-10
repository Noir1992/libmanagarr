using System.Text.Json;
using System.Reflection;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LibManagarr.Api.Tests.Services;

public sealed class SymlinkAdoptionTests
{
    private static readonly SpecialMoviePair Pair = new(SymlinkFixture.RadarrInstanceId, SymlinkFixture.MovieId,
        SymlinkFixture.SonarrInstanceId, SymlinkFixture.SeriesId, SymlinkFixture.EpisodeId);
    private static string ExistingPath => SymlinkTestPaths.LibManagarr($"series/{SymlinkFixture.SeriesFolder}/Season 00/already-linked.mkv");
    private static string InstancePath => SymlinkFixture.SeriesInstance($"{SymlinkFixture.SeriesFolder}/Season 00/already-linked.mkv");

    [Fact]
    public async Task PreviewAndConfirmationEndpointsCarryTheAdoptionContractAndCompleteDraft()
    {
        Fixture fixture = Create();
        FixedSettings settings = new(fixture.Settings);
        await fixture.Store.UpsertSuggestionAsync(new SymlinkSuggestion
        {
            Id = "offer", Pair = Pair, RadarrFileId = SymlinkFixture.RadarrFileId, ConfigRevision = SymlinkFixture.ConfigRevision
        }, CancellationToken.None);
        PreviewSymlinkLinkRequest preview = new()
        {
            RadarrInstanceId = Pair.RadarrInstanceId, RadarrMovieId = Pair.RadarrMovieId, RadarrFileId = SymlinkFixture.RadarrFileId,
            SonarrInstanceId = Pair.SonarrInstanceId, SonarrSeriesId = Pair.SonarrSeriesId, SonarrEpisodeId = Pair.SonarrEpisodeId
        };
        Task<Ok<SymlinkLinkPlanDto>> previewTask = (Task<Ok<SymlinkLinkPlanDto>>)typeof(SymlinkEndpoints)
            .GetMethod("PreviewLinkAsync", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [preview, settings, fixture.Service, CancellationToken.None])!;
        SymlinkLinkPlanDto plan = (await previewTask).Value!;
        Assert.True(plan.AdoptsExisting);
        Assert.True(plan.Runnable);
        Assert.Equal(88, plan.Draft.SonarrFileId);
        Assert.Equal(ExistingPath, plan.Draft.LinkPath);
        Assert.Equal(SymlinkFixture.SourcePath(), plan.Draft.SourcePath);
        ConfirmSuggestionRequest confirm = new()
        {
            ConfigRevision = SymlinkFixture.ConfigRevision, SourceVersion = plan.Draft.SourceVersion,
            ExistingLink = new(plan.Draft.LinkPath, plan.Draft.LinkText, plan.Draft.SonarrFileId!.Value)
        };
        Task<Results<Accepted<SymlinkLinkDto>, NotFound, ProblemHttpResult>> confirmTask =
            (Task<Results<Accepted<SymlinkLinkDto>, NotFound, ProblemHttpResult>>)typeof(SymlinkEndpoints)
            .GetMethod("ConfirmSuggestionAsync", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, ["offer", confirm, settings, fixture.Service, fixture.Store, CancellationToken.None])!;
        SymlinkLinkDto adopted = Assert.IsType<Accepted<SymlinkLinkDto>>((await confirmTask).Result).Value!;
        Assert.Equal(88, adopted.SonarrFileId);
        Assert.Equal(SymlinkOperationStatus.Active, adopted.OperationStatus);
        Assert.Empty(fixture.Files.CreatedLinks);
    }

    [RealSymlinkFact]
    public async Task ARealExistingSymlinkIsRegisteredWithoutChangingItsTargetOrTheMovie()
    {
        string root = Path.Combine(Path.GetTempPath(), "libmanagarr-adoption-" + Guid.NewGuid().ToString("N"));
        string movies = Path.Combine(root, "movies");
        string series = Path.Combine(root, "series");
        string source = Path.Combine(movies, SymlinkFixture.MovieFolder, SymlinkFixture.SourceFileName);
        string episode = Path.Combine(series, SymlinkFixture.SeriesFolder, "Season 00", "already-linked.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(episode)!);
        try
        {
            await File.WriteAllTextAsync(source, "original movie contents");
            DateTime sourceModified = File.GetLastWriteTimeUtc(source);
            string target = Path.GetRelativePath(Path.GetDirectoryName(episode)!, source);
            File.CreateSymbolicLink(episode, target);
            string[] before = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Order().ToArray();
            AppSettings settings = SymlinkFixture.Settings();
            settings.Symlinks.StorageAreas[0].MoviesRoot = movies;
            settings.Symlinks.StorageAreas[0].SeriesRoot = series;
            foreach (ArrPathMapping mapping in settings.Symlinks.PathMappings)
                mapping.LibManagarrRoot = mapping.Kind == ArrApplicationKind.Radarr ? movies : series;
            InMemorySymlinkStore store = new();
            SymlinkService service = Service(settings, new RealSymlinkFileSystem(), new StubArrHandler(Routes()), store);
            SymlinkLinkPlan plan = await service.PreviewAsync(Pair, SymlinkFixture.RadarrFileId, CancellationToken.None);
            Assert.True(plan.Runnable, string.Join(' ', plan.Blockers.Select(value => value.Detail)));
            SymlinkLink adopted = await service.CreateAsync(Pair, SymlinkFixture.RadarrFileId, SymlinkFixture.ConfigRevision,
                null, CancellationToken.None, plan.Draft.SourceVersion, Consent(plan));
            Assert.Equal(episode, adopted.LinkPath);
            Assert.Equal(target, new FileInfo(episode).LinkTarget);
            Assert.Equal("original movie contents", await File.ReadAllTextAsync(episode));
            Assert.Equal(sourceModified, File.GetLastWriteTimeUtc(source));
            Assert.Equal(before, Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Order().ToArray());
            Assert.Equal(SymlinkSetupStep.Completed, adopted.SetupStep);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingMatchingLinkIsPreviewedAndAdoptedWithoutFilesystemOrArrWrites(bool absolute)
    {
        Fixture fixture = Create(absolute);
        await fixture.Store.UpsertSuggestionAsync(new SymlinkSuggestion
        {
            Id = "offer", Pair = Pair, RadarrFileId = SymlinkFixture.RadarrFileId,
            Status = SymlinkSuggestionStatus.Pending, ConfigRevision = SymlinkFixture.ConfigRevision
        }, CancellationToken.None);
        string before = fixture.Files.Entries[ExistingPath]!;
        SymlinkLinkPlan plan = await fixture.Service.PreviewAsync(Pair, SymlinkFixture.RadarrFileId, CancellationToken.None);

        Assert.True(plan.Runnable, string.Join(' ', plan.Blockers.Select(finding => finding.Detail)));
        Assert.True(plan.AdoptsExisting);
        Assert.Equal(SymlinkFixture.SourcePath(), plan.Draft.SourcePath);
        Assert.Equal(ExistingPath, plan.Draft.LinkPath);
        Assert.Equal(before, plan.Draft.LinkText);
        Assert.Equal("already-linked.mkv", plan.Draft.DesiredFilename);
        Assert.Equal("WFFF", plan.Draft.ExpectedMetadata?.ReleaseGroup);
        Assert.Equal(["German"], plan.Draft.ExpectedMetadata!.Languages);
        Assert.Equal("WEBDL-1080p", plan.Draft.ExpectedMetadata.Quality);

        SymlinkLink adopted = await fixture.Service.CreateAsync(Pair, SymlinkFixture.RadarrFileId,
            SymlinkFixture.ConfigRevision, "offer", CancellationToken.None, plan.Draft.SourceVersion, Consent(plan));
        Assert.Equal(SymlinkOperationStatus.Active, adopted.OperationStatus);
        Assert.Equal(SymlinkSetupStep.Completed, adopted.SetupStep);
        Assert.Equal(88, adopted.SonarrFileId);
        Assert.Equal(ExistingPath, adopted.LinkPath);
        Assert.Equal(before, fixture.Files.Entries[ExistingPath]);
        Assert.Equal("sonarr", adopted.ConfirmedMetadata?.Origin);
        Assert.True(adopted.MonitoringOriginal);
        Assert.False(adopted.MonitoringDisabled); // Existing monitoring is deliberately unchanged.
        Assert.NotNull(adopted.NamingRevision);
        Assert.True(fixture.Service.ResolvesToSource(adopted));
        Assert.Equal(SymlinkSuggestionStatus.Completed, (await fixture.Store.GetSuggestionAsync("offer", CancellationToken.None))!.Status);
        Assert.Empty(fixture.Files.CreatedLinks);
        Assert.Empty(fixture.Files.CreatedFolders);
        Assert.Empty(fixture.Files.Removed);
        Assert.All(fixture.Handler.Requests, request => Assert.Equal("GET", request.Method));
    }

    [Fact]
    public async Task ExistingLinksCanBeAdoptedInTheSharedMediaDirectoryConfiguration()
    {
        Fixture fixture = Create(shared: true);
        SymlinkLinkPlan plan = await fixture.Service.PreviewAsync(Pair, SymlinkFixture.RadarrFileId, CancellationToken.None);
        Assert.True(plan.AdoptsExisting);
        Assert.True(plan.Runnable, string.Join(' ', plan.Blockers.Select(value => value.Detail)));
        SymlinkLink link = await fixture.Service.CreateAsync(Pair, SymlinkFixture.RadarrFileId,
            SymlinkFixture.ConfigRevision, null, CancellationToken.None, plan.Draft.SourceVersion, Consent(plan));
        Assert.Equal(ExistingPath, link.LinkPath);
        Assert.Empty(fixture.Settings.Symlinks.PathMappings);
        Assert.Empty(fixture.Files.CreatedLinks);
    }

    [Fact]
    public async Task MatchingLinkChainsRemainRecognizableAfterAdoption()
    {
        Fixture fixture = Create();
        string intermediate = SymlinkTestPaths.LibManagarr("movies/shortcut.mkv");
        fixture.Files.Entries[ExistingPath] = Path.GetRelativePath(Path.GetDirectoryName(ExistingPath)!, intermediate);
        fixture.Files.Entries[intermediate] = Path.GetRelativePath(Path.GetDirectoryName(intermediate)!, SymlinkFixture.SourcePath());
        SymlinkLinkPlan plan = await fixture.Service.PreviewAsync(Pair, SymlinkFixture.RadarrFileId, CancellationToken.None);
        Assert.True(plan.Runnable);
        SymlinkLink adopted = await fixture.Service.CreateAsync(Pair, SymlinkFixture.RadarrFileId,
            SymlinkFixture.ConfigRevision, null, CancellationToken.None, plan.Draft.SourceVersion, Consent(plan));
        Assert.True(fixture.Service.ResolvesToSource(adopted));
        Assert.Empty(fixture.Files.CreatedLinks);
        Assert.Empty(fixture.Files.Removed);
    }

    [Fact]
    public async Task ExistingLinkRequiresExplicitConsentFromThePreview()
    {
        Fixture fixture = Create();
        SymlinkLinkPlan plan = await fixture.Service.PreviewAsync(Pair, SymlinkFixture.RadarrFileId, CancellationToken.None);
        await Assert.ThrowsAsync<ApiProblemException>(() => fixture.Service.CreateAsync(Pair, SymlinkFixture.RadarrFileId,
            SymlinkFixture.ConfigRevision, null, CancellationToken.None, plan.Draft.SourceVersion));
        await Assert.ThrowsAsync<ApiProblemException>(() => fixture.Service.CreateAsync(Pair, SymlinkFixture.RadarrFileId,
            SymlinkFixture.ConfigRevision, null, CancellationToken.None, plan.Draft.SourceVersion,
            Consent(plan) with { LinkText = "different.mkv" }));
        Assert.Empty(await fixture.Store.GetLinksAsync(CancellationToken.None));
        Assert.Empty(fixture.Files.CreatedLinks);
    }

    [Fact]
    public async Task ALinkChangedAfterPreviewIsNotAdoptedOrOverwritten()
    {
        Fixture fixture = Create();
        SymlinkLinkPlan plan = await fixture.Service.PreviewAsync(Pair, SymlinkFixture.RadarrFileId, CancellationToken.None);
        string other = SymlinkTestPaths.LibManagarr("movies/other.mkv");
        fixture.Files.Entries[other] = null;
        fixture.Files.Entries[ExistingPath] = other;
        await Assert.ThrowsAsync<ApiProblemException>(() => fixture.Service.CreateAsync(Pair, SymlinkFixture.RadarrFileId,
            SymlinkFixture.ConfigRevision, null, CancellationToken.None, plan.Draft.SourceVersion, Consent(plan)));
        Assert.Empty(await fixture.Store.GetLinksAsync(CancellationToken.None));
        Assert.Equal(other, fixture.Files.Entries[ExistingPath]);
        Assert.Empty(fixture.Files.Removed);
    }

    [Theory]
    [InlineData("regular")]
    [InlineData("missing")]
    [InlineData("broken")]
    [InlineData("wrong-target")]
    public async Task UnsuitableExistingEntriesStayBlockedButThePreviewStillContainsKnownPathsAndMetadata(string kind)
    {
        Fixture fixture = Create();
        if (kind == "regular") fixture.Files.Entries[ExistingPath] = null;
        if (kind == "missing") fixture.Files.Entries.Remove(ExistingPath);
        if (kind == "broken") fixture.Files.Entries.Remove(SymlinkFixture.SourcePath());
        if (kind == "wrong-target")
        {
            string other = SymlinkTestPaths.LibManagarr("movies/other.mkv");
            fixture.Files.Entries[other] = null;
            fixture.Files.Entries[ExistingPath] = other;
        }
        SymlinkLinkPlan plan = await fixture.Service.PreviewAsync(Pair, SymlinkFixture.RadarrFileId, CancellationToken.None);
        Assert.False(plan.Runnable);
        Assert.False(plan.AdoptsExisting);
        Assert.Equal(SymlinkFixture.SourcePath(), plan.Draft.SourcePath);
        Assert.Equal(ExistingPath, plan.Draft.LinkPath);
        Assert.Equal("WFFF", plan.Draft.ExpectedMetadata?.ReleaseGroup);
        Assert.NotEmpty(plan.Blockers[0].Detail!);
        Assert.Empty(await fixture.Store.GetLinksAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdoptionRefreshesTheSeriesBeforePublishingACompletedManagedLink(bool unavailable)
    {
        TargetedSyncProbe sync = new() { Fail = unavailable };
        Fixture fixture = Create(snapshotSync: sync);
        SymlinkLinkPlan plan = await fixture.Service.PreviewAsync(Pair, SymlinkFixture.RadarrFileId, CancellationToken.None);
        Task<SymlinkLink> adoption = fixture.Service.CreateAsync(Pair, SymlinkFixture.RadarrFileId,
            SymlinkFixture.ConfigRevision, null, CancellationToken.None, plan.Draft.SourceVersion, Consent(plan));
        if (unavailable)
        {
            ApiProblemException error = await Assert.ThrowsAsync<ApiProblemException>(() => adoption);
            Assert.Equal(503, error.StatusCode);
            Assert.Empty(await fixture.Store.GetLinksAsync(CancellationToken.None));
        }
        else
        {
            SymlinkLink adopted = await adoption;
            Assert.Equal(SymlinkSetupStep.Completed, adopted.SetupStep);
            Assert.Single(await fixture.Store.GetLinksAsync(CancellationToken.None));
        }
        Assert.Equal((SymlinkFixture.SonarrInstanceId, 12), Assert.Single(sync.Requests));
        Assert.Empty(fixture.Files.CreatedLinks);
        Assert.Empty(fixture.Files.Removed);
        Assert.DoesNotContain(fixture.Handler.Requests, request => request.Method != "GET");
    }

    private static ExistingLinkConfirmation Consent(SymlinkLinkPlan plan) =>
        new(plan.Draft.LinkPath, plan.Draft.LinkText, plan.Draft.SonarrFileId!.Value);

    private static Fixture Create(bool absolute = false, bool shared = false, ISnapshotSyncService? snapshotSync = null)
    {
        AppSettings settings = SymlinkFixture.Settings();
        FakeLinkFileSystem files = new();
        files.Entries[SymlinkFixture.SourcePath()] = null;
        files.Entries[ExistingPath] = absolute ? SymlinkFixture.SourcePath()
            : Path.GetRelativePath(Path.GetDirectoryName(ExistingPath)!, SymlinkFixture.SourcePath());
        Dictionary<string, List<string>> routes = Routes();
        if (shared)
        {
            string movies = settings.Symlinks.StorageAreas[0].MoviesRoot;
            string series = settings.Symlinks.StorageAreas[0].SeriesRoot;
            settings.Symlinks = SymlinkSettingsRules.Normalize(new SymlinkSettings
            {
                MediaRoot = SymlinkTestPaths.LibManagarr(string.Empty), Revision = SymlinkFixture.ConfigRevision
            });
            routes["GET /api/v3/rootfolder"] = [JsonSerializer.Serialize(new[] { new { path = movies } }), JsonSerializer.Serialize(new[] { new { path = series } })];
            routes["GET /api/v3/moviefile"][0] = routes["GET /api/v3/moviefile"][0].Replace(
                JsonSerializer.Serialize(SymlinkFixture.MoviesInstance($"{SymlinkFixture.MovieFolder}/{SymlinkFixture.SourceFileName}")), JsonSerializer.Serialize(SymlinkFixture.SourcePath()));
            routes["GET /api/v3/series/12"][0] = routes["GET /api/v3/series/12"][0].Replace(
                JsonSerializer.Serialize(SymlinkFixture.SeriesInstance(SymlinkFixture.SeriesFolder)),
                JsonSerializer.Serialize(SymlinkTestPaths.LibManagarr($"series/{SymlinkFixture.SeriesFolder}")));
            routes["GET /api/v3/episodefile/88"][0] = routes["GET /api/v3/episodefile/88"][0].Replace(JsonSerializer.Serialize(InstancePath), JsonSerializer.Serialize(ExistingPath));
        }
        StubArrHandler handler = new(routes);
        InMemorySymlinkStore store = new();
        SymlinkService service = Service(settings, files, handler, store, snapshotSync);
        return new(service, store, files, handler, settings);
    }

    private static Dictionary<string, List<string>> Routes()
    {
        Dictionary<string, List<string>> routes = SymlinkFixture.HappyRoutes();
        routes["GET /api/v3/episode/5"] = ["""{"id":5,"seriesId":12,"seasonNumber":0,"episodeNumber":5,"hasFile":true,"episodeFileId":88,"monitored":true}"""];
        routes["GET /api/v3/episodefile/88"] = [JsonSerializer.Serialize(new
        {
            id = 88, path = InstancePath, releaseGroup = "WFFF", size = 5368709120L,
            languages = new[] { new { name = "German" } }, quality = new { quality = new { name = "WEBDL-1080p" } }
        })];
        routes["GET /api/v3/config/naming"] = ["""{"renameEpisodes":true,"standardEpisodeFormat":"{Series Title} - S{season:00}E{episode:00}"}"""];
        return routes;
    }

    private static SymlinkService Service(AppSettings settings, ISymlinkFileSystem files, StubArrHandler handler, InMemorySymlinkStore store, ISnapshotSyncService? snapshotSync = null)
    {
        FixedSettings snapshot = new(settings);
        HttpClient http = new(handler);
        RadarrClient radarr = new(http);
        SonarrClient sonarr = new(http);
        MediaPathMappingService paths = new(snapshot, files, new SharedMediaPathService(radarr, sonarr));
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        ExistingEpisodeFileInspector inspector = new(sonarr, radarr, paths, files, clock);
        SymlinkService service = new(store, snapshot, paths,
            new SymlinkCapabilityService(store, snapshot, files, clock, NullLogger<SymlinkCapabilityService>.Instance),
            new SymlinkSetupRequests(), radarr, sonarr, files, clock, NullLogger<SymlinkService>.Instance, existingFiles: inspector, snapshotSync: snapshotSync);
        return service;
    }

    private sealed record Fixture(SymlinkService Service, InMemorySymlinkStore Store, FakeLinkFileSystem Files, StubArrHandler Handler, AppSettings Settings);
}
