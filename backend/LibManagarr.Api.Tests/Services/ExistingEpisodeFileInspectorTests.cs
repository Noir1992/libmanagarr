using System.Text.Json;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class ExistingEpisodeFileInspectorTests
{
    private static string Root => OperatingSystem.IsWindows() ? @"C:\inspection-media" : "/inspection-media";
    private static string Local(string path) => Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
    private static string EpisodePath => Local("series/Show/Specials/s00e01.mkv");
    private static string SourcePath => Local("movies/Film/movie.mkv");
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorrectRelativeAndAbsoluteSymlinksAreVerifiedWithoutAnyWrite(bool absolute)
    {
        FakeLinkFileSystem files = LinkedFiles(absolute);
        StubArrHandler handler = Handler();
        ExistingEpisodeFileCheck result = await Check(files, handler);
        Assert.Equal(ExistingEpisodeFileStatus.Matches, result.Status);
        Assert.Equal(SourcePath, result.ExpectedTarget);
        Assert.Equal(SourcePath, result.ActualTarget);
        Assert.Equal(EpisodePath, result.EpisodePath);
        Assert.Equal(Now, result.CheckedAt);
        Assert.Contains("not adopted or modified", result.Detail);
        Assert.All(handler.Requests, request => Assert.Equal("GET", request.Method));
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(files.CreatedFolders);
        Assert.Empty(files.Removed);
    }

    [Fact]
    public async Task SharedMountConfigurationUsesDiscoveredRootsWithoutManualMappings()
    {
        AppSettings settings = Settings();
        settings.Symlinks = SymlinkSettingsRules.Normalize(new SymlinkSettings { MediaRoot = Root });
        StubArrHandler handler = Handler();
        handler.Routes["GET /api/v3/rootfolder"] = [
            JsonSerializer.Serialize(new[] { new { path = Local("series") } }),
            JsonSerializer.Serialize(new[] { new { path = Local("movies") } })
        ];
        using HttpClient http = new(handler);
        SonarrClient sonarr = new(http);
        RadarrClient radarr = new(http);
        FakeLinkFileSystem files = LinkedFiles();
        SharedMediaPathService discovery = new(radarr, sonarr);
        ExistingEpisodeFileInspector inspector = new(sonarr, radarr,
            new MediaPathMappingService(new FixedSettings(settings), files, discovery), files, new FakeTimeProvider(Now));
        ExistingEpisodeFileCheck result = (await inspector.CheckAsync(settings, Suggestion(), CancellationToken.None))!;
        Assert.Equal(ExistingEpisodeFileStatus.Matches, result.Status);
        Assert.Empty(settings.Symlinks.PathMappings);
        Assert.Equal(2, handler.Requests.Count(request => request.Path == "/api/v3/rootfolder"));
    }

    [Fact]
    public async Task ALinkChainToTheExpectedMovieIsAccepted()
    {
        FakeLinkFileSystem files = LinkedFiles();
        string intermediate = Local("movies/shortcut.mkv");
        files.Entries[EpisodePath] = Path.GetRelativePath(Path.GetDirectoryName(EpisodePath)!, intermediate);
        files.Entries[intermediate] = Path.GetRelativePath(Path.GetDirectoryName(intermediate)!, SourcePath);
        Assert.Equal(ExistingEpisodeFileStatus.Matches, (await Check(files)).Status);
    }

    [Fact]
    public async Task AnotherTargetIsReportedWithBothActualAndExpectedPaths()
    {
        FakeLinkFileSystem files = LinkedFiles();
        string wrong = Local("movies/Other/movie.mkv");
        files.Entries[wrong] = null;
        files.Entries[EpisodePath] = wrong;
        ExistingEpisodeFileCheck result = await Check(files);
        Assert.Equal(ExistingEpisodeFileStatus.TargetMismatch, result.Status);
        Assert.Equal(wrong, result.ActualTarget);
        Assert.Contains(SourcePath, result.Detail);
        Assert.Contains(wrong, result.Detail);
    }

    [Fact]
    public async Task AnOutsideTargetIsReportedWithoutOpeningIt()
    {
        FakeLinkFileSystem files = LinkedFiles();
        string outside = Root + "-outside/private.mkv";
        files.Entries[EpisodePath] = outside;
        files.Entries[outside] = null;
        files.RefusedReads.Add(outside);
        ExistingEpisodeFileCheck result = await Check(files);
        Assert.Equal(ExistingEpisodeFileStatus.TargetMismatch, result.Status);
        Assert.Contains("not opened", result.Detail);
        Assert.Empty(files.Removed);
    }

    [Fact]
    public async Task ADeletedTargetIsADanglingLinkRatherThanAMissingLinkEntry()
    {
        FakeLinkFileSystem files = LinkedFiles();
        files.Entries.Remove(SourcePath);
        ExistingEpisodeFileCheck result = await Check(files);
        Assert.Equal(ExistingEpisodeFileStatus.BrokenSymlink, result.Status);
        Assert.Equal(SourcePath, result.ActualTarget);
    }

    [Fact]
    public async Task ALinkCycleIsReportedWithoutFollowingItForever()
    {
        FakeLinkFileSystem files = LinkedFiles();
        string second = Local("series/loop.mkv");
        files.Entries[EpisodePath] = second;
        files.Entries[second] = EpisodePath;
        Assert.Equal(ExistingEpisodeFileStatus.BrokenSymlink, (await Check(files)).Status);
    }

    [Theory]
    [InlineData(true, ExistingEpisodeFileStatus.RegularFile)]
    [InlineData(false, ExistingEpisodeFileStatus.MissingFile)]
    public async Task RegularFilesAndMissingEntriesAreDistinguished(bool exists, ExistingEpisodeFileStatus status)
    {
        FakeLinkFileSystem files = LinkedFiles();
        if (exists) files.Entries[EpisodePath] = null;
        else files.Entries.Remove(EpisodePath);
        Assert.Equal(status, (await Check(files)).Status);
        Assert.Empty(files.Removed);
    }

    [Fact]
    public async Task PermissionErrorsDoNotClaimThatTheSymlinkIsWrong()
    {
        FakeLinkFileSystem files = LinkedFiles();
        files.RefusedReads.Add(EpisodePath);
        ExistingEpisodeFileCheck result = await Check(files);
        Assert.Equal(ExistingEpisodeFileStatus.Unavailable, result.Status);
        Assert.Contains("denied", result.Detail);
    }

    [Fact]
    public async Task ALinkedParentDirectoryIsRefused()
    {
        FakeLinkFileSystem files = LinkedFiles();
        files.Entries[Path.GetDirectoryName(EpisodePath)!] = Local("movies");
        ExistingEpisodeFileCheck result = await Check(files);
        Assert.Equal(ExistingEpisodeFileStatus.Unavailable, result.Status);
        Assert.Contains("parent directory", result.Detail);
    }

    [Fact]
    public async Task AnUpgradedAwaySourceDoesNotProduceAFalseSuccessfulCheck()
    {
        StubArrHandler handler = Handler();
        handler.Routes["GET /api/v3/moviefile"] = [JsonSerializer.Serialize(new[] { new { id = 901, movieId = 70, path = SourcePath } })];
        ExistingEpisodeFileCheck result = await Check(LinkedFiles(), handler);
        Assert.Equal(ExistingEpisodeFileStatus.Unavailable, result.Status);
        Assert.Contains("no longer reports the proposed movie", result.Detail);
    }

    [Fact]
    public async Task LiveSonarrAssignmentWinsOverAnOldHasFileSnapshot()
    {
        StubArrHandler handler = Handler();
        handler.Routes["GET /api/v3/episode/5"] = ["""{"id":5,"seriesId":12,"hasFile":false,"episodeFileId":0}"""];
        ExistingEpisodeFileCheck result = await Check(LinkedFiles(), handler);
        Assert.Equal(ExistingEpisodeFileStatus.NoFile, result.Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FilesystemChangesAreCheckedAgainEvenWhenMetadataIsUnchanged()
    {
        FakeLinkFileSystem files = LinkedFiles();
        StubArrHandler handler = Handler();
        AppSettings settings = Settings();
        ExistingEpisodeFileInspector inspector = Inspector(settings, files, handler);
        Assert.Equal(ExistingEpisodeFileStatus.Matches, (await inspector.CheckAsync(settings, Suggestion(), CancellationToken.None))!.Status);
        string different = Local("movies/Other/movie.mkv");
        files.Entries[different] = null;
        files.Entries[EpisodePath] = different;
        Assert.Equal(ExistingEpisodeFileStatus.TargetMismatch, (await inspector.CheckAsync(settings, Suggestion(), CancellationToken.None))!.Status);
        Assert.Equal(3, handler.Requests.Count); // Reuse the same Arr reads within one scope, never the filesystem result.
    }

    [Fact]
    public async Task UnreachableApisAreReportedAndCallerCancellationPropagates()
    {
        AppSettings settings = Settings();
        StubArrHandler handler = new(new()) { Throws = new HttpRequestException("Sonarr offline") };
        ExistingEpisodeFileInspector inspector = Inspector(settings, LinkedFiles(), handler);
        ExistingEpisodeFileCheck result = (await inspector.CheckAsync(settings, Suggestion(), CancellationToken.None))!;
        Assert.Equal(ExistingEpisodeFileStatus.Unavailable, result.Status);
        Assert.Contains("Sonarr offline", result.Detail);
        using CancellationTokenSource cancel = new();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspector.CheckAsync(settings, Suggestion(), cancel.Token));
    }

    [Fact]
    public async Task OffersWithoutAnExistingFileNeedNoLiveReads()
    {
        AppSettings settings = Settings();
        StubArrHandler handler = Handler();
        SymlinkSuggestion suggestion = Suggestion();
        suggestion.Reasons.Clear();
        Assert.Null(await Inspector(settings, LinkedFiles(), handler).CheckAsync(settings, suggestion, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    private static async Task<ExistingEpisodeFileCheck> Check(FakeLinkFileSystem files, StubArrHandler? handler = null)
    {
        AppSettings settings = Settings();
        return (await Inspector(settings, files, handler ?? Handler()).CheckAsync(settings, Suggestion(), CancellationToken.None))!;
    }

    private static FakeLinkFileSystem LinkedFiles(bool absolute = false)
    {
        FakeLinkFileSystem files = new();
        files.Entries[SourcePath] = null;
        files.Entries[EpisodePath] = absolute ? SourcePath : Path.GetRelativePath(Path.GetDirectoryName(EpisodePath)!, SourcePath);
        return files;
    }

    internal static ExistingEpisodeFileInspector Inspector(AppSettings settings, ISymlinkFileSystem files, HttpMessageHandler handler)
    {
        HttpClient http = new(handler);
        return new(new SonarrClient(http), new RadarrClient(http), new MediaPathMappingService(new FixedSettings(settings), files),
            files, new FakeTimeProvider(Now));
    }

    internal static AppSettings Settings(string? root = null)
    {
        root ??= Root;
        return new()
        {
            SonarrInstances = [new() { Id = "sonarr", Name = "Sonarr", BaseUrl = "http://sonarr", ApiKey = "key" }],
            RadarrInstances = [new() { Id = "radarr", Name = "Radarr", BaseUrl = "http://radarr", ApiKey = "key" }],
            Symlinks = new()
            {
                StorageAreas = [new() { Id = "area", Name = "Media", MoviesRoot = Path.Combine(root, "movies"), SeriesRoot = Path.Combine(root, "series") }],
                PathMappings = [
                    new() { Id = "radarr", InstanceId = "radarr", Kind = ArrApplicationKind.Radarr, InstanceRoot = Path.Combine(root, "movies"), LibManagarrRoot = Path.Combine(root, "movies") },
                    new() { Id = "sonarr", InstanceId = "sonarr", Kind = ArrApplicationKind.Sonarr, InstanceRoot = Path.Combine(root, "series"), LibManagarrRoot = Path.Combine(root, "series") }
                ]
            }
        };
    }

    internal static SymlinkSuggestion Suggestion() => new()
    {
        Id = "offer", Pair = new("radarr", 70, "sonarr", 12, 5), RadarrFileId = 900,
        Reasons = [new(SpecialMovieMatchingRules.EpisodeHasFileCode, "Sonarr has a file.")]
    };

    internal static StubArrHandler Handler(string? episodePath = null, string? sourcePath = null) => new(new()
    {
        ["GET /api/v3/episode/5"] = ["""{"id":5,"seriesId":12,"hasFile":true,"episodeFileId":500}"""],
        ["GET /api/v3/episodefile/500"] = [JsonSerializer.Serialize(new { id = 500, path = episodePath ?? EpisodePath })],
        ["GET /api/v3/moviefile"] = [JsonSerializer.Serialize(new[] { new { id = 900, movieId = 70, path = sourcePath ?? SourcePath } })]
    });
}
