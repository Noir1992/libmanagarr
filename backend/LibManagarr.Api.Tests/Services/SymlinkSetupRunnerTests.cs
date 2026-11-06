using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibManagarr.Api.Tests.Services;

/// <summary>
/// Which register rows a setup pass takes and which it leaves alone. An operation has two halves — the
/// link on the shelf and the entry inside Sonarr — and deciding which one a row still owes is the whole
/// job here. What a person has to decide about must not be pushed around by a timer, so those rows are
/// proven to stay exactly as they are.
/// </summary>
public sealed class SymlinkSetupRunnerTests
{
    private const string RadarrInstanceId = "radarr-1";
    private const string SonarrInstanceId = "sonarr-1";
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
    private static readonly string SourcePath = SymlinkTestPaths.LibManagarr("movies/The Battle of Unato (2016)/unato.mkv");

    [Fact]
    public async Task TheSweepWritesTheLinkThatADeadRunOwed()
    {
        InMemorySymlinkStore store = new();
        SymlinkLink owed = await SeedAsync(store, operation: SymlinkOperationStatus.Pending);
        FakeLinkFileSystem files = new();

        int waiting = await RunAsync(store, files);

        Assert.Equal(0, waiting);
        Assert.Equal([owed.LinkPath], files.CreatedLinks);
        SymlinkLink stored = await StoredAsync(store, owed.Id);
        Assert.Equal(SymlinkOperationStatus.Active, stored.OperationStatus);
        // The Sonarr half starts from here on; the pass does not run it in the same breath, which is what
        // lets a slow instance be waited for without holding anything else up.
        Assert.Equal(SymlinkSetupStep.MonitoringDisabled, stored.SetupStep);
    }

    [Fact]
    public async Task TheSweepFinishesAnOwedDeletionAndLeavesThePairDeclined()
    {
        InMemorySymlinkStore store = new();
        SymlinkLink owed = await SeedAsync(
            store,
            operation: SymlinkOperationStatus.DeletePending,
            setupStep: SymlinkSetupStep.Completed);
        FakeLinkFileSystem files = new();
        files.Entries[owed.LinkPath] = owed.LinkText;
        files.Entries[owed.SourcePath] = null;

        int waiting = await RunAsync(store, files);

        Assert.Equal(0, waiting);
        Assert.Equal([owed.LinkPath], files.Removed);
        Assert.True(files.Entries.ContainsKey(owed.SourcePath));
        Assert.Null(await store.GetLinkAsync(owed.Id, CancellationToken.None));
        SymlinkRejection decline = Assert.Single(await store.GetRejectionsAsync(CancellationToken.None));
        Assert.Equal(owed.Pair, decline.Pair);
    }

    [Fact]
    public async Task TheSweepLeavesAConflictThatWaitsForAPersonExactlyAsItIs()
    {
        InMemorySymlinkStore store = new();
        // The link itself is proven; what stopped is inside Sonarr and names a decision nobody made yet.
        SymlinkLink blocked = await SeedAsync(
            store,
            operation: SymlinkOperationStatus.Conflict,
            setupStep: SymlinkSetupStep.MetadataMatched);
        FakeLinkFileSystem files = new();

        int waiting = await RunAsync(store, files);

        // The instance is never asked (the handler would throw) and nothing is written, so the finding
        // keeps its meaning instead of being refreshed by a run that cannot resolve it.
        Assert.Equal(0, waiting);
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(files.Removed);
        SymlinkLink stored = await StoredAsync(store, blocked.Id);
        Assert.Equal(SymlinkOperationStatus.Conflict, stored.OperationStatus);
        Assert.Equal(SymlinkSetupStep.MetadataMatched, stored.SetupStep);
        Assert.Equal(["link-path-not-translatable"], stored.Findings.Select(finding => finding.Code));
    }

    [Fact]
    public async Task TheSweepCarriesTheOtherRowsWhenOneOfThemBreaks()
    {
        InMemorySymlinkStore store = new();
        SymlinkLink broken = await SeedAsync(store, id: "link-1", episode: 5, operation: SymlinkOperationStatus.Pending);
        SymlinkLink healthy = await SeedAsync(store, id: "link-2", episode: 6, operation: SymlinkOperationStatus.Pending);
        FakeLinkFileSystem files = new();
        files.BrokenPaths.Add(broken.LinkPath);

        int waiting = await RunAsync(store, files);

        Assert.Equal(0, waiting);
        // One pair that cannot be carried forward is never allowed to end the pass for the others: the
        // broken write is its own row's story, and the second entry got its link.
        Assert.Equal([healthy.LinkPath], files.CreatedLinks);
        Assert.Equal(SymlinkOperationStatus.Pending, (await StoredAsync(store, broken.Id)).OperationStatus);
        Assert.Equal(SymlinkOperationStatus.Active, (await StoredAsync(store, healthy.Id)).OperationStatus);
    }

    [Fact]
    public async Task TheSweepComparesARestoredEntryBeforeWritingTheLinkItOwes()
    {
        InMemorySymlinkStore store = new();
        SymlinkLink restored = await SeedAsync(
            store,
            findings: [new SymlinkFinding("restored-unchecked", "This entry came back from a backup.", Start)]);
        FakeLinkFileSystem files = new();

        // A restore says what was registered, never what the shelf holds now, so the entry is seen first
        // and the link it still owes waits for that look.
        Assert.Equal(1, await RunAsync(store, files));
        Assert.Empty(files.CreatedLinks);
        Assert.Equal(SymlinkOperationStatus.Pending, (await StoredAsync(store, restored.Id)).OperationStatus);

        // Recognised, the entry is an ordinary owed row again and the next pass writes what it always
        // planned to write — the comparison repairs nothing of its own.
        Assert.Equal(0, await RunAsync(store, files));
        Assert.Equal([restored.LinkPath], files.CreatedLinks);
        Assert.Equal(SymlinkSetupStep.MonitoringDisabled, (await StoredAsync(store, restored.Id)).SetupStep);
    }

    [Fact]
    public async Task TheSweepStopsARestoredEntryWhoseLinkIsNotThereAnymore()
    {
        InMemorySymlinkStore store = new();
        SymlinkLink restored = await SeedAsync(
            store,
            operation: SymlinkOperationStatus.Active,
            setupStep: SymlinkSetupStep.Completed,
            findings: [new SymlinkFinding("restored-unchecked", "This entry came back from a backup.", Start)]);
        // The entry says its link was proven once; this media tree does not have it, so either the tree
        // moved or the backup belongs to another installation.
        FakeLinkFileSystem files = new();

        Assert.Equal(0, await RunAsync(store, files));

        // A conflict waits for a person instead of being counted as work a later pass can finish, and the
        // pass afterwards leaves it exactly as it was.
        SymlinkLink stored = await StoredAsync(store, restored.Id);
        Assert.Equal(SymlinkOperationStatus.Conflict, stored.OperationStatus);
        Assert.Equal(["restored-entry-differs"], stored.Findings.Select(finding => finding.Code));
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(files.Removed);
        Assert.Equal(0, await RunAsync(store, files));
        Assert.Equal(SymlinkOperationStatus.Conflict, (await StoredAsync(store, restored.Id)).OperationStatus);
    }

    [Fact]
    public async Task TheSweepFollowsAnEntryTheLibraryMovedOnFrom()
    {
        InMemorySymlinkStore store = new();
        await store.SaveCapabilityCheckAsync(new SymlinkCapabilityCheck
        {
            StorageAreaId = "main",
            Status = SymlinkCapabilityStatus.Available,
            Detail = "File symlinks work in the series root.",
            CheckedAt = Start,
            ConfigRevision = SymlinkFixture.ConfigRevision
        }, CancellationToken.None);
        SymlinkLink marked = await SeedAsync(
            store,
            operation: SymlinkOperationStatus.Updating,
            setupStep: SymlinkSetupStep.LinkCreated,
            findings: [new SymlinkFinding("source-replaced", "Radarr has another file for that movie now.", Start)],
            sonarrFileId: SymlinkFixture.SonarrEpisodeFileId,
            sourceVersion: SymlinkFixture.SourceVersion(SymlinkFixture.RadarrFileId, SymlinkFixture.SourceSize));
        FakeLinkFileSystem files = new();
        files.Entries[marked.LinkPath] = marked.LinkText;
        files.Entries[marked.SourcePath] = null;

        int waiting = await RunAsync(store, files, SymlinkFixture.Settings(), new StubArrHandler(SymlinkFixture.UpgradeRoutes()));

        // Not the creation: that would have written the link this row already has, pointing at the file the
        // movie no longer has. The row was followed onto the file that exists now, and the Sonarr half is
        // owed to the next pass rather than run in the same breath.
        Assert.Equal(0, waiting);
        SymlinkLink stored = await StoredAsync(store, marked.Id);
        Assert.Equal(SymlinkOperationStatus.Active, stored.OperationStatus);
        Assert.Equal(SymlinkSetupStep.MonitoringDisabled, stored.SetupStep);
        Assert.Equal(
            SymlinkFixture.SourceVersion(SymlinkFixture.UpgradedRadarrFileId, SymlinkFixture.UpgradedSize),
            stored.SourceVersion);
        Assert.Equal(
            Path.GetRelativePath(
                Path.GetDirectoryName(marked.LinkPath)!,
                SymlinkTestPaths.LibManagarr($"movies/{SymlinkFixture.MovieFolder}/{SymlinkFixture.UpgradedFileName}")),
            files.Entries[marked.LinkPath]);
        // The file Radarr owns was never touched.
        Assert.True(files.Entries.ContainsKey(marked.SourcePath));
    }

    /// <summary>
    /// Sonarr is only ever answered by the handler it is given, and by default that handler refuses to
    /// answer: a pass that would speak to an instance for these rows is a pass that decided something it
    /// should not. A test that works an entry through a live pair brings its own settings and answers.
    /// </summary>
    private static async Task<int> RunAsync(
        InMemorySymlinkStore store,
        FakeLinkFileSystem files,
        AppSettings? settings = null,
        HttpMessageHandler? handler = null)
    {
        FixedSettings configured = new(settings ?? new AppSettings());
        HttpMessageHandler arrHandler = handler ?? new NeverAskedHandler();
        SonarrClient sonarr = new(new HttpClient(arrHandler));
        SymlinkService links = new(
            store,
            configured,
            new MediaPathMappingService(configured),
            new SymlinkCapabilityService(store, configured, files, new FakeTimeProvider(Start), NullLogger<SymlinkCapabilityService>.Instance),
            new SymlinkSetupRequests(),
            new RadarrClient(new HttpClient(arrHandler)),
            sonarr,
            files,
            new FakeTimeProvider(Start),
            NullLogger<SymlinkService>.Instance);
        SonarrSpecialIntegrationService setup = new(
            store,
            configured,
            sonarr,
            new MediaPathMappingService(configured),
            files,
            new FakeTimeProvider(Start),
            NullLogger<SonarrSpecialIntegrationService>.Instance);

        return await new SymlinkSetupRunner(
            store,
            links,
            setup,
            NullLogger<SymlinkSetupRunner>.Instance).RunDueSetupsAsync(CancellationToken.None);
    }

    private static async Task<SymlinkLink> SeedAsync(
        InMemorySymlinkStore store,
        string id = "link-1",
        int episode = 5,
        SymlinkOperationStatus operation = SymlinkOperationStatus.Pending,
        SymlinkSetupStep? setupStep = SymlinkSetupStep.LinkCreated,
        List<SymlinkFinding>? findings = null,
        int? sonarrFileId = null,
        string? sourceVersion = null)
    {
        string linkPath = Path.Combine(
            SymlinkTestPaths.LibManagarr("series/Kabaneri (2016)/Specials"),
            $"Kabaneri (2016) - S00E{episode:D2}.mkv");
        SymlinkLink link = new()
        {
            Id = id,
            RadarrInstanceId = RadarrInstanceId,
            RadarrMovieId = 70,
            RadarrFileId = 4321,
            SonarrInstanceId = SonarrInstanceId,
            SonarrSeriesId = 12,
            SonarrEpisodeId = 5,
            SeasonNumber = 0,
            EpisodeNumber = episode,
            StorageAreaId = "main",
            LinkPath = linkPath,
            LinkText = Path.GetRelativePath(Path.GetDirectoryName(linkPath)!, SourcePath),
            SourcePath = SourcePath,
            OperationStatus = operation,
            SetupStep = setupStep,
            SonarrFileId = sonarrFileId,
            SourceVersion = sourceVersion,
            CreatedAt = Start,
            UpdatedAt = Start,
            Findings = findings ?? (operation == SymlinkOperationStatus.Conflict
                ? [new SymlinkFinding("link-path-not-translatable", "The path of the series moved.", Start)]
                : [])
        };
        await store.InsertLinkAsync(link, CancellationToken.None);
        return link;
    }

    private static async Task<SymlinkLink> StoredAsync(InMemorySymlinkStore store, string id) =>
        await store.GetLinkAsync(id, CancellationToken.None) ?? throw new InvalidOperationException($"Entry {id} is missing.");
}
