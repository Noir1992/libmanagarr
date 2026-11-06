using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using LibManagarr.Api.Services.Backup;

namespace LibManagarr.Api.Tests.Services;

public sealed class SqliteSymlinkStoreTests : IDisposable
{
    private readonly TestStorageEnvironment environment = new();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));

    private StoragePaths Paths => new(environment, null);
    private string SettingsFile => Path.Combine(Paths.ConfigDirectory, "settings.json");

    private SqliteSymlinkStore CreateStore() =>
        new(Paths, clock, new BackupOperationCoordinator(clock), NullLogger<SqliteSymlinkStore>.Instance);

    [Fact]
    public async Task NewInstallation_CreatesTheTablesAndStartsEmpty()
    {
        SqliteSymlinkStore store = CreateStore();

        Assert.Empty(await store.GetLinksAsync(CancellationToken.None));
        Assert.Empty(await store.GetSuggestionsAsync(CancellationToken.None));
        Assert.Empty(await store.GetCapabilityChecksAsync(CancellationToken.None));
        Assert.Empty(await store.GetRejectionsAsync(CancellationToken.None));
        Assert.Empty(await store.GetMatchingStatesAsync(CancellationToken.None));
        // The feature owns no settings of its own beyond the section in settings.json, so an unused
        // installation must not gain a settings file from this code path.
        Assert.False(File.Exists(SettingsFile));

        await using SqliteConnection connection = new($"Data Source={Paths.DatabasePath}");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT migration_id FROM schema_migrations ORDER BY migration_id";
        Assert.Equal(
            ["symlinks-v1", "symlinks-v2", "symlinks-v3"],
            await ReadAllAsync(command));
    }

    [Fact]
    public async Task Migration_ASecondInstallationAlreadyOnV1AddsOnlyTheMissingStep()
    {
        await ExecuteSqlAsync(
            "CREATE TABLE schema_migrations (migration_id TEXT PRIMARY KEY, applied_at TEXT NOT NULL);" +
            "INSERT INTO schema_migrations VALUES ('symlinks-v1','2026-11-05T09:00:00.000+00:00');" +
            // The marked step is skipped, so the table it created has to be here for the step that
            // widens it — a marked step whose table was never made is not a state an upgrade meets.
            "CREATE TABLE symlink_links (id TEXT PRIMARY KEY);");

        SqliteSymlinkStore store = CreateStore();
        await store.SaveMatchingStateAsync(NewMatchingState("sonarr-1"), CancellationToken.None);

        await using SqliteConnection connection = new($"Data Source={Paths.DatabasePath}");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT migration_id FROM schema_migrations ORDER BY migration_id";
        Assert.Equal(["symlinks-v1", "symlinks-v2", "symlinks-v3"], await ReadAllAsync(command));
        // A step is marked once, so reopening an installed database does not replay it — which for the
        // widening step matters more than for a create, because the second ALTER would fail outright.
        await CreateStore().InitializeAsync(CancellationToken.None);
        using SqliteCommand counted = connection.CreateCommand();
        counted.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE migration_id IN ('symlinks-v2','symlinks-v3')";
        Assert.Equal(2L, await counted.ExecuteScalarAsync());
        Assert.NotNull(await store.GetMatchingStateAsync("sonarr-1", CancellationToken.None));
    }

    [Fact]
    public async Task Migration_OnAWriteAheadLogInstallation_CreatesTheTablesAndKeepsWAL()
    {
        // A running installation is in WAL mode before these tables appear, which is the state the
        // migration has to survive; a fresh DELETE-mode file would prove less than it looks like.
        await ExecuteSqlAsync("PRAGMA journal_mode=WAL;");
        await CreateStore().InitializeAsync(CancellationToken.None);
        await CreateStore().SaveCapabilityCheckAsync(Check("area-1", SymlinkCapabilityStatus.Available, 3), CancellationToken.None);

        await ExecuteSqlAsync("PRAGMA journal_mode=WAL;");
        SqliteSymlinkStore reopened = CreateStore();
        await reopened.InitializeAsync(CancellationToken.None);

        SymlinkCapabilityCheck stored = (await reopened.GetCapabilityCheckAsync("area-1", CancellationToken.None))!;
        Assert.Equal(SymlinkCapabilityStatus.Available, stored.Status);
        await using SqliteConnection connection = new($"Data Source={Paths.DatabasePath}");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        Assert.Equal("wal", await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task CapabilityCheck_KeepsOneAnswerPerAreaWithItsRevision()
    {
        SqliteSymlinkStore store = CreateStore();
        await store.SaveCapabilityCheckAsync(Check("area-1", SymlinkCapabilityStatus.PermissionDenied, 1), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(5));
        await store.SaveCapabilityCheckAsync(Check("area-2", SymlinkCapabilityStatus.Available, 2), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(5));
        await store.SaveCapabilityCheckAsync(Check("area-1", SymlinkCapabilityStatus.Available, 4), CancellationToken.None);

        IReadOnlyList<SymlinkCapabilityCheck> checks = await store.GetCapabilityChecksAsync(CancellationToken.None);
        Assert.Equal(2, checks.Count);
        SymlinkCapabilityCheck area1 = checks.Single(check => check.StorageAreaId == "area-1");
        Assert.Equal(SymlinkCapabilityStatus.Available, area1.Status);
        Assert.Equal(4, area1.ConfigRevision);
        Assert.Equal(clock.GetUtcNow(), area1.CheckedAt);
        Assert.True(area1.IsCurrent(4));
        Assert.False(area1.IsCurrent(5));
        Assert.Null(await store.GetCapabilityCheckAsync("area-3", CancellationToken.None));
    }

    [Fact]
    public async Task Link_RoundTripsEveryFieldIncludingItsMetadataAndFindings()
    {
        SqliteSymlinkStore store = CreateStore();
        SymlinkLink link = NewLink("link-1");
        link.SetupStep = SymlinkSetupStep.MetadataMatched;
        link.MonitoringOriginal = true;
        link.MonitoringDisabled = true;
        link.AutoFollowUp = true;
        link.SonarrFileId = 55;
        link.SonarrScanCommandId = "cmd-7";
        link.DesiredFilename = "Writing - S00E01 - The One.mkv";
        link.AppliedFilename = "Writing - S00E01 - The One.mkv";
        link.NamingRevision = "naming-3";
        link.SourceVersion = "radarr-episode-92";
        link.UpdateRevision = 6;
        link.ExpectedMetadata = new SymlinkFileMetadata
        {
            Origin = "radarr",
            ReleaseGroup = "NTb",
            Languages = ["de", "en"],
            Quality = "WEBDL-1080p",
            Size = 1_832_402_112
        };
        link.ConfirmedMetadata = new SymlinkFileMetadata { Origin = "sonarr", Quality = "WEBDL-1080p" };
        link.LastCheckedAt = clock.GetUtcNow();
        link.LastSuccessfulCheckAt = clock.GetUtcNow();
        link.Findings = [new SymlinkFinding("stale-source", "The Radarr file was replaced.", clock.GetUtcNow())];
        await store.InsertLinkAsync(link, CancellationToken.None);

        SymlinkLink stored = (await store.GetLinkAsync("link-1", CancellationToken.None))!;
        Assert.Equal(link.Id, stored.Id);
        Assert.Equal(link.Revision, stored.Revision);
        Assert.Equal(link.RadarrInstanceId, stored.RadarrInstanceId);
        Assert.Equal(link.RadarrMovieId, stored.RadarrMovieId);
        Assert.Equal(link.RadarrFileId, stored.RadarrFileId);
        Assert.Equal(link.SonarrInstanceId, stored.SonarrInstanceId);
        Assert.Equal(link.SonarrSeriesId, stored.SonarrSeriesId);
        Assert.Equal(link.SonarrEpisodeId, stored.SonarrEpisodeId);
        Assert.Equal(link.SeasonNumber, stored.SeasonNumber);
        Assert.Equal(link.EpisodeNumber, stored.EpisodeNumber);
        Assert.Equal(link.StorageAreaId, stored.StorageAreaId);
        Assert.Equal(link.LinkPath, stored.LinkPath);
        Assert.Equal(link.LinkText, stored.LinkText);
        Assert.Equal(link.SourcePath, stored.SourcePath);
        Assert.Equal(SymlinkOperationStatus.Active, stored.OperationStatus);
        Assert.Equal(SymlinkSetupStep.MetadataMatched, stored.SetupStep);
        Assert.Equal("suggestion-1", stored.SuggestionId);
        Assert.Equal(55, stored.SonarrFileId);
        Assert.Equal("cmd-7", stored.SonarrScanCommandId);
        Assert.True(stored.MonitoringOriginal);
        Assert.True(stored.MonitoringDisabled);
        Assert.True(stored.AutoFollowUp);
        Assert.Equal(link.DesiredFilename, stored.DesiredFilename);
        Assert.Equal(link.AppliedFilename, stored.AppliedFilename);
        Assert.Equal(link.NamingRevision, stored.NamingRevision);
        Assert.Equal(link.SourceVersion, stored.SourceVersion);
        Assert.Equal(6, stored.UpdateRevision);
        Assert.Equal("radarr", stored.ExpectedMetadata!.Origin);
        Assert.Equal(["de", "en"], stored.ExpectedMetadata!.Languages);
        Assert.Equal(1_832_402_112L, stored.ExpectedMetadata!.Size);
        Assert.Equal("sonarr", stored.ConfirmedMetadata!.Origin);
        Assert.Equal(link.CreatedAt, stored.CreatedAt);
        Assert.Equal(link.LastCheckedAt, stored.LastCheckedAt);
        Assert.Equal(link.LastSuccessfulCheckAt, stored.LastSuccessfulCheckAt);
        SymlinkFinding finding = Assert.Single(stored.Findings);
        Assert.Equal("stale-source", finding.Code);
        Assert.Equal("The Radarr file was replaced.", finding.Detail);

        // A row that never touched those columns has to come back empty rather than as broken JSON.
        SymlinkLink bare = NewLink("link-2");
        bare.SonarrEpisodeId = 2_000;
        bare.Findings = [];
        await store.InsertLinkAsync(bare, CancellationToken.None);
        SymlinkLink storedBare = (await store.GetLinkAsync("link-2", CancellationToken.None))!;
        Assert.Null(storedBare.SetupStep);
        Assert.Null(storedBare.MonitoringOriginal);
        Assert.Null(storedBare.ExpectedMetadata);
        Assert.Null(storedBare.LastCheckedAt);
        Assert.Empty(storedBare.Findings);
    }

    [Fact]
    public async Task Link_KeepsTheRenameCommandItWaitsForAcrossItsWrites()
    {
        // The pass that resumes between queueing the rename and its end reads the id back to decide
        // whether to poll or queue again, so the column has to survive being written and cleared.
        SqliteSymlinkStore store = CreateStore();
        SymlinkLink link = NewLink("link-1");
        link.SetupStep = SymlinkSetupStep.Renamed;
        link.SonarrScanCommandId = "cmd-91";
        await store.InsertLinkAsync(link, CancellationToken.None);

        SymlinkLink queued = (await store.GetLinkAsync("link-1", CancellationToken.None))!;
        Assert.Null(queued.SonarrRenameCommandId);

        queued.SonarrRenameCommandId = "cmd-92";
        Assert.True(await store.UpdateLinkAsync(queued, queued.Revision, CancellationToken.None));
        SymlinkLink renamed = (await store.GetLinkAsync("link-1", CancellationToken.None))!;
        Assert.Equal("cmd-92", renamed.SonarrRenameCommandId);
        Assert.Equal("cmd-91", renamed.SonarrScanCommandId);

        // An empty id is a state of its own: the pass that saw the rename finish has to be able to
        // store it, or every later pass would wait for a command that is long gone.
        renamed.SetupStep = SymlinkSetupStep.Completed;
        renamed.SonarrRenameCommandId = null;
        Assert.True(await store.UpdateLinkAsync(renamed, renamed.Revision, CancellationToken.None));
        SymlinkLink finished = (await store.GetLinkAsync("link-1", CancellationToken.None))!;
        Assert.Null(finished.SonarrRenameCommandId);
        Assert.Equal(SymlinkSetupStep.Completed, finished.SetupStep);
    }

    [Fact]
    public async Task Link_UpdateAndDeleteRefuseAStaleRevisionAndAdvanceTheirOwn()
    {
        SqliteSymlinkStore store = CreateStore();
        await store.InsertLinkAsync(NewLink("link-1"), CancellationToken.None);
        SymlinkLink stored = (await store.GetLinkAsync("link-1", CancellationToken.None))!;

        stored.OperationStatus = SymlinkOperationStatus.Conflict;
        Assert.False(await store.UpdateLinkAsync(stored, expectedRevision: 99, CancellationToken.None));
        Assert.True(await store.UpdateLinkAsync(stored, stored.Revision, CancellationToken.None));
        SymlinkLink updated = (await store.GetLinkAsync("link-1", CancellationToken.None))!;
        Assert.Equal(SymlinkOperationStatus.Conflict, updated.OperationStatus);
        Assert.Equal(stored.Revision + 1, updated.Revision);

        Assert.False(await store.DeleteLinkAsync("link-1", stored.Revision, CancellationToken.None));
        Assert.True(await store.DeleteLinkAsync("link-1", updated.Revision, CancellationToken.None));
        Assert.Null(await store.GetLinkAsync("link-1", CancellationToken.None));
    }

    [Fact]
    public async Task Link_KeepsOneActiveEntryPerEpisodeAndAllowsTheOnePendingDeletion()
    {
        SqliteSymlinkStore store = CreateStore();
        await store.InsertLinkAsync(NewLink("link-1"), CancellationToken.None);

        SymlinkLink duplicate = NewLink("link-2");
        await Assert.ThrowsAnyAsync<SqliteException>(() => store.InsertLinkAsync(duplicate, CancellationToken.None));

        // A row that only records a deletion still owed must not be blocked by the guard, or an
        // interrupted removal could never be finished.
        SymlinkLink deletion = NewLink("link-3");
        deletion.OperationStatus = SymlinkOperationStatus.DeletePending;
        await store.InsertLinkAsync(deletion, CancellationToken.None);
        Assert.Equal(2, (await store.GetLinksAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Suggestion_KeepsOneActiveRowPerPairAcrossSyncRounds()
    {
        SqliteSymlinkStore store = CreateStore();
        SymlinkSuggestion first = NewSuggestion("suggestion-1");
        first.Reasons = [new SymlinkSuggestionReason("identical-title", "The special and the movie have the same title.")];
        await store.UpsertSuggestionAsync(first, CancellationToken.None);

        clock.Advance(TimeSpan.FromHours(2));
        SymlinkSuggestion second = NewSuggestion("suggestion-ignored");
        second.Reasons =
        [
            new SymlinkSuggestionReason("identical-title", "The special and the movie have the same title."),
            new SymlinkSuggestionReason("release-year-match", "Both are dated 2019.")
        ];
        second.RadarrFileId = 4321;
        await store.UpsertSuggestionAsync(second, CancellationToken.None);

        SymlinkSuggestion stored = Assert.Single(await store.GetSuggestionsAsync(CancellationToken.None));
        // The second round updated the row the user is looking at instead of adding a second decision,
        // and it kept the id and the first sighting the history refers to.
        Assert.Equal("suggestion-1", stored.Id);
        // A reason keeps its code and its sentence, so the interface can label it and the log can still
        // be read after the wording changed.
        Assert.Equal(["identical-title", "release-year-match"], stored.Reasons.Select(reason => reason.Code));
        Assert.Equal("Both are dated 2019.", stored.Reasons[1].Detail);
        Assert.Equal(4321, stored.RadarrFileId);
        Assert.Equal(first.CreatedAt, stored.CreatedAt);
        Assert.Equal(clock.GetUtcNow(), stored.UpdatedAt);
        Assert.Equal(1, stored.Revision);
        Assert.Equal(stored.Id, (await store.FindActiveSuggestionAsync(first.Pair, CancellationToken.None))!.Id);
    }

    [Fact]
    public async Task Suggestion_AllowsHistoryRowsOnceThePairIsDecided()
    {
        SqliteSymlinkStore store = CreateStore();
        SymlinkSuggestion pending = NewSuggestion("suggestion-1");
        await store.UpsertSuggestionAsync(pending, CancellationToken.None);
        pending.Status = SymlinkSuggestionStatus.Completed;
        Assert.True(await store.UpdateSuggestionAsync(pending, pending.Revision, CancellationToken.None));

        // A later movie upgrade has to be reportable again for the same pair; the completed row stays as
        // history next to it.
        SymlinkSuggestion followUp = NewSuggestion("suggestion-2");
        followUp.RadarrFileId = 99;
        await store.UpsertSuggestionAsync(followUp, CancellationToken.None);

        Assert.Equal(2, (await store.GetSuggestionsAsync(CancellationToken.None)).Count);
        Assert.Equal("suggestion-2", (await store.GetSuggestionAsync("suggestion-2", CancellationToken.None))!.Id);
        // The pair is reportable again, so the follow-up — not the completed row — is what a sync finds.
        Assert.Equal("suggestion-2", (await store.FindActiveSuggestionAsync(pending.Pair, CancellationToken.None))!.Id);
    }

    [Fact]
    public async Task Suggestion_UpdateRefusesAStaleRevisionAndCarriesTheAlternatives()
    {
        SqliteSymlinkStore store = CreateStore();
        SymlinkSuggestion suggestion = NewSuggestion("suggestion-1");
        suggestion.Alternatives = [new SymlinkSuggestionAlternative("radarr-1", 12, 34, "same title, other year")];
        suggestion.Detail = "two sources matched";
        await store.UpsertSuggestionAsync(suggestion, CancellationToken.None);

        SymlinkSuggestion stored = (await store.GetSuggestionAsync("suggestion-1", CancellationToken.None))!;
        SymlinkSuggestionAlternative alternative = Assert.Single(stored.Alternatives);
        Assert.Equal("radarr-1", alternative.RadarrInstanceId);
        Assert.Equal(34, alternative.RadarrFileId);
        Assert.Equal("same title, other year", alternative.Detail);
        Assert.Equal("two sources matched", stored.Detail);

        Assert.False(await store.UpdateSuggestionAsync(stored, stored.Revision + 5, CancellationToken.None));
        Assert.True(await store.UpdateSuggestionAsync(stored, stored.Revision, CancellationToken.None));
        Assert.Equal(stored.Revision + 1, (await store.GetSuggestionAsync("suggestion-1", CancellationToken.None))!.Revision);
    }

    [Fact]
    public async Task Rejection_KeysOnThePairAndKeepsTheFirstDecline()
    {
        SqliteSymlinkStore store = CreateStore();
        SpecialMoviePair pair = new("radarr-1", 12, "sonarr-1", 34, 56);
        Assert.False(await store.IsRejectedAsync(pair, CancellationToken.None));

        await store.RejectAsync(pair, "wrong release group", clock.GetUtcNow(), CancellationToken.None);
        clock.Advance(TimeSpan.FromDays(1));
        await store.RejectAsync(pair, "declined again", clock.GetUtcNow(), CancellationToken.None);

        SymlinkRejection rejection = Assert.Single(await store.GetRejectionsAsync(CancellationToken.None));
        Assert.Equal(pair, rejection.Pair);
        Assert.Equal("wrong release group", rejection.Detail);
        Assert.Equal(clock.GetUtcNow() - TimeSpan.FromDays(1), rejection.CreatedAt);
        Assert.True(await store.IsRejectedAsync(pair, CancellationToken.None));
        Assert.False(await store.IsRejectedAsync(pair with { SonarrEpisodeId = 57 }, CancellationToken.None));

        Assert.True(await store.RemoveRejectionAsync(pair, CancellationToken.None));
        Assert.False(await store.RemoveRejectionAsync(pair, CancellationToken.None));
        Assert.False(await store.IsRejectedAsync(pair, CancellationToken.None));
    }

    [Fact]
    public async Task MatchingState_RoundTripsTheSignatureItsVersionAndTheCheckedMoment()
    {
        SqliteSymlinkStore store = CreateStore();
        Assert.Null(await store.GetMatchingStateAsync("sonarr-1", CancellationToken.None));

        await store.SaveMatchingStateAsync(NewMatchingState("sonarr-1"), CancellationToken.None);
        await store.SaveMatchingStateAsync(NewMatchingState("radarr-1", ArrApplicationKind.Radarr), CancellationToken.None);

        SymlinkMatchingState state = (await store.GetMatchingStateAsync("sonarr-1", CancellationToken.None))!;
        Assert.Equal("sonarr-1", state.InstanceId);
        Assert.Equal(ArrApplicationKind.Sonarr, state.Kind);
        Assert.Equal("signature-of-that-pass", state.Signature);
        Assert.Equal(1, state.MatcherVersion);
        Assert.Equal(4, state.ConfigRevision);
        Assert.Equal(clock.GetUtcNow(), state.CheckedAt);
        Assert.Equal(["radarr-1", "sonarr-1"], (await store.GetMatchingStatesAsync(CancellationToken.None)).Select(saved => saved.InstanceId));
    }

    [Fact]
    public async Task MatchingState_KeepsOneRowPerInstanceAndReplacesWhatTheLastPassSaw()
    {
        SqliteSymlinkStore store = CreateStore();
        await store.SaveMatchingStateAsync(NewMatchingState("sonarr-1"), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(5));
        await store.SaveMatchingStateAsync(NewMatchingState("sonarr-1", signature: "a-newer-pass", configRevision: 5), CancellationToken.None);

        SymlinkMatchingState stored = Assert.Single(await store.GetMatchingStatesAsync(CancellationToken.None));
        // One row per instance is what lets an aborted pass stay overdue: the previous signature is gone
        // only when a pass actually finished, and the instance keeps its own row across rescans.
        Assert.Equal("a-newer-pass", stored.Signature);
        Assert.Equal(5, stored.ConfigRevision);
        Assert.Equal(clock.GetUtcNow(), stored.CheckedAt);
    }

    [Fact]
    public async Task Rejection_AndSuggestionCoexistWithTheTaskTablesInTheSameDatabase()
    {
        // The feature shares libmanagarr.db with the scheduled tasks and the library history, so a
        // migration that only works on a private file would not be enough.
        SqliteScheduledTaskStore tasks = new(
            Paths,
            ScheduledTaskStoreTests.CreateCatalog(),
            clock,
            new BackupOperationCoordinator(clock),
            NullLogger<SqliteScheduledTaskStore>.Instance);
        Assert.True((await tasks.GetAllAsync(CancellationToken.None)).Count > 0);

        SqliteSymlinkStore store = CreateStore();
        await store.InsertLinkAsync(NewLink("link-1"), CancellationToken.None);
        await store.RejectAsync(new SpecialMoviePair("radarr-1", 1, "sonarr-1", 2, 3), null, clock.GetUtcNow(), CancellationToken.None);

        Assert.Single(await store.GetLinksAsync(CancellationToken.None));
        Assert.True((await tasks.GetAllAsync(CancellationToken.None)).Count > 0);
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        Directory.CreateDirectory(Paths.ConfigDirectory);
        await using SqliteConnection connection = new($"Data Source={Paths.DatabasePath}");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private SymlinkCapabilityCheck Check(string areaId, SymlinkCapabilityStatus status, int revision) => new()
    {
        StorageAreaId = areaId,
        Status = status,
        Detail = $"{status} for {areaId}",
        // The probe passes its own moment in, so a later round has to be visibly newer than the first.
        CheckedAt = clock.GetUtcNow(),
        ConfigRevision = revision
    };

    private SymlinkLink NewLink(string id) => new()
    {
        Id = id,
        Revision = 0,
        RadarrInstanceId = "radarr-1",
        RadarrMovieId = 12,
        RadarrFileId = 34,
        SonarrInstanceId = "sonarr-1",
        SonarrSeriesId = 5,
        SonarrEpisodeId = 56,
        SeasonNumber = 0,
        EpisodeNumber = 3,
        StorageAreaId = "area-1",
        LinkPath = "/media/series/Writing/Writing S00E03.mkv",
        LinkText = "../../../movies/Film (2010)/Film (2010) [1080p].mkv",
        SourcePath = "/media/movies/Film (2010)/Film (2010) [1080p].mkv",
        OperationStatus = SymlinkOperationStatus.Active,
        SuggestionId = "suggestion-1",
        CreatedAt = clock.GetUtcNow(),
        UpdatedAt = clock.GetUtcNow()
    };

    private SymlinkMatchingState NewMatchingState(
        string instanceId,
        ArrApplicationKind kind = ArrApplicationKind.Sonarr,
        string signature = "signature-of-that-pass",
        int configRevision = 4) => new()
    {
        InstanceId = instanceId,
        Kind = kind,
        Signature = signature,
        MatcherVersion = SpecialMovieMatchingRules.MatcherVersion,
        ConfigRevision = configRevision,
        CheckedAt = clock.GetUtcNow()
    };

    private static async Task<string[]> ReadAllAsync(SqliteCommand command)
    {
        List<string> values = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return [.. values];
    }

    private SymlinkSuggestion NewSuggestion(string id) => new()
    {
        Id = id,
        MatcherVersion = "v1",
        Status = SymlinkSuggestionStatus.Pending,
        Pair = new SpecialMoviePair("radarr-1", 12, "sonarr-1", 34, 56),
        RadarrFileId = 1234,
        SeasonNumber = 0,
        EpisodeNumber = 3,
        Reasons = [new SymlinkSuggestionReason("identical-title", "The special and the movie have the same title.")],
        ConfigRevision = 2,
        CreatedAt = clock.GetUtcNow(),
        UpdatedAt = clock.GetUtcNow()
    };

    public void Dispose()
    {
        // Every connection the store used goes back to the pool instead of closing, and a pooled reader
        // keeps the database file held, so the temporary tree could not be removed without this.
        SqliteConnection.ClearAllPools();
        environment.Dispose();
    }
}
