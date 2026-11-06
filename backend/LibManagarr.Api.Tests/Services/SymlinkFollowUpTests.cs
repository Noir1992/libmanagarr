using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibManagarr.Api.Tests.Services;

/// <summary>
/// What a finished entry is owed afterwards: the way onto a file Radarr replaced, and the comparison a
/// restored backup owes before anything of the entry's own may happen. The library is answered twice — by
/// the snapshot the synchronization left behind and by the live read — and those two can disagree, which is
/// why the mark is only a mark and the entry moves once the live answer agrees with it. A backup says what
/// was registered, never what the shelf holds now, so the entries it brought back are compared first and
/// left exactly where they are when the comparison does not recognise them.
/// </summary>
public sealed class SymlinkFollowUpTests
{
    private const string RadarrInstanceId = SymlinkFixture.RadarrInstanceId;
    private const string SonarrInstanceId = SymlinkFixture.SonarrInstanceId;
    private const string StorageAreaId = SymlinkFixture.StorageAreaId;
    private const int MovieId = SymlinkFixture.MovieId;
    private const int RadarrFileId = SymlinkFixture.RadarrFileId;
    private const int SeriesId = SymlinkFixture.SeriesId;
    private const int EpisodeId = SymlinkFixture.EpisodeId;
    private const string MovieFolder = SymlinkFixture.MovieFolder;
    private const string SeriesFolder = SymlinkFixture.SeriesFolder;
    private const string LinkName = SymlinkFixture.LinkName;

    private static readonly DateTimeOffset Start = new(2026, 11, 6, 14, 0, 0, TimeSpan.Zero);
    private static readonly string CurrentVersion = SymlinkFixture.SourceVersion(RadarrFileId, SymlinkFixture.SourceSize);
    private static readonly string UpgradedVersion =
        SymlinkFixture.SourceVersion(SymlinkFixture.UpgradedRadarrFileId, SymlinkFixture.UpgradedSize);

    private readonly InMemorySymlinkStore store = new();
    private readonly FakeTimeProvider clock = new(Start);
    private readonly SymlinkSetupRequests setupRequests = new();

    public SymlinkFollowUpTests()
    {
        // A followed entry is moved by the same plan a creation is built from, so its storage area has to
        // carry the answer a working shelf has.
        store.SaveCapabilityCheckAsync(new SymlinkCapabilityCheck
        {
            StorageAreaId = StorageAreaId,
            Status = SymlinkCapabilityStatus.Available,
            Detail = "File symlinks work in the series root.",
            CheckedAt = Start,
            ConfigRevision = SymlinkFixture.ConfigRevision
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task TheSyncMarksTheEntryWhoseMovieHasAnotherFileNow()
    {
        await SeedFinishedEntryAsync("link-1");
        await SeedFinishedEntryAsync("link-2", autoFollowUp: false);
        await SeedFinishedEntryAsync(
            "link-3",
            radarrFileId: SymlinkFixture.UpgradedRadarrFileId,
            sourceVersion: UpgradedVersion);
        SymlinkService service = CreateService(new NeverAskedHandler(), new FakeLinkFileSystem(),
            SymlinkFixture.SettingsWithLibraryFile(SymlinkFixture.UpgradedRadarrFileId, SymlinkFixture.UpgradedSize));

        int marked = await service.MarkReplacedSourcesAsync(CancellationToken.None);

        Assert.Equal(1, marked);
        SymlinkLink stored = await StoredAsync("link-1");
        Assert.Equal(SymlinkOperationStatus.Updating, stored.OperationStatus);
        // The Sonarr half is not owed again yet — the entry has to be pointed at the new file first, which
        // is the same point a creation starts from.
        Assert.Equal(SymlinkSetupStep.LinkCreated, stored.SetupStep);
        Assert.Equal(1, stored.UpdateRevision);
        SymlinkFinding finding = Assert.Single(stored.Findings);
        Assert.Equal("source-replaced", finding.Code);
        Assert.Contains($"{SymlinkFixture.UpgradedRadarrFileId}", finding.Detail);
        // An entry whose person switched following off and one that already reflects the newest file are
        // not the sync's to touch, and neither is an upgrade that has nothing to follow.
        Assert.Equal(SymlinkOperationStatus.Active, (await StoredAsync("link-2")).OperationStatus);
        Assert.Equal(SymlinkSetupStep.Completed, (await StoredAsync("link-2")).SetupStep);
        Assert.Equal(SymlinkOperationStatus.Active, (await StoredAsync("link-3")).OperationStatus);
        Assert.Equal(SymlinkSetupStep.Completed, (await StoredAsync("link-3")).SetupStep);
    }

    [Fact]
    public async Task TheSyncLeavesAnEntryTheLibraryNeverAnsweredAboutAlone()
    {
        await SeedFinishedEntryAsync("link-1");
        // The snapshots of another instance, or none at all, are no answer about this movie's file, and
        // "I do not know" must not become "the source moved".
        SymlinkService service = CreateService(new NeverAskedHandler(), new FakeLinkFileSystem(), SymlinkFixture.Settings());

        Assert.Equal(0, await service.MarkReplacedSourcesAsync(CancellationToken.None));
        SymlinkLink stored = await StoredAsync("link-1");
        Assert.Equal(SymlinkOperationStatus.Active, stored.OperationStatus);
        Assert.Equal(SymlinkSetupStep.Completed, stored.SetupStep);
        Assert.Empty(stored.Findings);
    }

    [Fact]
    public async Task SwitchingFollowingOnQueuesAnEntryThatIsAlreadyBehind()
    {
        await SeedFinishedEntryAsync("link-1", autoFollowUp: false);
        SymlinkService service = CreateService(new NeverAskedHandler(), new FakeLinkFileSystem(),
            SymlinkFixture.SettingsWithLibraryFile(SymlinkFixture.UpgradedRadarrFileId, SymlinkFixture.UpgradedSize));

        SymlinkLink updated = (await service.SetAutoFollowUpAsync("link-1", true, 0, CancellationToken.None))!;

        // Saying "keep following this source" to an entry that is behind is the whole order it needs; it
        // does not wait for the library to be synchronized a second time.
        Assert.True(updated.AutoFollowUp);
        Assert.Equal(SymlinkOperationStatus.Updating, updated.OperationStatus);
        Assert.Equal(SymlinkSetupStep.LinkCreated, updated.SetupStep);
        Assert.Equal(1, updated.UpdateRevision);
        Assert.Equal(["source-replaced"], updated.Findings.Select(finding => finding.Code));
    }

    [Fact]
    public async Task TheFollowUpMovesTheEntryOntoTheNewFileAndOwesTheSonarrHalfAgain()
    {
        SymlinkLink entry = await SeedMarkedEntryAsync("link-1");
        FakeLinkFileSystem files = ShelfWithOwnLink(entry);
        string originalSource = entry.SourcePath;
        SymlinkService service = CreateService(new StubArrHandler(SymlinkFixture.UpgradeRoutes()), files);

        Assert.Equal(SymlinkSetupOutcome.Completed, await service.FinishUpdateAsync(entry, CancellationToken.None));

        SymlinkLink stored = await StoredAsync("link-1");
        Assert.Equal(SymlinkFixture.UpgradedRadarrFileId, stored.RadarrFileId);
        Assert.Equal(UpgradedVersion, stored.SourceVersion);
        Assert.Equal(
            SymlinkTestPaths.LibManagarr($"movies/{MovieFolder}/{SymlinkFixture.UpgradedFileName}"),
            stored.SourcePath);
        Assert.Equal(SymlinkOperationStatus.Active, stored.OperationStatus);
        // The file it was named for is gone, so the scan, the attribution, the reading and the naming all
        // run again for what is there now. Monitoring is left as the entry made it.
        Assert.Equal(SymlinkSetupStep.MonitoringDisabled, stored.SetupStep);
        Assert.Null(stored.SonarrScanCommandId);
        Assert.Null(stored.SonarrRenameCommandId);
        Assert.Null(stored.ConfirmedMetadata);
        Assert.Null(stored.AppliedFilename);
        Assert.Empty(stored.Findings);
        // The new file has the same shape, so the same entry is re-pointed: let go of and written again,
        // with the link text answering the new source.
        Assert.Equal([entry.LinkPath], files.Removed);
        Assert.Equal([entry.LinkPath], files.CreatedLinks);
        Assert.Equal(
            Path.GetRelativePath(
                Path.GetDirectoryName(entry.LinkPath)!,
                SymlinkTestPaths.LibManagarr($"movies/{MovieFolder}/{SymlinkFixture.UpgradedFileName}")),
            files.Entries[entry.LinkPath]);
        // The file Radarr owns was never touched.
        Assert.True(files.Entries.ContainsKey(originalSource));
        Assert.True(await IsPassRequestedAsync());
    }

    [Fact]
    public async Task TheFollowUpWritesTheNewNameWhileTheOldLinkStillStands()
    {
        SymlinkLink entry = await SeedMarkedEntryAsync("link-1");
        FakeLinkFileSystem files = ShelfWithOwnLink(entry);
        string originalLinkPath = entry.LinkPath;
        // A different container is a different file name, so the replacement cannot be written over the
        // entry that is still being followed.
        Dictionary<string, List<string>> routes = SymlinkFixture.UpgradeRoutes();
        routes["GET /api/v3/moviefile"] = [$"[{MovieFileObject(SymlinkFixture.UpgradedRadarrFileId, SymlinkFixture.UpgradedSize, ".mp4")}]"];
        SymlinkService service = CreateService(new StubArrHandler(routes), files);

        Assert.Equal(SymlinkSetupOutcome.Completed, await service.FinishUpdateAsync(entry, CancellationToken.None));

        string newLinkPath = SymlinkFixture.ExpectedLinkPath().Replace(LinkName, LinkName.Replace(".mkv", ".mp4"));
        Assert.Equal([newLinkPath], files.CreatedLinks);
        Assert.Equal([originalLinkPath], files.Removed);
        Assert.Equal(newLinkPath, (await StoredAsync("link-1")).LinkPath);
    }

    [Fact]
    public async Task TheFollowUpLeavesALinkItDoesNotOwnWhereItIs()
    {
        SymlinkLink entry = await SeedMarkedEntryAsync("link-1");
        FakeLinkFileSystem files = ShelfWithOwnLink(entry);
        string originalLinkPath = entry.LinkPath;
        // Somebody else's file stands where this entry's link used to be. It is not this run's to remove —
        // and not a reason to undo the replacement it already proved, either.
        files.Entries[originalLinkPath] = "someone-else.mkv";
        Dictionary<string, List<string>> routes = SymlinkFixture.UpgradeRoutes();
        routes["GET /api/v3/moviefile"] = [$"[{MovieFileObject(SymlinkFixture.UpgradedRadarrFileId, SymlinkFixture.UpgradedSize, ".mp4")}]"];
        SymlinkService service = CreateService(new StubArrHandler(routes), files);

        Assert.Equal(SymlinkSetupOutcome.Completed, await service.FinishUpdateAsync(entry, CancellationToken.None));

        Assert.Empty(files.Removed);
        string newLinkPath = SymlinkFixture.ExpectedLinkPath().Replace(LinkName, LinkName.Replace(".mkv", ".mp4"));
        Assert.Equal([newLinkPath], files.CreatedLinks);
        SymlinkLink stored = await StoredAsync("link-1");
        Assert.Equal(SymlinkOperationStatus.Active, stored.OperationStatus);
        SymlinkFinding finding = Assert.Single(stored.Findings);
        Assert.Equal("replaced-link-left-behind", finding.Code);
        Assert.Contains(originalLinkPath, finding.Detail);
    }

    [Fact]
    public async Task TheFollowUpWaitsWhileTheInstanceStopsAnswering()
    {
        SymlinkLink entry = await SeedMarkedEntryAsync("link-1");
        FakeLinkFileSystem files = ShelfWithOwnLink(entry);
        SymlinkService service = CreateService(
            new StubArrHandler(SymlinkFixture.UpgradeRoutes()) { Throws = new HttpRequestException("Radarr did not answer") },
            files);

        Assert.Equal(SymlinkSetupOutcome.Waiting, await service.FinishUpdateAsync(entry, CancellationToken.None));

        // "Not reachable" is not "wrong pair": the mark stays, so the next pass finds the same owed entry
        // and a restart does not lose it.
        SymlinkLink stored = await StoredAsync("link-1");
        Assert.Equal(SymlinkOperationStatus.Updating, stored.OperationStatus);
        Assert.Equal(SymlinkSetupStep.LinkCreated, stored.SetupStep);
        Assert.Equal(RadarrFileId, stored.RadarrFileId);
        Assert.Equal(["source-replaced"], stored.Findings.Select(finding => finding.Code));
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(files.Removed);
    }

    [Fact]
    public async Task TheFollowUpStopsForAPersonWhenAForeignFileStandsAtItsPath()
    {
        SymlinkLink entry = await SeedMarkedEntryAsync("link-1");
        FakeLinkFileSystem files = new();
        // Not a link at all: something else was made at the path this entry would write.
        files.Entries[entry.LinkPath] = null;
        files.Entries[entry.SourcePath] = null;
        SymlinkService service = CreateService(new StubArrHandler(SymlinkFixture.UpgradeRoutes()), files);

        Assert.Equal(SymlinkSetupOutcome.Blocked, await service.FinishUpdateAsync(entry, CancellationToken.None));

        SymlinkLink stored = await StoredAsync("link-1");
        Assert.Equal(SymlinkOperationStatus.Conflict, stored.OperationStatus);
        // The blocker is stated, and the mark that started the run stays with it: the file really did move,
        // and only a replacement that went through may say otherwise.
        Assert.Equal(["source-replaced", "link-path-taken"], stored.Findings.Select(finding => finding.Code));
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(files.Removed);
    }

    [Fact]
    public async Task TheFollowUpSettlesTheEntryWhenTheLiveAnswerDisagreesWithTheMark()
    {
        SymlinkLink entry = await SeedMarkedEntryAsync("link-1");
        FakeLinkFileSystem files = ShelfWithOwnLink(entry);
        // The snapshots named another file, but Radarr answers with the one this entry already points at —
        // an upgrade that was undone, or a snapshot taken mid-import.
        SymlinkService service = CreateService(new StubArrHandler(SymlinkFixture.HappyRoutes()), files);

        Assert.Equal(SymlinkSetupOutcome.Completed, await service.FinishUpdateAsync(entry, CancellationToken.None));

        SymlinkLink stored = await StoredAsync("link-1");
        Assert.Equal(SymlinkOperationStatus.Active, stored.OperationStatus);
        Assert.Equal(SymlinkSetupStep.Completed, stored.SetupStep);
        Assert.Equal(RadarrFileId, stored.RadarrFileId);
        Assert.Equal(CurrentVersion, stored.SourceVersion);
        Assert.Empty(stored.Findings);
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(files.Removed);
    }

    [Fact]
    public async Task TheFollowUpKeepsTheEntryOwedWhenTheSourceMovedAgain()
    {
        SymlinkLink entry = await SeedMarkedEntryAsync("link-1");
        FakeLinkFileSystem files = ShelfWithOwnLink(entry);
        // The movie moved a second time between the plan and the answer that would have confirmed it. The
        // entry stays owed and the next pass follows the newest stand, so an older answer cannot settle on
        // a state that is already behind.
        Dictionary<string, List<string>> routes = SymlinkFixture.UpgradeRoutes();
        routes["GET /api/v3/moviefile"] =
        [
            $"[{MovieFileObject(SymlinkFixture.UpgradedRadarrFileId, SymlinkFixture.UpgradedSize)}]",
            $"[{MovieFileObject(4323, 7_516_192_768, ".mkv", "WEB-DL")}]"
        ];
        SymlinkService service = CreateService(new StubArrHandler(routes), files);

        SymlinkSetupOutcome outcome = await service.FinishUpdateAsync(entry, CancellationToken.None);

        Assert.Equal(SymlinkSetupOutcome.Waiting, outcome);
        SymlinkLink stored = await StoredAsync("link-1");
        Assert.Equal(SymlinkOperationStatus.Updating, stored.OperationStatus);
        Assert.Equal(SymlinkSetupStep.LinkCreated, stored.SetupStep);
        // What the link provably points at is written down even though the answer ran out from under it —
        // the register never keeps claiming the file this run let go of. The mark stays with it, which is
        // what makes the next pass read the instance again instead of settling.
        Assert.Equal(UpgradedVersion, stored.SourceVersion);
        Assert.Equal(["source-replaced"], stored.Findings.Select(finding => finding.Code));
        Assert.Equal([entry.LinkPath], files.CreatedLinks);
    }

    [Fact]
    public async Task ARestoreAsksEveryEntryToBeSeenAgainOnce()
    {
        await SeedFinishedEntryAsync("link-1");
        await SeedFinishedEntryAsync("link-2");
        SymlinkService service = CreateService(new NeverAskedHandler(), new FakeLinkFileSystem());

        Assert.Equal(2, await service.MarkRestoredEntriesAsync(CancellationToken.None));
        Assert.Equal(
            ["restored-unchecked", "restored-unchecked"],
            (await store.GetLinksAsync(CancellationToken.None))
                .SelectMany(link => link.Findings.Select(finding => finding.Code)));

        // A second start on the same markers says nothing twice.
        Assert.Equal(0, await service.MarkRestoredEntriesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TheComparisonConfirmsTheEntryWhoseLinkIsStillItsOwn()
    {
        SymlinkLink entry = await SeedFinishedEntryAsync("link-1", findings: RestoredMarker());
        FakeLinkFileSystem files = ShelfWithOwnLink(entry);
        SymlinkService service = CreateService(new NeverAskedHandler(), files);

        Assert.Equal(SymlinkSetupOutcome.Waiting, await service.CheckRestoredEntryAsync(entry, CancellationToken.None));

        SymlinkLink stored = await StoredAsync("link-1");
        Assert.Empty(stored.Findings);
        Assert.Equal(SymlinkOperationStatus.Active, stored.OperationStatus);
        Assert.Equal(SymlinkSetupStep.Completed, stored.SetupStep);
        Assert.Equal(Start, stored.LastCheckedAt);
        Assert.Equal(Start, stored.LastSuccessfulCheckAt);
        // The comparison is a look, never a repair: nothing was written, removed or asked.
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(files.Removed);
    }

    [Fact]
    public async Task TheComparisonStopsTheEntryWhoseProvenLinkIsGone()
    {
        SymlinkLink entry = await SeedFinishedEntryAsync("link-1", findings: RestoredMarker());
        // The entry says its link was proven; the shelf does not have it. Either the media tree moved or
        // this backup belongs to another installation, and a timer must not pick one of the two.
        FakeLinkFileSystem files = new();
        SymlinkService service = CreateService(new NeverAskedHandler(), files);

        Assert.Equal(SymlinkSetupOutcome.Blocked, await service.CheckRestoredEntryAsync(entry, CancellationToken.None));

        SymlinkLink stored = await StoredAsync("link-1");
        Assert.Equal(SymlinkOperationStatus.Conflict, stored.OperationStatus);
        SymlinkFinding finding = Assert.Single(stored.Findings);
        Assert.Equal("restored-entry-differs", finding.Code);
        Assert.Contains(entry.LinkPath, finding.Detail);
        Assert.Null(stored.LastSuccessfulCheckAt);
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(files.Removed);
    }

    [Fact]
    public async Task TheComparisonExpectsNothingAtThePathOfAnEntryThatStillOwedItsFirstLink()
    {
        SymlinkLink entry = await SeedFinishedEntryAsync(
            "link-1",
            operation: SymlinkOperationStatus.Pending,
            setupStep: SymlinkSetupStep.LinkCreated,
            findings: RestoredMarker());
        FakeLinkFileSystem files = new();
        SymlinkService service = CreateService(new NeverAskedHandler(), files);

        Assert.Equal(SymlinkSetupOutcome.Waiting, await service.CheckRestoredEntryAsync(entry, CancellationToken.None));

        // An entry that never had a link proven has nothing the file system could contradict; the creation
        // it still owes is what answers its path, with every check that belongs to it.
        SymlinkLink stored = await StoredAsync("link-1");
        Assert.Empty(stored.Findings);
        Assert.Equal(SymlinkOperationStatus.Pending, stored.OperationStatus);
        Assert.Equal(SymlinkSetupStep.LinkCreated, stored.SetupStep);
    }

    private static List<SymlinkFinding> RestoredMarker() =>
        [new SymlinkFinding("restored-unchecked", "This entry came back from a backup.", Start)];

    /// <summary>The shelf as a finished entry leaves it: the entry's own link, and the source it points at.</summary>
    private static FakeLinkFileSystem ShelfWithOwnLink(SymlinkLink entry)
    {
        FakeLinkFileSystem files = new();
        files.Entries[entry.LinkPath] = entry.LinkText;
        files.Entries[entry.SourcePath] = null;
        return files;
    }

    private static string MovieFileObject(
        int fileId,
        long size,
        string extension = ".mkv",
        string releaseGroup = "ION",
        string quality = "WEBDL-1080p") =>
        $$"""
          { "id": {{fileId}}, "movieId": {{MovieId}},
            "path": {{SymlinkTestPaths.Quote(SymlinkFixture.MoviesInstance($"{MovieFolder}/unato-{fileId}{extension}"))}},
            "size": {{size}}, "releaseGroup": "{{releaseGroup}}",
            "languages": [ { "name": "German" } ],
            "quality": { "quality": { "name": "{{quality}}", "source": "web", "resolution": 1080 } } }
          """;

    /// <summary>
    /// The entry as the library sync hands it to the follow-up: finished once, then marked by the marking
    /// itself, so that a follow-up test never invents a state the register would otherwise have to reach.
    /// </summary>
    private async Task<SymlinkLink> SeedMarkedEntryAsync(string id)
    {
        await SeedFinishedEntryAsync(id);
        SymlinkService marking = CreateService(new NeverAskedHandler(), new FakeLinkFileSystem(),
            SymlinkFixture.SettingsWithLibraryFile(SymlinkFixture.UpgradedRadarrFileId, SymlinkFixture.UpgradedSize));

        Assert.Equal(1, await marking.MarkReplacedSourcesAsync(CancellationToken.None));
        return await StoredAsync(id);
    }

    private async Task<SymlinkLink> SeedFinishedEntryAsync(
        string id,
        int radarrFileId = RadarrFileId,
        string? sourceVersion = null,
        bool autoFollowUp = true,
        SymlinkOperationStatus operation = SymlinkOperationStatus.Active,
        SymlinkSetupStep? setupStep = SymlinkSetupStep.Completed,
        List<SymlinkFinding>? findings = null)
    {
        string linkPath = SymlinkFixture.ExpectedLinkPath();
        SymlinkLink link = new()
        {
            Id = id,
            RadarrInstanceId = RadarrInstanceId,
            RadarrMovieId = MovieId,
            RadarrFileId = radarrFileId,
            SonarrInstanceId = SonarrInstanceId,
            SonarrSeriesId = SeriesId,
            SonarrEpisodeId = EpisodeId,
            SeasonNumber = 0,
            EpisodeNumber = EpisodeId,
            StorageAreaId = StorageAreaId,
            LinkPath = linkPath,
            LinkText = Path.GetRelativePath(Path.GetDirectoryName(linkPath)!, SymlinkFixture.SourcePath()),
            SourcePath = SymlinkFixture.SourcePath(),
            OperationStatus = operation,
            SetupStep = setupStep,
            SonarrFileId = SymlinkFixture.SonarrEpisodeFileId,
            SourceVersion = sourceVersion ?? CurrentVersion,
            MonitoringDisabled = true,
            MonitoringOriginal = true,
            AutoFollowUp = autoFollowUp,
            DesiredFilename = LinkName,
            AppliedFilename = LinkName,
            ExpectedMetadata = new SymlinkFileMetadata { Origin = "radarr", ReleaseGroup = "WFFF", Size = SymlinkFixture.SourceSize },
            ConfirmedMetadata = new SymlinkFileMetadata { Origin = "sonarr", ReleaseGroup = "WFFF", Size = SymlinkFixture.SourceSize },
            CreatedAt = Start,
            UpdatedAt = Start,
            Findings = findings ?? []
        };
        await store.InsertLinkAsync(link, CancellationToken.None);
        return await StoredAsync(id);
    }

    private SymlinkService CreateService(
        HttpMessageHandler handler,
        ISymlinkFileSystem files,
        AppSettings? settings = null)
    {
        FixedSettings snapshot = new(settings ?? SymlinkFixture.Settings());
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
            NullLogger<SymlinkService>.Instance);
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
}
