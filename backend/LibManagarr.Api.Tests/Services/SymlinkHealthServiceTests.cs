using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibManagarr.Api.Tests.Services;

/// <summary>
/// What the health pass says about the register as it stands. Every answer here has two halves — the entry on
/// the shelf and the episode in the library snapshot — so the tests are split by the half that is wrong,
/// because each of them is a different thing an operator can do about it. Where neither half can answer, the
/// row says exactly that instead of guessing: an offline share and a refused read have no right to be written
/// up as a deleted film. The pass writes no link and asks no instance, which its doubles refuse on their own.
/// </summary>
public sealed class SymlinkHealthServiceTests
{
    private const string RadarrInstanceId = SymlinkFixture.RadarrInstanceId;
    private const string SonarrInstanceId = SymlinkFixture.SonarrInstanceId;
    private const string StorageAreaId = SymlinkFixture.StorageAreaId;
    private const int MovieId = SymlinkFixture.MovieId;
    private const int RadarrFileId = SymlinkFixture.RadarrFileId;
    private const int SeriesId = SymlinkFixture.SeriesId;
    private const int EpisodeId = SymlinkFixture.EpisodeId;
    private const string SeriesFolder = SymlinkFixture.SeriesFolder;
    private const string LinkName = SymlinkFixture.LinkName;

    private static readonly DateTimeOffset Start = new(2026, 11, 6, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SyncedAt = new(2026, 11, 6, 12, 30, 0, TimeSpan.Zero);

    private readonly InMemorySymlinkStore store = new();
    private readonly FakeTimeProvider clock = new(Start);
    private readonly SymlinkSetupRequests setupRequests = new();
    private readonly FakeLinkFileSystem files = new();

    [Fact]
    public async Task TheEntryStandingExactlyAsItWasLeftIsStampedAndSaysNothing()
    {
        await SeedEntryAsync("link-1");

        SymlinkHealthRun run = await Health().CheckAllAsync(CancellationToken.None);

        Assert.Equal(1, run.LinksChecked);
        Assert.Equal(0, run.Reported);
        Assert.Equal(0, run.Undetermined);
        Assert.Equal(0, run.FollowUpsQueued);
        Assert.Equal(Start, run.StartedAt);
        SymlinkLink stored = await StoredAsync("link-1");
        Assert.Empty(stored.Findings);
        Assert.Equal(Start, stored.LastCheckedAt);
        Assert.Equal(Start, stored.LastSuccessfulCheckAt);
        // The check is a look and never a repair.
        Assert.Empty(files.CreatedLinks);
        Assert.Empty(files.Removed);
    }

    [Fact]
    public async Task ARegisterHoldingNothingIsAnAnswerOfItsOwn()
    {
        SymlinkHealthRun run = await Health().CheckAllAsync(CancellationToken.None);

        Assert.Equal(0, run.LinksChecked);
        Assert.Equal(0, run.Reported);
        Assert.Equal(0, run.Undetermined);
        Assert.Equal(0, run.FollowUpsQueued);
        Assert.False(await IsPassRequestedAsync());
    }

    [Fact]
    public async Task TheLinkThatIsGoneIsReportedAndLosesItsCleanStamp()
    {
        SymlinkLink entry = await SeedEntryAsync("link-1");
        files.Entries.Remove(entry.LinkPath);

        SymlinkHealthRun run = await Health().CheckAllAsync(CancellationToken.None);

        Assert.Equal(1, run.Reported);
        Assert.Equal(0, run.Undetermined);
        SymlinkLink stored = await StoredAsync("link-1");
        SymlinkFinding finding = Assert.Single(stored.Findings);
        Assert.Equal("link-lost", finding.Code);
        Assert.Contains(entry.LinkPath, finding.Detail);
        Assert.Equal(Start, stored.LastCheckedAt);
        Assert.Null(stored.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task ARegularFileAtThePathOfAManagedLinkIsReportedAsSuch()
    {
        SymlinkLink entry = await SeedEntryAsync("link-1");
        // Somebody made a real file where this entry's link stands: the bytes may even be the same film, but
        // the entry no longer speaks for what is there.
        files.Entries[entry.LinkPath] = null;

        await Health().CheckAllAsync(CancellationToken.None);

        SymlinkFinding finding = Assert.Single((await StoredAsync("link-1")).Findings);
        Assert.Equal("entry-not-a-link", finding.Code);
        Assert.Contains(entry.LinkPath, finding.Detail);
    }

    [Fact]
    public async Task TheLinkPointedAtAnotherFileIsReportedWithWhereItWent()
    {
        SymlinkLink entry = await SeedEntryAsync("link-1");
        files.Entries[entry.LinkPath] = "The.Battle.of.Unato.2016.720p.mkv";

        await Health().CheckAllAsync(CancellationToken.None);

        SymlinkFinding finding = Assert.Single((await StoredAsync("link-1")).Findings);
        Assert.Equal("link-target-moved", finding.Code);
        Assert.Contains("The.Battle.of.Unato.2016.720p.mkv", finding.Detail);
        Assert.Contains(entry.SourcePath, finding.Detail);
    }

    [Fact]
    public async Task AReadTheRightsRefuseIsNotWrittenUpAsAMissingFile()
    {
        SymlinkLink entry = await SeedEntryAsync("link-1");
        files.RefusedReads.Add(entry.LinkPath);

        SymlinkHealthRun run = await Health().CheckAllAsync(CancellationToken.None);

        Assert.Equal(1, run.Reported);
        Assert.Equal(1, run.Undetermined);
        SymlinkLink stored = await StoredAsync("link-1");
        SymlinkFinding finding = Assert.Single(stored.Findings);
        Assert.Equal("access-denied", finding.Code);
        Assert.Null(stored.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task AShareThatIsNotMountedIsReportedAsAShareAndNotAsADeletedFilm()
    {
        SymlinkLink entry = await SeedEntryAsync("link-1");
        files.UnreadablePaths.Add(entry.LinkPath);
        files.UnreadableRoots.Add(SymlinkTestPaths.LibManagarr("series"));

        SymlinkHealthRun run = await Health().CheckAllAsync(CancellationToken.None);

        Assert.Equal(1, run.Undetermined);
        SymlinkFinding finding = Assert.Single((await StoredAsync("link-1")).Findings);
        Assert.Equal("storage-unreachable", finding.Code);
    }

    [Fact]
    public async Task AReadableShareAnswersTheMissingFileItOwns()
    {
        SymlinkLink entry = await SeedEntryAsync("link-1");
        // The share lists fine, so only this one file is gone — which is a different finding and a different
        // thing to do about it than a mount that is not there at all.
        files.UnreadablePaths.Add(entry.LinkPath);

        SymlinkHealthRun run = await Health().CheckAllAsync(CancellationToken.None);

        Assert.Equal(0, run.Undetermined);
        SymlinkFinding finding = Assert.Single((await StoredAsync("link-1")).Findings);
        Assert.Equal("source-missing", finding.Code);
    }

    [Fact]
    public async Task ASourceThatMovedWhileFollowingIsOnQueuesTheFollowUpAndAsksForTheSetupPass()
    {
        await SeedEntryAsync("link-1");

        SymlinkHealthRun run = await Health(ProvenLibraryWithMovieFile(
            SymlinkFixture.UpgradedRadarrFileId, SymlinkFixture.UpgradedSize)).CheckAllAsync(CancellationToken.None);

        Assert.Equal(1, run.FollowUpsQueued);
        Assert.Equal(1, run.Reported);
        SymlinkLink stored = await StoredAsync("link-1");
        // The health pass only notices a missed upgrade; the run that carries it out is the setup pass.
        Assert.True(await IsPassRequestedAsync());
        Assert.Equal(SymlinkOperationStatus.Updating, stored.OperationStatus);
        Assert.Equal(SymlinkSetupStep.LinkCreated, stored.SetupStep);
        Assert.Equal(1, stored.UpdateRevision);
        // The sentence the queueing wrote stays on the row — it is the answer a later pass reads to know why
        // this entry is owed something.
        SymlinkFinding finding = Assert.Single(stored.Findings);
        Assert.Equal("source-replaced", finding.Code);
        Assert.Contains($"{SymlinkFixture.UpgradedRadarrFileId}", finding.Detail);
        Assert.Null(stored.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task ASourceThatMovedWhileFollowingIsOffIsReportedWithoutQueueingAnything()
    {
        await SeedEntryAsync("link-1", autoFollowUp: false);

        SymlinkHealthRun run = await Health(ProvenLibraryWithMovieFile(
            SymlinkFixture.UpgradedRadarrFileId, SymlinkFixture.UpgradedSize)).CheckAllAsync(CancellationToken.None);

        Assert.Equal(0, run.FollowUpsQueued);
        // The switch does what it promises, and the difference is still worth seeing.
        Assert.Equal(1, run.Reported);
        Assert.False(await IsPassRequestedAsync());
        SymlinkLink stored = await StoredAsync("link-1");
        Assert.Equal(SymlinkOperationStatus.Active, stored.OperationStatus);
        Assert.Equal(SymlinkSetupStep.Completed, stored.SetupStep);
        Assert.Equal(0, stored.UpdateRevision);
        SymlinkFinding finding = Assert.Single(stored.Findings);
        Assert.Equal("source-replaced", finding.Code);
        Assert.Contains("Later upgrades are not followed", finding.Detail);
    }

    [Fact]
    public async Task AFileTheLibraryNeverAnsweredAboutIsSaidSoInsteadOfCalledGone()
    {
        await SeedEntryAsync("link-1");
        AppSettings settings = ProvenLibrary();
        settings.RadarrSnapshots = [];

        SymlinkHealthRun run = await Health(settings).CheckAllAsync(CancellationToken.None);

        Assert.Equal(1, run.Undetermined);
        Assert.Equal(0, run.FollowUpsQueued);
        SymlinkFinding finding = Assert.Single((await StoredAsync("link-1")).Findings);
        Assert.Equal("radarr-unsynced", finding.Code);
        Assert.Contains(MovieId.ToString(), finding.Detail);
    }

    [Fact]
    public async Task TheEpisodeTheSnapshotDoesNotKnowIsReportedWithTheDateItRestsOn()
    {
        await SeedEntryAsync("link-1");
        AppSettings settings = ProvenLibrary();
        settings.SonarrSnapshots[0].Series[0].Seasons[0].Episodes.Clear();

        await Health(settings).CheckAllAsync(CancellationToken.None);

        SymlinkFinding finding = Assert.Single((await StoredAsync("link-1")).Findings);
        Assert.Equal("sonarr-episode-missing", finding.Code);
        Assert.Contains(EpisodeId.ToString(), finding.Detail);
        Assert.Contains(SyncedAt.ToString("yyyy-MM-dd HH:mm"), finding.Detail);
    }

    [Fact]
    public async Task ASeriesThatWasRemovedSaysSoAboutTheEpisodeItTookWithIt()
    {
        await SeedEntryAsync("link-1");
        AppSettings settings = ProvenLibrary();
        settings.SonarrSnapshots[0].Series.Clear();

        await Health(settings).CheckAllAsync(CancellationToken.None);

        SymlinkFinding finding = Assert.Single((await StoredAsync("link-1")).Findings);
        Assert.Equal("sonarr-episode-missing", finding.Code);
        Assert.Contains($"no series {SeriesId}", finding.Detail);
    }

    [Fact]
    public async Task AnInstanceNobodyEverSynchronizedIsSaidSo()
    {
        await SeedEntryAsync("link-1");
        AppSettings settings = ProvenLibrary();
        settings.SonarrSnapshots = [];

        SymlinkHealthRun run = await Health(settings).CheckAllAsync(CancellationToken.None);

        Assert.Equal(1, run.Undetermined);
        SymlinkFinding finding = Assert.Single((await StoredAsync("link-1")).Findings);
        Assert.Equal("sonarr-unsynced", finding.Code);
    }

    [Fact]
    public async Task TheEpisodeThatIsWatchedAgainIsReported()
    {
        await SeedEntryAsync("link-1");
        AppSettings settings = ProvenLibrary();
        // The pairing is untouched and the file is still there: the only thing that changed is that Sonarr
        // wants this episode again, which is what the person has to decide about.
        EpisodeOf(settings).Monitored = true;

        SymlinkHealthRun run = await Health(settings).CheckAllAsync(CancellationToken.None);

        Assert.Equal(1, run.Reported);
        Assert.Equal(0, run.Undetermined);
        SymlinkFinding finding = Assert.Single((await StoredAsync("link-1")).Findings);
        Assert.Equal("monitoring-reactivated", finding.Code);
    }

    [Fact]
    public async Task TheEpisodeThatCarriesNoFileAnymoreIsReported()
    {
        await SeedEntryAsync("link-1");
        AppSettings settings = ProvenLibrary();
        EpisodeOf(settings).HasFile = false;

        await Health(settings).CheckAllAsync(CancellationToken.None);

        SymlinkFinding finding = Assert.Single((await StoredAsync("link-1")).Findings);
        Assert.Equal("episode-file-missing", finding.Code);
    }

    [Fact]
    public async Task TheNameTheInstanceGaveTheFileIsReportedAgainstTheOneTheEntryLeft()
    {
        await SeedEntryAsync("link-1");
        AppSettings settings = ProvenLibrary();
        string renamed = LinkName.Replace(".mkv", " (1).mkv");
        EpisodeOf(settings).File.Path = SymlinkFixture.SeriesInstance($"{SeriesFolder}/Specials/{renamed}");

        await Health(settings).CheckAllAsync(CancellationToken.None);

        SymlinkFinding finding = Assert.Single((await StoredAsync("link-1")).Findings);
        Assert.Equal("filename-differs", finding.Code);
        Assert.Contains(renamed, finding.Detail);
        Assert.Contains(LinkName, finding.Detail);
    }

    [Fact]
    public async Task AnEntryThatNeverRecordedItsFileInformationIsSaidSo()
    {
        SymlinkLink entry = await SeedEntryAsync("link-1");
        entry.ExpectedMetadata = null;
        Assert.True(await store.UpdateLinkAsync(entry, entry.Revision, CancellationToken.None));

        SymlinkHealthRun run = await Health().CheckAllAsync(CancellationToken.None);

        Assert.Equal(1, run.Undetermined);
        SymlinkFinding finding = Assert.Single((await StoredAsync("link-1")).Findings);
        Assert.Equal("metadata-unconfirmed", finding.Code);
    }

    [Fact]
    public async Task TheSnapshotMeasuringAnotherFileThanTheChosenOneIsReported()
    {
        await SeedEntryAsync("link-1");
        AppSettings settings = ProvenLibrary();
        // The name still matches, so the only answer left is that something else stands behind the link.
        EpisodeOf(settings).File.Size = SymlinkFixture.UpgradedSize;

        await Health(settings).CheckAllAsync(CancellationToken.None);

        SymlinkFinding finding = Assert.Single((await StoredAsync("link-1")).Findings);
        Assert.Equal("metadata-size-deviation", finding.Code);
        Assert.Contains(SymlinkFixture.SourceSize.ToString(), finding.Detail);
    }

    [Fact]
    public async Task TheSnapshotNamingAnotherReleaseGroupThanTheChosenFileIsReported()
    {
        await SeedEntryAsync("link-1");
        AppSettings settings = ProvenLibrary();
        EpisodeOf(settings).File.ReleaseGroup = "ION";

        await Health(settings).CheckAllAsync(CancellationToken.None);

        SymlinkFinding finding = Assert.Single((await StoredAsync("link-1")).Findings);
        Assert.Equal("metadata-release-group-deviation", finding.Code);
        Assert.Contains("WFFF", finding.Detail);
    }

    [Fact]
    public async Task TheSnapshotWithoutTheAudioOfTheChosenFileIsReported()
    {
        await SeedEntryAsync("link-1");
        AppSettings settings = ProvenLibrary();
        EpisodeOf(settings).File.AudioLanguages = ["English"];

        await Health(settings).CheckAllAsync(CancellationToken.None);

        SymlinkFinding finding = Assert.Single((await StoredAsync("link-1")).Findings);
        Assert.Equal("metadata-languages-deviation", finding.Code);
        Assert.Contains("German", finding.Detail);
    }

    [Fact]
    public async Task AComplaintOfItsOwnThatDidNotOccurAgainIsRetired()
    {
        await SeedEntryAsync(
            "link-1",
            findings: [new SymlinkFinding("link-lost", "'somewhere.mkv' is not there anymore.", Start.AddDays(-7))]);

        SymlinkHealthRun run = await Health().CheckAllAsync(CancellationToken.None);

        Assert.Equal(0, run.Reported);
        SymlinkLink stored = await StoredAsync("link-1");
        // The link is back, so the old complaint goes with the pass that did not find it again — and the row
        // is clean enough to carry the stamp of a quiet look.
        Assert.Empty(stored.Findings);
        Assert.Equal(Start, stored.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task AComplaintAnotherServiceWroteIsLeftToItsOwner()
    {
        await SeedEntryAsync(
            "link-1",
            findings: [new SymlinkFinding("renaming-disabled", "The series has renaming enabled.", Start.AddDays(-7))]);

        SymlinkHealthRun run = await Health().CheckAllAsync(CancellationToken.None);

        // The health pass reports the state it looked at and retires its own complaints only.
        SymlinkFinding finding = Assert.Single((await StoredAsync("link-1")).Findings);
        Assert.Equal("renaming-disabled", finding.Code);
        Assert.Null((await StoredAsync("link-1")).LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task TheSingleCheckAnswersTheRowThePassAnswersAndKeepsItsWordOnARetake()
    {
        SymlinkLink entry = await SeedEntryAsync("link-1");
        files.Entries.Remove(entry.LinkPath);

        SymlinkLink? checkedLink = await Health().CheckLinkAsync("link-1", CancellationToken.None);

        Assert.NotNull(checkedLink);
        Assert.Equal(["link-lost"], checkedLink.Findings.Select(finding => finding.Code));
        Assert.Equal(Start, checkedLink.LastCheckedAt);
        // The caller is about to write against this row, so it gets the revision the register holds.
        Assert.Equal(1, checkedLink.Revision);

        SymlinkHealthRun retake = await Health().CheckAllAsync(CancellationToken.None);

        Assert.Equal(1, retake.LinksChecked);
        Assert.Equal(1, retake.Reported);
        SymlinkLink stored = await StoredAsync("link-1");
        // The same look says the same thing once, not twice.
        Assert.Equal(["link-lost"], stored.Findings.Select(finding => finding.Code));
        Assert.Equal(2, stored.Revision);
    }

    [Fact]
    public async Task ARowThatIsNotInTheRegisterHasNoAnswer()
    {
        Assert.Null(await Health().CheckLinkAsync("link-unknown", CancellationToken.None));
    }

    [Fact]
    public async Task ARowThatCannotBeLookedAtDoesNotEndThePass()
    {
        SymlinkLink entry = await SeedEntryAsync("link-1");
        // The second row is one that still owes its first link, so the pass has nothing to say about it and
        // the count below can only be moved by the row that could not be read.
        await SeedEntryAsync(
            "link-2",
            linkName: LinkName.Replace("S00E05", "S00E06"),
            operation: SymlinkOperationStatus.Pending,
            setupStep: SymlinkSetupStep.LinkCreated);
        SymlinkHealthService health = new(
            store,
            new FixedSettings(ProvenLibrary()),
            CreateLinksService(ProvenLibrary()),
            new FailingShelf(files, entry.LinkPath),
            setupRequests,
            clock,
            NullLogger<SymlinkHealthService>.Instance);

        SymlinkHealthRun run = await health.CheckAllAsync(CancellationToken.None);

        // A dying mount answers one entry; the rest of the register is still answered, and the row that could
        // not be read keeps what it had instead of gaining a guess.
        Assert.Equal(2, run.LinksChecked);
        Assert.Equal(1, run.Reported);
        Assert.Equal(1, run.Undetermined);
        SymlinkLink unreadable = await StoredAsync("link-1");
        Assert.Equal("check-failed", Assert.Single(unreadable.Findings).Code);
        Assert.Equal(Start, unreadable.LastCheckedAt);
        Assert.Equal(1, (await StoredAsync("link-2")).Revision);
    }

    /// <summary>The entry as a finished setup leaves it, with its own link and its source standing on the shelf.</summary>
    private async Task<SymlinkLink> SeedEntryAsync(
        string id,
        string linkName = LinkName,
        bool autoFollowUp = true,
        SymlinkOperationStatus operation = SymlinkOperationStatus.Active,
        SymlinkSetupStep? setupStep = SymlinkSetupStep.Completed,
        List<SymlinkFinding>? findings = null)
    {
        string linkPath = Path.Combine(SymlinkTestPaths.LibManagarr($"series/{SeriesFolder}"), "Specials", linkName);
        SymlinkLink link = new()
        {
            Id = id,
            RadarrInstanceId = RadarrInstanceId,
            RadarrMovieId = MovieId,
            RadarrFileId = RadarrFileId,
            SonarrInstanceId = SonarrInstanceId,
            SonarrSeriesId = SeriesId,
            SonarrEpisodeId = EpisodeId,
            SeasonNumber = 0,
            EpisodeNumber = EpisodeId,
            StorageAreaId = StorageAreaId,
            LinkPath = linkPath,
            LinkText = Path.GetRelativePath(Path.GetDirectoryName(linkPath)!, SymlinkFixture.SourcePath()),
            SourcePath = SymlinkFixture.SourcePath(),
            SourceVersion = SymlinkFixture.SourceVersion(RadarrFileId, SymlinkFixture.SourceSize),
            OperationStatus = operation,
            SetupStep = setupStep,
            SonarrFileId = SymlinkFixture.SonarrEpisodeFileId,
            MonitoringDisabled = true,
            AutoFollowUp = autoFollowUp,
            // Which name the entry left behind is the rename step's answer, so an entry that never reached it
            // has none to be compared against.
            AppliedFilename = setupStep is null or SymlinkSetupStep.LinkCreated ? null : linkName,
            ExpectedMetadata = new SymlinkFileMetadata
            {
                Origin = "radarr",
                ReleaseGroup = "WFFF",
                Languages = ["German"],
                Size = SymlinkFixture.SourceSize
            },
            CreatedAt = Start,
            UpdatedAt = Start,
            Findings = findings ?? []
        };
        await store.InsertLinkAsync(link, CancellationToken.None);
        files.Entries[link.LinkPath] = link.LinkText;
        files.Entries[link.SourcePath] = null;
        return await StoredAsync(id);
    }

    /// <summary>The library as the last synchronization left a working installation: the movie with the chosen file and the episode carrying this entry's link.</summary>
    private static AppSettings ProvenLibrary()
    {
        AppSettings settings = SymlinkFixture.SettingsWithLibraryFile(RadarrFileId, SymlinkFixture.SourceSize);
        settings.SonarrSnapshots =
        [
            new SonarrInstanceSnapshot
            {
                InstanceId = SonarrInstanceId,
                InstanceName = "Sonarr",
                SyncedAt = SyncedAt,
                Series =
                [
                    new SonarrSeriesSnapshot
                    {
                        SeriesId = SeriesId,
                        Title = "Kabaneri of the Iron Fortress",
                        Seasons =
                        [
                            new SonarrSeasonSnapshot
                            {
                                SeasonNumber = 0,
                                Name = "Specials",
                                Episodes =
                                [
                                    new SonarrEpisodeSnapshot
                                    {
                                        EpisodeId = EpisodeId,
                                        EpisodeNumber = EpisodeId,
                                        // The entry made this episode available precisely so that it is not watched.
                                        Monitored = false,
                                        HasFile = true,
                                        File = new MediaFileSnapshot
                                        {
                                            Id = SymlinkFixture.SonarrEpisodeFileId,
                                            // The instance names the file in its own view, which is a different grammar
                                            // from the one the register keeps.
                                            Path = SymlinkFixture.SeriesInstance($"{SeriesFolder}/Specials/{LinkName}"),
                                            Size = SymlinkFixture.SourceSize,
                                            ReleaseGroup = "WFFF",
                                            AudioLanguages = ["German"]
                                        }
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        ];
        return settings;
    }

    private static AppSettings ProvenLibraryWithMovieFile(int radarrFileId, long size)
    {
        AppSettings settings = ProvenLibrary();
        settings.RadarrSnapshots[0].Movies[0].File.Id = radarrFileId;
        settings.RadarrSnapshots[0].Movies[0].File.Size = size;
        settings.RadarrSnapshots[0].Movies[0].File.SourceVersion = SymlinkFixture.SourceVersion(radarrFileId, size);
        return settings;
    }

    private static SonarrEpisodeSnapshot EpisodeOf(AppSettings settings) =>
        settings.SonarrSnapshots[0].Series[0].Seasons[0].Episodes[0];

    private SymlinkHealthService Health(AppSettings? settings = null) =>
        new(
            store,
            new FixedSettings(settings ?? ProvenLibrary()),
            CreateLinksService(settings ?? ProvenLibrary()),
            files,
            setupRequests,
            clock,
            NullLogger<SymlinkHealthService>.Instance);

    /// <summary>
    /// The real link service, because queueing a follow-up is left to the code that owns it. Its Arr clients
    /// refuse every request, which is how these tests hold the promise that a pass answers from the snapshots
    /// it is handed instead of asking an instance per row.
    /// </summary>
    private SymlinkService CreateLinksService(AppSettings settings)
    {
        FixedSettings snapshot = new(settings);
        return new SymlinkService(
            store,
            snapshot,
            new MediaPathMappingService(snapshot),
            new SymlinkCapabilityService(store, snapshot, files, clock, NullLogger<SymlinkCapabilityService>.Instance),
            setupRequests,
            new RadarrClient(new HttpClient(new NeverAskedHandler())),
            new SonarrClient(new HttpClient(new NeverAskedHandler())),
            files,
            clock,
            NullLogger<SymlinkService>.Instance);
    }

    /// <summary>The shelf answering one path with a failure no check accounts for, the way a dying mount answers.</summary>
    private sealed class FailingShelf(FakeLinkFileSystem inner, string failsAt) : ISymlinkFileSystem
    {
        public bool EntryExists(string path) => inner.EntryExists(path);

        public bool IsSymlink(string path) =>
            path == failsAt ? throw new IOException($"'{path}' could not be read.") : inner.IsSymlink(path);

        public string? ReadLinkTarget(string path) => inner.ReadLinkTarget(path);

        public void CreateFileSymlink(string linkPath, string targetPath) => inner.CreateFileSymlink(linkPath, targetPath);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public void DeleteEntry(string path) => inner.DeleteEntry(path);

        public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);

        public void OpenForRead(string path) => inner.OpenForRead(path);

        public void WriteAllBytes(string path, byte[] content) => inner.WriteAllBytes(path, content);

        public void MoveEntry(string sourcePath, string destinationPath) => inner.MoveEntry(sourcePath, destinationPath);

        public IReadOnlyList<string> ListEntries(string directoryPath) => inner.ListEntries(directoryPath);
    }

    /// <summary>
    /// Whether a setup pass was asked for. The signal holds one pending request, so one that was written is
    /// read at once while a missing one leaves the wait open — which is what the timeout is for.
    /// </summary>
    private async Task<bool> IsPassRequestedAsync()
    {
        Task signaled = setupRequests.WaitForRequestAsync(CancellationToken.None);
        return await Task.WhenAny(signaled, Task.Delay(TimeSpan.FromSeconds(1))) == signaled;
    }

    private async Task<SymlinkLink> StoredAsync(string id) =>
        await store.GetLinkAsync(id, CancellationToken.None) ?? throw new InvalidOperationException($"Entry {id} is missing.");
}
