using LibManagarr.Api.Clients;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using LibManagarr.Api.Tests.Services;

namespace LibManagarr.Api.Tests.Endpoints;

public sealed class SymlinkSettingsEndpointTests
{
    // The server hands out the area identifiers and refuses to trust a foreign one, so a fixture that
    // wanted its answers to be found again has to use identifiers that survive that check.
    private static readonly string Area1 = UuidV7.Create();
    private static readonly string Area2 = UuidV7.Create();
    private static readonly ArrInstanceSettings Radarr = new() { Id = "radarr-1", Name = "Radarr", BaseUrl = "http://localhost:7878", ApiKey = "k" };

    [Fact]
    public async Task Save_RejectsMissingDirectoryWithoutCreatingItOrReplacingSavedSettings()
    {
        StubSettingsService settings = new(Settings(4));
        InMemorySymlinkStore store = new();
        string json = JsonSerializer.Serialize(new { mediaRoot = LibManagarr("missing"), enabled = false });
        Results<Ok<SymlinkSettingsDto>, ValidationProblem> result = await SaveResult(settings, store, json, Capability(settings, store));
        Assert.Contains("symlinks.mediaRoot", Assert.IsType<ValidationProblem>(result.Result).ProblemDetails.Errors);
        Assert.Equal(0, settings.Saves);
        Assert.Equal(4, settings.Settings.Symlinks.Revision);
        Assert.Empty(await store.GetCapabilityChecksAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Save_CannotEnableUnsupportedFilesystemEvenWhenRequestedDirectly()
    {
        StubSettingsService settings = new(new AppSettings { Symlinks = new SymlinkSettings { Enabled = false } });
        InMemorySymlinkStore store = new();
        string json = JsonSerializer.Serialize(new { mediaRoot = LibManagarr("library"), enabled = true });
        SymlinkCapabilityService capability = SaveCapability(settings, store, json, new PlatformNotSupportedException("No file symlinks."));
        SymlinkSettingsDto result = await UnwrapOk(SaveResult(settings, store, json, capability));
        Assert.False(result.Enabled);
        Assert.Equal(SymlinkCapabilityStatus.Unsupported, Assert.Single(result.CapabilityChecks).Status);
        Assert.Contains("No file symlinks", result.CapabilityChecks[0].Detail);
        Assert.False(settings.Settings.Symlinks.Enabled);
    }

    [Fact]
    public async Task AutomaticCheckWorksWhileDisabledAndDoesNotEnableWithoutConsent()
    {
        StubSettingsService settings = new(new AppSettings { Symlinks = SymlinkSettingsRules.Normalize(new SymlinkSettings { MediaRoot = LibManagarr("library"), Enabled = false }) });
        InMemorySymlinkStore store = new();
        SymlinkCapabilityService capability = SaveCapability(settings, store, Body(settings.Settings.Symlinks));
        AppSettings checkedSettings = await capability.CheckAndUpdateAsync(CancellationToken.None);
        Assert.False(checkedSettings.Symlinks.Enabled);
        Assert.Equal(SymlinkCapabilityStatus.Available, Assert.Single(await store.GetCapabilityChecksAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task AutomaticCheckPreservesActivationButPausesProcessingWhenPrivilegesAreMissing()
    {
        StubSettingsService settings = new(new AppSettings { Symlinks = SymlinkSettingsRules.Normalize(new SymlinkSettings { MediaRoot = LibManagarr("library"), Enabled = true }) });
        InMemorySymlinkStore store = new();
        SymlinkCapabilityService capability = SaveCapability(settings, store, Body(settings.Settings.Symlinks), new UnauthorizedAccessException("Privilege denied."));
        AppSettings checkedSettings = await capability.CheckAndUpdateAsync(CancellationToken.None);
        Assert.True(checkedSettings.Symlinks.Enabled);
        Assert.Equal(0, settings.Saves);
        Assert.False(await capability.IsReadyAsync(checkedSettings.Symlinks, CancellationToken.None));
        Assert.Equal(SymlinkCapabilityStatus.PermissionDenied, Assert.Single(await store.GetCapabilityChecksAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task DisabledFilter_RefusesActionsUntilEnabled()
    {
        AppSettings snapshot = new();
        snapshot.Symlinks.Enabled = false;
        StubSettingsService settings = new(snapshot);
        using ServiceProvider provider = new ServiceCollection().AddSingleton<ISettingsService>(settings).BuildServiceProvider();
        Microsoft.AspNetCore.Http.DefaultHttpContext http = new() { RequestServices = provider };
        Microsoft.AspNetCore.Http.DefaultEndpointFilterInvocationContext context = new(http);
        bool called = false;
        Microsoft.AspNetCore.Http.EndpointFilterDelegate next = _ => { called = true; return ValueTask.FromResult<object?>("allowed"); };
        MethodInfo method = typeof(SymlinkEndpoints).GetMethod("RequireEnabledAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? denied = await (ValueTask<object?>)method.Invoke(null, [context, next])!;
        Assert.Equal(409, Assert.IsType<ProblemHttpResult>(denied).StatusCode);
        Assert.False(called);
        snapshot.Symlinks.Enabled = true;
        Assert.Equal("allowed", await (ValueTask<object?>)method.Invoke(null, [context, next])!);
        Assert.True(called);
    }

    [Fact]
    public async Task Reenabling_WakesMatchingAndPendingSetupWork()
    {
        AppSettings snapshot = new();
        string root = LibManagarr("library");
        snapshot.Symlinks = SymlinkSettingsRules.Normalize(new SymlinkSettings { MediaRoot = root, Enabled = false });
        StubSettingsService settings = new(snapshot);
        SymlinkSetupRequests setup = new();
        SymlinkMatchingRequests matching = new();
        InMemorySymlinkStore store = new();
        string body = JsonSerializer.Serialize(new { mediaRoot = root, enabled = true });
        await Invoke<Results<Ok<SymlinkSettingsDto>, ValidationProblem>>("SaveSymlinksAsync",
            JsonSerializer.Deserialize<JsonElement>(body), settings, store, setup, matching,
            SaveCapability(settings, store, body), CancellationToken.None);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(1));
        await setup.WaitForRequestAsync(deadline.Token);
        await matching.WaitForRequestAsync(deadline.Token);
    }

    [Fact]
    public async Task Save_TogglingDoesNotInvalidatePathsAndOldClientsPreserveDisabledState()
    {
        StubSettingsService settings = new(new AppSettings());
        InMemorySymlinkStore store = new();
        string root = LibManagarr("library");
        SymlinkSettingsDto original = await Save(settings, store, JsonSerializer.Serialize(new { mediaRoot = root }));
        Assert.True(original.Enabled);
        SymlinkSettingsDto disabled = await Save(settings, store, JsonSerializer.Serialize(new { mediaRoot = root, enabled = false }));
        Assert.False(disabled.Enabled);
        Assert.Equal(original.Revision, disabled.Revision);
        Assert.False((await Save(settings, store, JsonSerializer.Serialize(new { mediaRoot = root }))).Enabled);
        SymlinkSettingsDto enabled = await Save(settings, store, JsonSerializer.Serialize(new { mediaRoot = root, enabled = true }));
        Assert.True(enabled.Enabled);
        Assert.Equal(original.Revision, enabled.Revision);
    }

    [Fact]
    public async Task Save_AcceptsOnlyTheSharedMediaDirectoryAndRetainsItsRevisionOnNoOpSaves()
    {
        StubSettingsService settings = new(new AppSettings());
        InMemorySymlinkStore store = new();
        string root = LibManagarr("library");
        string body = JsonSerializer.Serialize(new { mediaRoot = root });

        SymlinkSettingsDto saved = await Save(settings, store, body);
        Assert.Equal(root, saved.MediaRoot);
        Assert.Empty(saved.PathMappings);
        Assert.Empty(saved.ReaderViews);
        Assert.Single(saved.CapabilityChecks);
        Assert.Equal(SymlinkCapabilityStatus.Available, saved.CapabilityChecks[0].Status);
        Assert.Equal(1, saved.Revision);
        Assert.Equal(saved.Revision, (await Save(settings, store, body)).Revision);
    }

    [Fact]
    public async Task Save_RejectsInvalidMediaDirectoriesWithoutReplacingTheConfiguration()
    {
        StubSettingsService settings = new(Settings(4));
        Results<Ok<SymlinkSettingsDto>, ValidationProblem> result =
            await SaveResult(settings, new InMemorySymlinkStore(), """{"mediaRoot":"relative/path"}""");
        Assert.Contains("symlinks.mediaRoot", Assert.IsType<ValidationProblem>(result.Result).ProblemDetails.Errors);
        Assert.Equal(0, settings.Saves);
        Assert.Equal(4, settings.Settings.Symlinks.Revision);
    }

    [Fact]
    public async Task Save_MigratesLegacyLinksInsideTheMediaTreeWithoutChangingTheirPaths()
    {
        StubSettingsService settings = new(Settings(4));
        InMemorySymlinkStore store = new();
        SymlinkLink link = new()
        {
            Id = "old-link", StorageAreaId = Area1,
            SourcePath = LibManagarr("movies/Film/movie.mkv"), LinkPath = LibManagarr("series/Show/Season 00/s00e01.mkv")
        };
        await store.InsertLinkAsync(link, CancellationToken.None);
        string root = Path.GetDirectoryName(LibManagarr("movies"))!;
        SymlinkSettingsDto saved = await Save(settings, store, JsonSerializer.Serialize(new { mediaRoot = root }));

        Assert.Equal(5, saved.Revision);
        Assert.NotNull(settings.Settings.Symlinks.FindStorageArea(link.StorageAreaId));
        Assert.Equal(LibManagarr("movies/Film/movie.mkv"), Assert.Single(await store.GetLinksAsync(CancellationToken.None)).SourcePath);

        Results<Ok<SymlinkSettingsDto>, ValidationProblem> refused = await SaveResult(settings, store,
            JsonSerializer.Serialize(new { mediaRoot = LibManagarr("different-library") }));
        Assert.Contains("mediaRoot", Assert.IsType<ValidationProblem>(refused.Result).ProblemDetails.Errors);
        Assert.Equal(1, settings.Saves);
    }

    [Fact]
    public async Task Get_ReportsEveryAreaAsNotTestedUntilAProbeRan()
    {
        StubSettingsService settings = new(Settings(revision: 3));

        SymlinkSettingsDto view = await Get(settings, new InMemorySymlinkStore());

        Assert.Equal(3, view.Revision);
        Assert.Equal(2, view.StorageAreas.Count);
        Assert.Equal(2, view.CapabilityChecks.Count);
        Assert.All(view.CapabilityChecks, check =>
        {
            Assert.Equal(SymlinkCapabilityStatus.NotTested, check.Status);
            Assert.True(check.Stale);
        });
        // The response identifies each state by the area it belongs to, so the page cannot show one
        // area's answer next to another area's paths.
        Assert.Equal(view.StorageAreas.Select(area => area.Id), view.CapabilityChecks.Select(check => check.StorageAreaId));
    }

    [Fact]
    public async Task Get_ReportsAProbeOfAnOlderRevisionAsStale()
    {
        StubSettingsService settings = new(Settings(revision: 4));
        InMemorySymlinkStore store = new();
        await store.SaveCapabilityCheckAsync(new SymlinkCapabilityCheck
        {
            StorageAreaId = Area1,
            Status = SymlinkCapabilityStatus.Available,
            Detail = "File symlinks work.",
            CheckedAt = new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero),
            ConfigRevision = 3
        }, CancellationToken.None);
        await store.SaveCapabilityCheckAsync(new SymlinkCapabilityCheck
        {
            StorageAreaId = Area2,
            Status = SymlinkCapabilityStatus.PermissionDenied,
            Detail = "Creating a link was denied.",
            CheckedAt = new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero),
            ConfigRevision = 4
        }, CancellationToken.None);

        SymlinkSettingsDto view = await Get(settings, store);

        SymlinkCapabilityStateDto stale = view.CapabilityChecks.Single(check => check.StorageAreaId == Area1);
        Assert.Equal(SymlinkCapabilityStatus.Available, stale.Status);
        Assert.True(stale.Stale);
        Assert.Equal(3, stale.ConfigRevision);

        SymlinkCapabilityStateDto current = view.CapabilityChecks.Single(check => check.StorageAreaId == Area2);
        Assert.False(current.Stale);
        Assert.Equal(SymlinkCapabilityStatus.PermissionDenied, current.Status);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero), current.CheckedAt);
    }

    [Fact]
    public async Task Save_RaisesTheRevisionOnlyWhenAPathChanged()
    {
        StubSettingsService settings = new(new AppSettings { RadarrInstances = [Radarr] });
        InMemorySymlinkStore store = new();

        SymlinkSettingsDto first = await Save(settings, store, Body(Valid(revision: 0)));
        Assert.Equal(1, first.Revision);
        Assert.Single(first.StorageAreas);
        Assert.Equal(1, settings.Settings.Symlinks.Revision);

        SymlinkSettings unchanged = Valid(revision: 1);
        unchanged.StorageAreas[0].Name = "Renamed shelf";
        SymlinkSettingsDto renamed = await Save(settings, store, Body(unchanged));
        // A name is not a path: the proven probe result still describes the same shelves.
        Assert.Equal(1, renamed.Revision);

        SymlinkSettings moved = Valid(revision: 1);
        moved.StorageAreas[0].MoviesRoot = LibManagarr("movies-two");
        moved.PathMappings[0].LibManagarrRoot = LibManagarr("movies-two");
        SymlinkSettingsDto movedView = await Save(settings, store, Body(moved));
        Assert.Equal(2, movedView.Revision);
        Assert.Equal(LibManagarr("movies-two"), movedView.StorageAreas[0].MoviesRoot);
        Assert.Equal(3, settings.Saves);
    }

    [Fact]
    public async Task Save_IgnoresARevisionTheClientSentAndKeepsTheServerOwnsIt()
    {
        StubSettingsService settings = new(new AppSettings { RadarrInstances = [Radarr] });

        SymlinkSettingsDto view = await Save(settings, new InMemorySymlinkStore(), Body(Valid(revision: 99)));

        // The revision ages out probe results, so a client that never raises it could keep a stale
        // "symlinks work" answer alive forever.
        Assert.Equal(1, view.Revision);
        Assert.Equal(1, settings.Settings.Symlinks.Revision);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"text\"")]
    public async Task Save_RejectsABodyThatIsNotTheSection(string json)
    {
        StubSettingsService settings = new(new AppSettings { RadarrInstances = [Radarr] });

        Assert.IsType<ValidationProblem>((await SaveResult(settings, new InMemorySymlinkStore(), json)).Result);
        Assert.Equal(0, settings.Saves);
    }

    [Fact]
    public async Task Save_RejectsAConfigurationThatWouldPickTheWrongShelf()
    {
        StubSettingsService settings = new(new AppSettings { RadarrInstances = [Radarr] });
        SymlinkSettings broken = Valid(revision: 0);
        broken.PathMappings[0].InstanceId = "not-configured";

        Results<Ok<SymlinkSettingsDto>, ValidationProblem> result = await SaveResult(settings, new InMemorySymlinkStore(), Body(broken));
        ValidationProblem problem = Assert.IsType<ValidationProblem>(result.Result);
        Assert.Contains("symlinks.pathMappings[0].instanceId", problem.ProblemDetails.Errors.Keys);
        Assert.Equal(0, settings.Saves);
    }

    [Fact]
    public async Task Save_AcceptsTheEnumNamesTheSettingsPageSends()
    {
        StubSettingsService settings = new(new AppSettings { RadarrInstances = [Radarr] });
        // Assembled as a document, because the roots carry the runtime path separators while the page names
        // a kind and a verification state in text — the two forms the endpoint has to read together.
        JsonObject body = new()
        {
            ["revision"] = 0,
            ["storageAreas"] = new JsonArray(new JsonObject
            {
                ["name"] = "Main",
                ["moviesRoot"] = LibManagarr("movies"),
                ["seriesRoot"] = LibManagarr("series")
            }),
            ["pathMappings"] = new JsonArray(new JsonObject
            {
                ["instanceId"] = Radarr.Id,
                ["kind"] = nameof(ArrApplicationKind.Radarr),
                ["instanceRoot"] = @"\\nas\radarr",
                ["libManagarrRoot"] = LibManagarr("movies")
            }),
            ["readerViews"] = new JsonArray(new JsonObject
            {
                ["kind"] = nameof(PathReaderKind.Sonarr),
                ["root"] = LibManagarr("series"),
                ["verification"] = nameof(PathReaderVerification.Unverified)
            })
        };

        await Save(settings, new InMemorySymlinkStore(), body.ToJsonString());

        // A body whose names were read as numbers would put the mapping on whichever application happens to
        // carry that number, so the stored kind is the only assertion that says the name arrived.
        Assert.Equal(ArrApplicationKind.Radarr, settings.Settings.Symlinks.PathMappings[0].Kind);
        Assert.Equal(PathReaderKind.Sonarr, settings.Settings.Symlinks.ReaderViews[0].Kind);
        Assert.Equal(PathReaderVerification.Unverified, settings.Settings.Symlinks.ReaderViews[0].Verification);
        Assert.Equal(1, settings.Saves);
    }

    [Fact]
    public async Task CapabilityTest_UnknownAreaIsNotFoundAndNothingIsProbed()
    {
        StubSettingsService settings = new(Settings(revision: 1));
        InMemorySymlinkStore store = new();

        Results<Ok<SymlinkCapabilityStateDto>, NotFound> result = await Invoke<Results<Ok<SymlinkCapabilityStateDto>, NotFound>>(
            "TestCapabilityAsync", "area-9", settings, Capability(settings, store), CancellationToken.None);

        Assert.IsType<NotFound>(result.Result);
        Assert.Empty(await store.GetCapabilityChecksAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CapabilityTest_StoresAndReturnsWhatTheProbeFound()
    {
        StubSettingsService settings = new(Settings(revision: 6));
        InMemorySymlinkStore store = new();

        SymlinkCapabilityStateDto state = await Invoke<Results<Ok<SymlinkCapabilityStateDto>, NotFound>>(
            "TestCapabilityAsync", Area1, settings, Capability(settings, store), CancellationToken.None) is
            { Result: Ok<SymlinkCapabilityStateDto> ok } ? ok.Value! : throw new InvalidOperationException("Expected a probe result.");

        Assert.Equal(Area1, state.StorageAreaId);
        Assert.Equal(SymlinkCapabilityStatus.StorageUnavailable, state.Status);
        Assert.Contains(LibManagarr("series"), state.Detail);
        Assert.Equal(6, state.ConfigRevision);
        // The answer belongs to the configuration that is still in effect, so it is not stale on arrival.
        Assert.False(state.Stale);
        Assert.Equal(state.Status, (await store.GetCapabilityCheckAsync(Area1, CancellationToken.None))!.Status);
    }

    private static async Task<SymlinkSettingsDto> Get(ISettingsService settings, ISymlinkStore store) =>
        (await Invoke<Ok<SymlinkSettingsDto>>("GetSymlinksAsync", settings, store, CancellationToken.None)).Value!;

    private static Task<SymlinkSettingsDto> Save(StubSettingsService settings, ISymlinkStore store, string json) =>
        UnwrapOk(SaveResult(settings, store, json));

    private static async Task<SymlinkSettingsDto> UnwrapOk(Task<Results<Ok<SymlinkSettingsDto>, ValidationProblem>> result) =>
        Assert.IsType<Ok<SymlinkSettingsDto>>((await result).Result).Value!;

    private static Task<Results<Ok<SymlinkSettingsDto>, ValidationProblem>> SaveResult(
        StubSettingsService settings, ISymlinkStore store, string json, SymlinkCapabilityService? capability = null) =>
        Invoke<Results<Ok<SymlinkSettingsDto>, ValidationProblem>>("SaveSymlinksAsync", JsonSerializer.Deserialize<JsonElement>(json),
            settings, store, new SymlinkSetupRequests(), new SymlinkMatchingRequests(), capability ?? SaveCapability(settings, store, json), CancellationToken.None);

    private static SymlinkCapabilityService SaveCapability(StubSettingsService settings, ISymlinkStore store, string json, Exception? failure = null)
    {
        SymlinkSettings? candidate;
        try { candidate = JsonSerializer.Deserialize<SymlinkSettings>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } }); }
        catch (JsonException) { return new(store, settings, new MissingTreeFileSystem(), TimeProvider.System, NullLogger<SymlinkCapabilityService>.Instance); }
        SymlinkSettings normalized = SymlinkSettingsRules.Normalize(candidate);
        List<string> roots = normalized.StorageAreas.SelectMany(area => new[] { area.MoviesRoot, area.SeriesRoot }).Distinct().ToList();
        string movies = normalized.MediaRoot is string media ? Path.Combine(media, "movies") : roots.FirstOrDefault() ?? LibManagarr("movies");
        string series = normalized.MediaRoot is string mediaRoot ? Path.Combine(mediaRoot, "series") : roots.LastOrDefault() ?? LibManagarr("series");
        roots.AddRange([movies, series]);
        SymlinkCapabilityServiceTests.FakeSymlinkFileSystem files = new(roots) { CreateSymlinkThrows = failure };
        if (normalized.MediaRoot is not null)
        {
            if (settings.Settings.RadarrInstances.Count == 0) settings.Settings.RadarrInstances.Add(new() { Id = "radarr-ready", Name = "Movies", BaseUrl = "http://radarr", ApiKey = "key" });
            if (settings.Settings.SonarrInstances.Count == 0) settings.Settings.SonarrInstances.Add(new() { Id = "sonarr-ready", Name = "Series", BaseUrl = "http://sonarr", ApiKey = "key" });
        }
        StubArrHandler radarr = new(new() { ["GET /api/v3/rootfolder"] = [JsonSerializer.Serialize(new[] { new { path = movies } })] });
        StubArrHandler sonarr = new(new() { ["GET /api/v3/rootfolder"] = [JsonSerializer.Serialize(new[] { new { path = series } })] });
        SharedMediaPathService discovery = new(new RadarrClient(new HttpClient(radarr)), new SonarrClient(new HttpClient(sonarr)));
        return new(store, settings, files, new FakeTimeProvider(new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero)),
            NullLogger<SymlinkCapabilityService>.Instance, discovery);
    }

    private static SymlinkCapabilityService Capability(StubSettingsService settings, InMemorySymlinkStore store) =>
        new(store, settings, new MissingTreeFileSystem(), new FakeTimeProvider(new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero)),
            NullLogger<SymlinkCapabilityService>.Instance);

    private static Task<T> Invoke<T>(string method, params object?[] arguments) =>
        (Task<T>)typeof(SymlinkEndpoints).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments)!;

    // Written exactly as the settings page sends it: the kind of a mapping or a reader view leaves the
    // browser as text, so a body that named them by number would describe a client nobody uses.
    private static string Body(SymlinkSettings settings) =>
        JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        });

    [Fact]
    public async Task Routes_AreMountedInTheAuthorizedSettingsGroupAndTheirServicesResolve()
    {
        // A route that was never mapped and a route that is open to everyone both answer 404 or 401 for
        // the wrong reason, so the session check is what proves the wiring here. An unmatched path under
        // /api/settings would answer 404, not 401.
        using AuthTestAppFactory factory = new();
        HttpClient cookieless = factory.CreateCookielessClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await cookieless.GetAsync("/api/settings/symlinks")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cookieless.GetAsync("/api/settings/symlinks/directories")).StatusCode);

        using IServiceScope scope = factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SymlinkCapabilityService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<MediaPathMappingService>());
        Assert.NotNull(factory.Services.GetRequiredService<ISymlinkStore>());
        Assert.NotNull(factory.Services.GetRequiredService<ISymlinkFileSystem>());
    }

    private static SymlinkSettings Valid(int revision) => new()
    {
        Revision = revision,
        StorageAreas =
        [
            new MediaStorageArea
            {
                Id = Area1,
                Name = "Main",
                MoviesRoot = LibManagarr("movies"),
                SeriesRoot = LibManagarr("series")
            }
        ],
        PathMappings =
        [
            new ArrPathMapping
            {
                Id = "radarr-1",
                InstanceId = Radarr.Id,
                Kind = ArrApplicationKind.Radarr,
                InstanceRoot = @"\\nas\radarr",
                LibManagarrRoot = LibManagarr("movies")
            }
        ]
    };

    private static AppSettings Settings(int revision)
    {
        AppSettings settings = new()
        {
            RadarrInstances = [Radarr]
        };
        SymlinkSettings symlinks = Valid(revision);
        symlinks.StorageAreas.Add(new MediaStorageArea
        {
            Id = Area2,
            Name = "Second",
            MoviesRoot = LibManagarr("movies-two"),
            SeriesRoot = LibManagarr("series-two")
        });
        settings.Symlinks = symlinks;
        return settings;
    }

    private static string LibManagarr(string relative) =>
        ArrPathRules.RuntimeSyntax == ArrPathSyntax.Windows
            ? @"C:\media\" + relative.Replace('/', '\\')
            : "/media/" + relative;

    /// <summary>A file system in which nothing is mounted, which the probe has to report as such.</summary>
    private sealed class MissingTreeFileSystem : ISymlinkFileSystem
    {
        public void CreateFileSymlink(string linkPath, string targetPath) => throw new IOException("Nothing is mounted here.");
        public bool EntryExists(string path) => false;
        public bool IsSymlink(string path) => false;
        public string? ReadLinkTarget(string path) => null;
        public byte[] ReadAllBytes(string path) => throw new IOException("Nothing is mounted here.");
        public void OpenForRead(string path) => throw new IOException("Nothing is mounted here.");
        public void WriteAllBytes(string path, byte[] content) => throw new IOException("Nothing is mounted here.");
        public void DeleteEntry(string path) => throw new IOException("Nothing is mounted here.");
        public void MoveEntry(string sourcePath, string destinationPath) => throw new IOException("Nothing is mounted here.");
        public void CreateDirectory(string path) => throw new IOException("Nothing is mounted here.");
        public IReadOnlyList<string> ListEntries(string directoryPath) => throw new DirectoryNotFoundException($"Nothing is mounted at '{directoryPath}'.");
    }

    private sealed class StubSettingsService(AppSettings settings) : ISettingsService
    {
        public AppSettings Settings { get; private set; } = settings;
        public int Saves { get; private set; }
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken)
        {
            Settings = value;
            Saves += 1;
            return Task.FromResult(Settings);
        }
    }
}
