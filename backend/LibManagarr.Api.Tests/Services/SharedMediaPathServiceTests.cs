using System.Text.Json;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibManagarr.Api.Tests.Services;

public sealed class SharedMediaPathServiceTests
{
    private static string Root => OperatingSystem.IsWindows() ? @"C:\media" : "/media";
    private static string Below(string path) => Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public async Task PathsAreUnchangedAndConstrainedToTheCorrectInstanceRootFolders()
    {
        AppSettings settings = Settings();
        StubArrHandler radarr = Handler(Below("movies"));
        StubArrHandler sonarr = Handler(Below("series"));
        SharedMediaPathService discovery = Discovery(radarr, sonarr);
        MediaPathMappingService paths = new(new FixedSettings(settings), new FakeLinkFileSystem(), discovery);

        string movie = Below("movies/Film/film.mkv");
        PathMappingResult source = await paths.TranslateToLibManagarrPathAsync("radarr", ArrApplicationKind.Radarr, movie, CancellationToken.None);
        Assert.True(source.Success, source.Failure);
        Assert.Equal(movie, source.Path);
        Assert.Equal(movie, (await paths.TranslateToInstancePathAsync("radarr", ArrApplicationKind.Radarr, movie, CancellationToken.None)).Path);

        Assert.False((await paths.TranslateToLibManagarrPathAsync("sonarr", ArrApplicationKind.Sonarr, movie, CancellationToken.None)).Success);
        Assert.False((await paths.TranslateToLibManagarrPathAsync("radarr", ArrApplicationKind.Radarr, Below("downloads/file.mkv"), CancellationToken.None)).Success);
        Assert.False((await paths.TranslateToLibManagarrPathAsync("unknown", ArrApplicationKind.Radarr, movie, CancellationToken.None)).Success);
        Assert.Single(radarr.Requests); // One root-folder read per instance and operation scope.
        Assert.Equal("/api/v3/rootfolder", radarr.Requests[0].Path);
    }

    [Fact]
    public async Task ParallelHealthReadsShareOneRootFolderRequestPerInstance()
    {
        StubArrHandler radarr = Handler(Below("movies"));
        SharedMediaPathService discovery = Discovery(radarr, Handler(Below("series")));
        PathMappingResult[] results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            Task.Run(() => discovery.ValidatePathAsync(Settings(), ArrApplicationKind.Radarr,
                "radarr", Below("movies/movie.mkv"), CancellationToken.None))));
        Assert.All(results, result => Assert.True(result.Success, result.Failure));
        Assert.Single(radarr.Requests);
    }

    [Fact]
    public async Task MultipleRootFoldersAreDiscoveredWithoutAssumingFolderNames()
    {
        SharedMediaPathService discovery = Discovery(Handler(Below("films"), Below("anime/movies")), Handler(Below("shows")));
        PathMappingResult result = await discovery.ValidatePathAsync(Settings(), ArrApplicationKind.Radarr,
            "radarr", Below("anime/movies/Film/movie.mkv"), CancellationToken.None);
        Assert.True(result.Success, result.Failure);
    }

    [Fact]
    public async Task DifferentMountPrefixesAreRefusedRatherThanGuessed()
    {
        string otherRoot = OperatingSystem.IsWindows() ? @"C:\other\movies" : "/other/movies";
        SharedMediaPathService discovery = Discovery(Handler(otherRoot), Handler(Below("series")));
        PathMappingResult result = await discovery.ValidatePathAsync(Settings(), ArrApplicationKind.Radarr,
            "radarr", Below("movies/Film/movie.mkv"), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("Mount the same media tree", result.Failure);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task MissingOrMalformedRootFoldersCannotAuthorizePaths(string response)
    {
        StubArrHandler radarr = new(new() { ["GET /api/v3/rootfolder"] = [response] });
        SharedMediaPathService discovery = Discovery(radarr, Handler(Below("series")));
        Assert.False((await discovery.ValidatePathAsync(Settings(), ArrApplicationKind.Radarr,
            "radarr", Below("movies/Film/movie.mkv"), CancellationToken.None)).Success);
    }

    [Fact]
    public async Task MediaDirectoryBoundaryIsCheckedBeforeReadingAnInstance()
    {
        StubArrHandler radarr = Handler(Below("movies"));
        SharedMediaPathService discovery = Discovery(radarr, Handler(Below("series")));
        PathMappingResult result = await discovery.ValidatePathAsync(Settings(), ArrApplicationKind.Radarr,
            "radarr", Root + "-other/file.mkv", CancellationToken.None);
        Assert.False(result.Success);
        Assert.Empty(radarr.Requests);
    }

    [Fact]
    public async Task UnreachableInstancesFailClosedAndCallerCancellationIsPreserved()
    {
        StubArrHandler radarr = new(new()) { Throws = new HttpRequestException("offline") };
        SharedMediaPathService discovery = Discovery(radarr, Handler(Below("series")));
        PathMappingResult result = await discovery.ValidatePathAsync(Settings(), ArrApplicationKind.Radarr,
            "radarr", Below("movies/movie.mkv"), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("offline", result.Failure);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovery.ValidatePathAsync(Settings(), ArrApplicationKind.Radarr,
            "radarr", Below("movies/movie.mkv"), cancellation.Token));
    }

    [Fact]
    public async Task CapabilityTestUsesDiscoveredSeriesFoldersAndNeverWritesToTheMediaRoot()
    {
        AppSettings settings = Settings();
        ProbeFiles files = new([Root, Below("movies"), Below("series"), Below("anime")]);
        SharedMediaPathService discovery = Discovery(Handler(Below("movies")), Handler(Below("series"), Below("anime")));
        InMemorySymlinkStore store = new();
        SymlinkCapabilityService capability = new(store, new FixedSettings(settings), files, TimeProvider.System,
            NullLogger<SymlinkCapabilityService>.Instance, discovery);

        SymlinkCapabilityCheck check = await capability.ProbeAsync(SymlinkSettings.SharedStorageAreaId, CancellationToken.None);

        Assert.Equal(SymlinkCapabilityStatus.Available, check.Status);
        Assert.Equal(3, check.ConfigRevision);
        Assert.Equal(2, files.ProbedDirectories.Count);
        Assert.Contains(files.ProbedDirectories, path => Path.GetDirectoryName(path) == Below("series"));
        Assert.Contains(files.ProbedDirectories, path => Path.GetDirectoryName(path) == Below("anime"));
        Assert.DoesNotContain(files.ProbedDirectories, path => Path.GetDirectoryName(path) == Root);
        Assert.Empty(files.Bytes);
        Assert.Empty(files.Links);
        Assert.Same(check, await capability.GetStatusAsync("legacy-area-id", CancellationToken.None));
    }

    [Fact]
    public async Task CapabilityRefusesAnInconsistentMountBeforeWritingAnyProbeFiles()
    {
        ProbeFiles files = new([]);
        SymlinkCapabilityService capability = new(new InMemorySymlinkStore(), new FixedSettings(Settings()), files,
            TimeProvider.System, NullLogger<SymlinkCapabilityService>.Instance,
            Discovery(Handler(Root + "-wrong/movies"), Handler(Below("series"))));
        SymlinkCapabilityCheck check = await capability.ProbeAsync(SymlinkSettings.SharedStorageAreaId, CancellationToken.None);
        Assert.Equal(SymlinkCapabilityStatus.StorageUnavailable, check.Status);
        Assert.Empty(files.ProbedDirectories);
    }

    [Fact]
    public void SharedConfigurationHasOneInternalBoundaryAndNoManualMappings()
    {
        SymlinkSettings normalized = SymlinkSettingsRules.Normalize(new SymlinkSettings { MediaRoot = Root + Path.DirectorySeparatorChar });
        Assert.Equal(Root, normalized.MediaRoot);
        Assert.Single(normalized.StorageAreas);
        Assert.Empty(normalized.PathMappings);
        Assert.Empty(normalized.ReaderViews);
        Assert.Empty(SymlinkSettingsRules.Validate(normalized, [], []));
        Assert.Equal(Root, normalized.FindStorageArea("legacy-area-id")?.SeriesRoot);
        Assert.False(SymlinkSettingsRules.RequiresNewRevision(normalized, new SymlinkSettings { MediaRoot = Root }));
        Assert.True(SymlinkSettingsRules.RequiresNewRevision(normalized, new SymlinkSettings { MediaRoot = Root + "-other" }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/media")]
    public void MediaDirectoryMustBeAnAbsoluteDedicatedDirectory(string root)
    {
        Assert.Contains("symlinks.mediaRoot", SymlinkSettingsRules.Validate(new SymlinkSettings { MediaRoot = root }, [], []));
        Assert.Contains("symlinks.mediaRoot", SymlinkSettingsRules.Validate(new SymlinkSettings { MediaRoot = Path.GetPathRoot(Root) }, [], []));
    }

    private static AppSettings Settings() => new()
    {
        Symlinks = SymlinkSettingsRules.Normalize(new SymlinkSettings { MediaRoot = Root, Revision = 3 }),
        RadarrInstances = [new() { Id = "radarr", Name = "Movies", BaseUrl = "http://radarr", ApiKey = "key" }],
        SonarrInstances = [new() { Id = "sonarr", Name = "Series", BaseUrl = "http://sonarr", ApiKey = "key" }]
    };

    private static StubArrHandler Handler(params string[] folders) => new(new()
    {
        ["GET /api/v3/rootfolder"] = [JsonSerializer.Serialize(folders.Select(path => new { path }))]
    });

    private static SharedMediaPathService Discovery(StubArrHandler radarr, StubArrHandler sonarr) =>
        new(new RadarrClient(new HttpClient(radarr)), new SonarrClient(new HttpClient(sonarr)));

    private sealed class ProbeFiles(IEnumerable<string> roots) : ISymlinkFileSystem
    {
        private readonly HashSet<string> directories = [.. roots];
        public Dictionary<string, byte[]> Bytes { get; } = [];
        public Dictionary<string, string> Links { get; } = [];
        public List<string> ProbedDirectories { get; } = [];
        public bool EntryExists(string path) => directories.Contains(path) || Bytes.ContainsKey(path) || Links.ContainsKey(path);
        public bool IsSymlink(string path) => Links.ContainsKey(path);
        public string? ReadLinkTarget(string path) => Links.GetValueOrDefault(path);
        public void CreateFileSymlink(string path, string target) => Links.Add(path, target);
        public byte[] ReadAllBytes(string path) => Links.TryGetValue(path, out string? target)
            ? Bytes[Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, target))] : Bytes[path];
        public void OpenForRead(string path) => _ = ReadAllBytes(path);
        public void WriteAllBytes(string path, byte[] content) => Bytes[path] = content;
        public void CreateDirectory(string path) { directories.Add(path); ProbedDirectories.Add(path); }
        public void DeleteEntry(string path) { directories.Remove(path); Bytes.Remove(path); Links.Remove(path); }
        public void MoveEntry(string source, string destination) { Links.Add(destination, Links[source]); Links.Remove(source); }
        public IReadOnlyList<string> ListEntries(string path) => directories.Contains(path)
            ? Bytes.Keys.Concat(Links.Keys).Where(entry => Path.GetDirectoryName(entry) == path).ToArray()
            : throw new DirectoryNotFoundException(path);
    }
}
