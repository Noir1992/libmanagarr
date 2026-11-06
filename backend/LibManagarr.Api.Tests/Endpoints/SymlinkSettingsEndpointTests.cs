using System.Net;
using System.Reflection;
using System.Text.Json;
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
        StubSettingsService settings, ISymlinkStore store, string json) =>
        Invoke<Results<Ok<SymlinkSettingsDto>, ValidationProblem>>("SaveSymlinksAsync", JsonSerializer.Deserialize<JsonElement>(json), settings, store, CancellationToken.None);

    private static SymlinkCapabilityService Capability(StubSettingsService settings, InMemorySymlinkStore store) =>
        new(store, settings, new MissingTreeFileSystem(), new FakeTimeProvider(new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero)),
            NullLogger<SymlinkCapabilityService>.Instance);

    private static Task<T> Invoke<T>(string method, params object?[] arguments) =>
        (Task<T>)typeof(SymlinkEndpoints).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments)!;

    private static string Body(SymlinkSettings settings) =>
        JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public async Task Routes_AreMountedInTheAuthorizedSettingsGroupAndTheirServicesResolve()
    {
        // A route that was never mapped and a route that is open to everyone both answer 404 or 401 for
        // the wrong reason, so the session check is what proves the wiring here. An unmatched path under
        // /api/settings would answer 404, not 401.
        using AuthTestAppFactory factory = new();
        HttpClient cookieless = factory.CreateCookielessClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await cookieless.GetAsync("/api/settings/symlinks")).StatusCode);

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
