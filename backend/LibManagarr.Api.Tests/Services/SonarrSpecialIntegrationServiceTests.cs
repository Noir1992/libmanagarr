using System.Net;
using System.Text;
using System.Text.Json;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibManagarr.Api.Tests.Services;

/// <summary>
/// The Sonarr side of a confirmed link. The stub handler is the script of the conversation with the
/// instance: every route answers what the next read gets, so a test can say which answer arrived in which
/// pass. Waiting passes are counted rather than slept through, because the service never blocks on a
/// timer — it leaves the step open and lets the next pass read the instance again.
/// </summary>
public sealed class SonarrSpecialIntegrationServiceTests
{
    private const string SonarrInstanceId = "sonarr-1";
    private const string LinkId = "link-1";
    private const int SeriesId = 12;
    private const int EpisodeId = 5;
    private const int EpisodeFileId = 44;
    private const string OldName = "kabaneri.s00e05.mkv";
    private const string NewName = "Kabaneri of the Iron Fortress - 2016-06-23 - The Battle of Unato.mkv";
    private const string MovieFolder = "The Battle of Unato (2016)";
    private const string SeriesFolder = "Kabaneri (2016)";

    private static readonly DateTimeOffset Start = new(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);

    private readonly InMemorySymlinkStore store = new();
    private readonly FakeTimeProvider clock = new(Start);

    [Fact]
    public async Task TheSetupRunsThroughQueuedCommandsAndEndsFullySet()
    {
        StubArrHandler handler = new(HappyRoutes());
        FakeLinkFileSystem files = new() { Links = { [NewLinkPath()] = RelativeTextToSource() } };
        SonarrSpecialIntegrationService service = CreateService(handler, files);
        SymlinkLink link = await SeedAsync(SymlinkSetupStep.MonitoringDisabled);

        Assert.Equal(SymlinkSetupOutcome.Waiting, await service.RunSetupAsync(link, CancellationToken.None));
        SymlinkLink scanQueued = await StoredAsync();
        Assert.True(scanQueued.MonitoringOriginal);
        Assert.True(scanQueued.MonitoringDisabled);
        Assert.Equal(SymlinkSetupStep.RescanRequested, scanQueued.SetupStep);
        Assert.NotNull(scanQueued.SonarrScanCommandId);

        Assert.Equal(SymlinkSetupOutcome.Waiting, await service.RunSetupAsync(scanQueued, CancellationToken.None));
        SymlinkLink renameQueued = await StoredAsync();
        Assert.Equal(SymlinkSetupStep.Renamed, renameQueued.SetupStep);
        Assert.Equal(EpisodeFileId, renameQueued.SonarrFileId);
        // The scan is owed nothing any more, so its id is gone and only the rename is outstanding.
        Assert.Null(renameQueued.SonarrScanCommandId);
        Assert.NotNull(renameQueued.SonarrRenameCommandId);
        Assert.Equal("sonarr", renameQueued.ConfirmedMetadata?.Origin);
        Assert.Equal("WFFF", renameQueued.ConfirmedMetadata?.ReleaseGroup);

        Assert.Equal(SymlinkSetupOutcome.Completed, await service.RunSetupAsync(renameQueued, CancellationToken.None));
        SymlinkLink done = await StoredAsync();
        Assert.Equal(SymlinkSetupStep.Completed, done.SetupStep);
        Assert.Equal(SymlinkOperationStatus.Active, done.OperationStatus);
        Assert.Null(done.SonarrRenameCommandId);
        // The register follows the name Sonarr chose, because that name is what the episode points at now.
        Assert.Equal(NewLinkPath(), done.LinkPath);
        Assert.Equal(NewName, done.AppliedFilename);
        Assert.Equal(NewName, done.DesiredFilename);
        Assert.NotNull(done.NamingRevision);
        Assert.Empty(done.Findings);

        // Each command went out once, for this link alone: a rescan of the series without a file list and
        // a rename that names exactly the managed file.
        Assert.Contains("\"seriesId\":12", SinglePost("RescanSeries", handler));
        Assert.DoesNotContain("fileIds", SinglePost("RescanSeries", handler));
        Assert.Contains("\"fileIds\":[44]", SinglePost("RenameFiles", handler));
        Assert.Equal(2, handler.Posts.Count());
    }

    [Fact]
    public async Task AFailingScanKeepsTheScanStepOpenInsteadOfClaimingCompletion()
    {
        Dictionary<string, List<string>> routes = HappyRoutes();
        routes["GET /api/v3/command/91"] =
        [
            """{ "id": 91, "status": "failed", "message": "The folder could not be read." }"""
        ];
        SonarrSpecialIntegrationService service = CreateService(new StubArrHandler(routes), new FakeLinkFileSystem());
        SymlinkLink first = await SeedAsync(SymlinkSetupStep.MonitoringDisabled);
        await service.RunSetupAsync(first, CancellationToken.None);

        Assert.Equal(SymlinkSetupOutcome.Blocked, await service.RunSetupAsync(await StoredAsync(), CancellationToken.None));

        SymlinkLink stored = await StoredAsync();
        Assert.Equal(SymlinkSetupStep.RescanRequested, stored.SetupStep);
        Assert.Equal(SymlinkOperationStatus.Pending, stored.OperationStatus);
        Assert.Null(stored.SonarrFileId);
        Assert.Equal("scan-failed", Assert.Single(stored.Findings).Code);
    }

    [Fact]
    public async Task AScanThatNeverFinishesIsReportedInsteadOfWaitedOnForever()
    {
        Dictionary<string, List<string>> routes = HappyRoutes();
        routes["GET /api/v3/command/91"] =
        [
            $$"""{ "id": 91, "status": "queued", "queued": "{{Start.AddHours(-1).ToString("O")}}" }"""
        ];
        SonarrSpecialIntegrationService service = CreateService(new StubArrHandler(routes), new FakeLinkFileSystem());
        SymlinkLink first = await SeedAsync(SymlinkSetupStep.MonitoringDisabled);
        await service.RunSetupAsync(first, CancellationToken.None);

        Assert.Equal(SymlinkSetupOutcome.Blocked, await service.RunSetupAsync(await StoredAsync(), CancellationToken.None));

        SymlinkLink stored = await StoredAsync();
        Assert.Equal(SymlinkSetupStep.RescanRequested, stored.SetupStep);
        Assert.Equal("scan-wait-expired", Assert.Single(stored.Findings).Code);
    }

    [Fact]
    public async Task AFileOnAnotherPathIsAConflictAndNothingOfItIsAdopted()
    {
        Dictionary<string, List<string>> routes = HappyRoutes();
        routes[$"GET /api/v3/episodefile/{EpisodeFileId}"] =
        [
            EpisodeFileJson(Instance("Other Show/S01E01.mkv"), "WFFF")
        ];
        SonarrSpecialIntegrationService service = CreateService(new StubArrHandler(routes), new FakeLinkFileSystem());
        SymlinkLink first = await SeedAsync(SymlinkSetupStep.MonitoringDisabled);
        await service.RunSetupAsync(first, CancellationToken.None);

        Assert.Equal(SymlinkSetupOutcome.Blocked, await service.RunSetupAsync(await StoredAsync(), CancellationToken.None));

        SymlinkLink stored = await StoredAsync();
        // A file Sonarr put somewhere else belongs to somebody else: the link keeps its own step and its
        // own path, and the episode file id is not stored for a file this run did not verify.
        Assert.Equal(SymlinkOperationStatus.Conflict, stored.OperationStatus);
        Assert.Equal(SymlinkSetupStep.EpisodeAssigned, stored.SetupStep);
        Assert.Null(stored.SonarrFileId);
        Assert.Equal(OldLinkPath(), stored.LinkPath);
        Assert.Equal("sonarr-file-not-the-link", Assert.Single(stored.Findings).Code);
    }

    [Fact]
    public async Task MonitoringThatStaysOnIsNotReportedAsASetup()
    {
        Dictionary<string, List<string>> routes = HappyRoutes();
        routes[$"GET /api/v3/episode/{EpisodeId}"] =
        [
            $$"""{ "id": {{EpisodeId}}, "seriesId": {{SeriesId}}, "monitored": true }""",
            $$"""{ "id": {{EpisodeId}}, "seriesId": {{SeriesId}}, "monitored": true }"""
        ];
        SonarrSpecialIntegrationService service = CreateService(new StubArrHandler(routes), new FakeLinkFileSystem());
        SymlinkLink link = await SeedAsync(SymlinkSetupStep.MonitoringDisabled);

        Assert.Equal(SymlinkSetupOutcome.Blocked, await service.RunSetupAsync(link, CancellationToken.None));

        SymlinkLink stored = await StoredAsync();
        Assert.Equal(SymlinkSetupStep.MonitoringDisabled, stored.SetupStep);
        Assert.False(stored.MonitoringDisabled);
        // What monitoring was before is still worth knowing, even though the change did not take.
        Assert.True(stored.MonitoringOriginal);
        Assert.Equal("monitoring-still-on", Assert.Single(stored.Findings).Code);
    }

    [Fact]
    public async Task AnUnreachableInstanceWaitsOnTheSameStepInsteadOfBlamingTheLink()
    {
        StubArrHandler handler = new(HappyRoutes()) { Throws = new HttpRequestException("connection refused") };
        SonarrSpecialIntegrationService service = CreateService(handler, new FakeLinkFileSystem());
        SymlinkLink link = await SeedAsync(SymlinkSetupStep.MonitoringDisabled);

        Assert.Equal(SymlinkSetupOutcome.Waiting, await service.RunSetupAsync(link, CancellationToken.None));

        SymlinkLink stored = await StoredAsync();
        Assert.Equal(SymlinkSetupStep.MonitoringDisabled, stored.SetupStep);
        Assert.Equal(SymlinkOperationStatus.Pending, stored.OperationStatus);
        Assert.Equal("arr-unreachable", Assert.Single(stored.Findings).Code);
    }

    [Fact]
    public async Task ARenameThatLeftACopyInsteadOfTheLinkIsAConflictAndNothingIsDeleted()
    {
        // The entry at the new path exists but is not a link, which is the one shape that must never be
        // answered by removing the registered entry.
        FakeLinkFileSystem files = new() { Links = { [NewLinkPath()] = null } };
        SonarrSpecialIntegrationService service = CreateService(new StubArrHandler(HappyRoutes()), files);
        SymlinkLink first = await SeedAsync(SymlinkSetupStep.MonitoringDisabled);
        await service.RunSetupAsync(first, CancellationToken.None);
        await service.RunSetupAsync(await StoredAsync(), CancellationToken.None);

        Assert.Equal(SymlinkSetupOutcome.Blocked, await service.RunSetupAsync(await StoredAsync(), CancellationToken.None));

        SymlinkLink stored = await StoredAsync();
        Assert.Equal(SymlinkOperationStatus.Conflict, stored.OperationStatus);
        Assert.Equal(SymlinkSetupStep.Renamed, stored.SetupStep);
        Assert.Equal(OldLinkPath(), stored.LinkPath);
        Assert.Equal("renamed-entry-not-a-link", Assert.Single(stored.Findings).Code);
        Assert.Empty(files.Created);
    }

    [Fact]
    public async Task ALinkThatNoLongerResolvesToTheChosenFilmIsNotTakenOver()
    {
        FakeLinkFileSystem files = new() { Links = { [NewLinkPath()] = "../../other/The Empress.mkv" } };
        SonarrSpecialIntegrationService service = CreateService(new StubArrHandler(HappyRoutes()), files);
        SymlinkLink first = await SeedAsync(SymlinkSetupStep.MonitoringDisabled);
        await service.RunSetupAsync(first, CancellationToken.None);
        await service.RunSetupAsync(await StoredAsync(), CancellationToken.None);

        Assert.Equal(SymlinkSetupOutcome.Blocked, await service.RunSetupAsync(await StoredAsync(), CancellationToken.None));

        SymlinkLink stored = await StoredAsync();
        Assert.Equal(OldLinkPath(), stored.LinkPath);
        Assert.Equal("renamed-link-stale", Assert.Single(stored.Findings).Code);
    }

    [Fact]
    public async Task ARenamingInstanceWithoutProposalsCompletesWithItsOwnNameAndSaysWhy()
    {
        Dictionary<string, List<string>> routes = HappyRoutes();
        routes["GET /api/v3/config/naming"] = ["""{ "renameEpisodes": false }"""];
        routes["GET /api/v3/rename"] = ["[]"];
        SonarrSpecialIntegrationService service = CreateService(new StubArrHandler(routes), new FakeLinkFileSystem());
        SymlinkLink first = await SeedAsync(SymlinkSetupStep.MonitoringDisabled);
        await service.RunSetupAsync(first, CancellationToken.None);

        Assert.Equal(SymlinkSetupOutcome.Completed, await service.RunSetupAsync(await StoredAsync(), CancellationToken.None));

        SymlinkLink stored = await StoredAsync();
        // Renaming being off is not this feature's decision to change, so the link keeps its name and the
        // reason stays visible.
        Assert.Equal(SymlinkSetupStep.Completed, stored.SetupStep);
        Assert.Equal(OldName, stored.AppliedFilename);
        Assert.Equal(OldLinkPath(), stored.LinkPath);
        Assert.Equal("renaming-disabled", Assert.Single(stored.Findings).Code);
    }

    [Fact]
    public async Task ANameNobodyProposedIsNotTreatedAsCorrect()
    {
        Dictionary<string, List<string>> routes = HappyRoutes();
        routes["GET /api/v3/rename"] = ["[]"];
        SonarrSpecialIntegrationService service = CreateService(new StubArrHandler(routes), new FakeLinkFileSystem());
        SymlinkLink first = await SeedAsync(SymlinkSetupStep.MonitoringDisabled);
        await service.RunSetupAsync(first, CancellationToken.None);

        Assert.Equal(SymlinkSetupOutcome.Blocked, await service.RunSetupAsync(await StoredAsync(), CancellationToken.None));

        SymlinkLink stored = await StoredAsync();
        Assert.Equal(SymlinkSetupStep.Renamed, stored.SetupStep);
        Assert.Equal("rename-proposal-missing", Assert.Single(stored.Findings).Code);
    }

    [Fact]
    public async Task ARadarrValueThatIsNotKnownIsNeverWrittenOverWhatSonarrReports()
    {
        StubArrHandler handler = new(HappyRoutes());
        FakeLinkFileSystem files = new() { Links = { [NewLinkPath()] = RelativeTextToSource() } };
        SonarrSpecialIntegrationService service = CreateService(handler, files);
        // Radarr has nothing to say about this file, which must not become an empty write.
        SymlinkFileMetadata knowsNothing = new() { Origin = "radarr" };
        SymlinkLink first = await SeedAsync(SymlinkSetupStep.MonitoringDisabled, expected: knowsNothing);
        await service.RunSetupAsync(first, CancellationToken.None);
        await service.RunSetupAsync(await StoredAsync(), CancellationToken.None);
        Assert.Equal(SymlinkSetupOutcome.Completed, await service.RunSetupAsync(await StoredAsync(), CancellationToken.None));

        Assert.DoesNotContain(handler.Requests, request => request.Path == "/api/v3/episodefile" && request.Method == "PUT");
        SymlinkLink stored = await StoredAsync();
        // Confirmed still means what Sonarr itself reported, not what Radarr failed to say.
        Assert.Equal("WFFF", stored.ConfirmedMetadata?.ReleaseGroup);
        Assert.Equal("WEBDL-1080p", stored.ConfirmedMetadata?.Quality);
    }

    [Fact]
    public async Task AStaleRevisionStopsTheRunInsteadOfOverwritingAnotherOperation()
    {
        SonarrSpecialIntegrationService service = CreateService(new StubArrHandler(HappyRoutes()), new FakeLinkFileSystem());
        SymlinkLink stale = await SeedAsync(SymlinkSetupStep.MonitoringDisabled);
        await store.UpdateLinkAsync(await StoredAsync(), stale.Revision, CancellationToken.None);

        Assert.Equal(SymlinkSetupOutcome.Blocked, await service.RunSetupAsync(stale, CancellationToken.None));

        SymlinkLink stored = await StoredAsync();
        Assert.Equal(SymlinkSetupStep.MonitoringDisabled, stored.SetupStep);
        Assert.False(stored.MonitoringDisabled);
    }

    [Fact]
    public async Task ALinkWhoseInstanceIsGoneIsAConflictRatherThanALinkToRepair()
    {
        SonarrSpecialIntegrationService service = CreateService(new StubArrHandler(HappyRoutes()), new FakeLinkFileSystem());
        SymlinkLink link = await SeedAsync(SymlinkSetupStep.MonitoringDisabled);
        link.SonarrInstanceId = "deleted-sonarr";
        await store.UpdateLinkAsync(link, link.Revision, CancellationToken.None);

        Assert.Equal(SymlinkSetupOutcome.Blocked, await service.RunSetupAsync(await StoredAsync(), CancellationToken.None));

        SymlinkLink stored = await StoredAsync();
        Assert.Equal(SymlinkOperationStatus.Conflict, stored.OperationStatus);
        Assert.Equal("sonarr-instance-missing", Assert.Single(stored.Findings).Code);
    }

    private SonarrSpecialIntegrationService CreateService(StubArrHandler handler, ISymlinkFileSystem files) =>
        new(
            store,
            new FixedSettings(Settings()),
            new SonarrClient(new HttpClient(handler)),
            new MediaPathMappingService(new FixedSettings(Settings())),
            files,
            clock,
            NullLogger<SonarrSpecialIntegrationService>.Instance);

    private async Task<SymlinkLink> SeedAsync(SymlinkSetupStep step, SymlinkFileMetadata? expected = null)
    {
        SymlinkLink link = new()
        {
            Id = LinkId,
            RadarrInstanceId = "radarr-1",
            RadarrMovieId = 70,
            RadarrFileId = 900,
            SonarrInstanceId = SonarrInstanceId,
            SonarrSeriesId = SeriesId,
            SonarrEpisodeId = EpisodeId,
            SeasonNumber = 0,
            EpisodeNumber = 1,
            StorageAreaId = "main",
            LinkPath = OldLinkPath(),
            LinkText = RelativeTextToSource(),
            SourcePath = SourcePath(),
            OperationStatus = SymlinkOperationStatus.Pending,
            SetupStep = step,
            ExpectedMetadata = expected ?? ExpectedFromRadarr(),
            AutoFollowUp = true,
            CreatedAt = Start,
            UpdatedAt = Start
        };
        await store.InsertLinkAsync(link, CancellationToken.None);
        return await StoredAsync();
    }

    private async Task<SymlinkLink> StoredAsync() =>
        await store.GetLinkAsync(LinkId, CancellationToken.None)
        ?? throw new InvalidOperationException($"The link {LinkId} was never registered.");

    private static SymlinkFileMetadata ExpectedFromRadarr() => new()
    {
        Origin = "radarr",
        ReleaseGroup = "WFFF",
        Languages = ["German"],
        Quality = "WEBDL-1080p",
        Size = 5_368_709_120
    };

    /// <summary>
    /// The conversation of a setup that works, one answer per read. The episode answers three times
    /// because the service reads it before the change, after it, and again to find the file the scan
    /// produced; the episode file answers three times for the same reason on its own side.
    /// </summary>
    private static Dictionary<string, List<string>> HappyRoutes() => new()
    {
        [$"GET /api/v3/episode/{EpisodeId}"] =
        [
            $$"""{ "id": {{EpisodeId}}, "seriesId": {{SeriesId}}, "seasonNumber": 0, "episodeNumber": 1, "monitored": true }""",
            $$"""{ "id": {{EpisodeId}}, "seriesId": {{SeriesId}}, "seasonNumber": 0, "episodeNumber": 1, "monitored": false }""",
            $$"""{ "id": {{EpisodeId}}, "seriesId": {{SeriesId}}, "seasonNumber": 0, "episodeNumber": 1, "monitored": false, "episodeFileId": {{EpisodeFileId}} }"""
        ],
        ["PUT /api/v3/episode"] = [$$"""{ "id": {{EpisodeId}}, "monitored": false }"""],
        ["POST /api/v3/command"] = ["""{ "id": 91, "status": "queued" }"""],
        ["GET /api/v3/command/91"] =
        [
            $$"""{ "id": 91, "status": "completed", "queued": "{{Start.ToString("O")}}" }"""
        ],
        [$"GET /api/v3/episodefile/{EpisodeFileId}"] =
        [
            EpisodeFileJson(Instance($"{SeriesFolder}/Specials/{OldName}"), null),
            EpisodeFileJson(Instance($"{SeriesFolder}/Specials/{OldName}"), "WFFF"),
            EpisodeFileJson(Instance($"{SeriesFolder}/Specials/{NewName}"), "WFFF")
        ],
        ["GET /api/v3/config/naming"] =
        [
            """{ "renameEpisodes": true, "specialEpisodeFormat": "{Series Title} - {Air-Date} - {Episode Title}" }"""
        ],
        ["GET /api/v3/rename"] =
        [
            $$"""[ { "id": {{EpisodeFileId}}, "filename": "{{OldName}}", "newFilename": "{{NewName}}" } ]"""
        ]
    };

    private static string EpisodeFileJson(string path, string? releaseGroup) =>
        $$"""
          {
            "id": {{EpisodeFileId}},
            "path": {{Quote(path)}},
            "size": 5368709120,
            "releaseGroup": {{Quote(releaseGroup)}},
            "languages": [ { "name": "German" } ],
            "quality": { "quality": { "name": "WEBDL-1080p", "source": "web", "resolution": 1080 } }
          }
          """;

    private static string Quote(string? value) => JsonSerializer.Serialize(value);

    private static string SinglePost(string commandName, StubArrHandler handler) =>
        handler.Posts.Single(body => body.Contains(commandName));

    private static AppSettings Settings() => new()
    {
        SonarrInstances =
        [
            new ArrInstanceSettings
            {
                Id = SonarrInstanceId,
                Name = "Sonarr",
                BaseUrl = "http://sonarr.local",
                ApiKey = "secret"
            }
        ],
        Symlinks = new SymlinkSettings
        {
            Revision = 3,
            StorageAreas =
            [
                new MediaStorageArea
                {
                    Id = "main",
                    Name = "Main",
                    MoviesRoot = LibManagarr("movies"),
                    SeriesRoot = LibManagarr("series")
                }
            ],
            PathMappings =
            [
                new ArrPathMapping
                {
                    Id = "sonarr-series",
                    InstanceId = SonarrInstanceId,
                    Kind = ArrApplicationKind.Sonarr,
                    InstanceRoot = InstanceRoot(),
                    LibManagarrRoot = LibManagarr("series")
                }
            ]
        }
    };

    private static string RelativeTextToSource() => $"../../../movies/{MovieFolder}/{SourceFileName}";

    private const string SourceFileName = "The.Battle.of.Unato.2016.1080p.mkv";

    private static string SourcePath() => LibManagarr($"movies/{MovieFolder}/{SourceFileName}");

    private static string OldLinkPath() => LibManagarr($"series/{SeriesFolder}/Specials/{OldName}");

    private static string NewLinkPath() => LibManagarr($"series/{SeriesFolder}/Specials/{NewName}");

    private static string InstanceRoot() => Instance(string.Empty);

    private static string Instance(string relative) =>
        ArrPathRules.RuntimeSyntax == ArrPathSyntax.Windows
            ? @"D:\series" + (relative.Length == 0 ? string.Empty : "\\" + relative.Replace('/', '\\'))
            : "/srv/series" + (relative.Length == 0 ? string.Empty : "/" + relative);

    private static string LibManagarr(string relative) =>
        ArrPathRules.RuntimeSyntax == ArrPathSyntax.Windows
            ? @"C:\media\" + relative.Replace('/', '\\')
            : "/media/" + relative;

    private sealed class FixedSettings(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) => Task.FromResult(value);
    }

    private sealed record RecordedRequest(string Method, string Path, string Body);

    /// <summary>
    /// Every route answers from its own script: the first read gets the first entry and the last entry
    /// keeps answering afterwards. That is what lets one test describe a conversation across several
    /// passes without a real instance in the middle.
    /// </summary>
    private sealed class StubArrHandler(Dictionary<string, List<string>> jsonByRoute) : HttpMessageHandler
    {
        private readonly Dictionary<string, int> reads = new(StringComparer.OrdinalIgnoreCase);

        public List<RecordedRequest> Requests { get; } = [];

        public IEnumerable<string> Posts =>
            Requests.Where(request => request.Method == "POST").Select(request => request.Body);

        /// <summary>Set to make the instance unreachable, which is a state of its own.</summary>
        public Exception? Throws { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Throws is not null) throw Throws;

            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            string path = request.RequestUri!.AbsolutePath;
            Requests.Add(new RecordedRequest(request.Method.Method, path, body));

            string key = $"{request.Method} {path}";
            if (!jsonByRoute.TryGetValue(key, out List<string>? script))
            {
                return Response("{}");
            }

            int index = Math.Min(reads.TryGetValue(key, out int seen) ? seen : 0, script.Count - 1);
            reads[key] = index + 1;
            return Response(script[index]);
        }

        private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    /// <summary>
    /// Only the two questions this service asks a file system: is something at that path, and is it a link
    /// with which content. Anything else here would pretend to be real symlink behaviour, which is proven
    /// against a real file system in its own test stage.
    /// </summary>
    private sealed class FakeLinkFileSystem : ISymlinkFileSystem
    {
        /// <summary>Path to stored link content; a null content means an entry that is not a link.</summary>
        public Dictionary<string, string?> Links { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Created { get; } = [];

        public bool EntryExists(string path) => Links.ContainsKey(path);

        public bool IsSymlink(string path) => Links.TryGetValue(path, out string? text) && text is not null;

        public string? ReadLinkTarget(string path) => Links.TryGetValue(path, out string? text) ? text : null;

        public void CreateFileSymlink(string linkPath, string targetPath)
        {
            Created.Add(linkPath);
            Links[linkPath] = targetPath;
        }

        public byte[] ReadAllBytes(string path) => throw new NotSupportedException("This service does not read file content.");
        public void OpenForRead(string path) => throw new NotSupportedException("This service does not open files.");
        public void WriteAllBytes(string path, byte[] content) => throw new NotSupportedException("This service does not write files.");
        public void DeleteEntry(string path) => throw new NotSupportedException("The setup run must never delete an entry.");
        public void MoveEntry(string sourcePath, string destinationPath) => throw new NotSupportedException("The setup run must never move an entry itself.");
        public void CreateDirectory(string path) => throw new NotSupportedException("This service creates no directory trees.");
        public IReadOnlyList<string> ListEntries(string directoryPath) => throw new NotSupportedException("This service lists nothing.");
    }
}
