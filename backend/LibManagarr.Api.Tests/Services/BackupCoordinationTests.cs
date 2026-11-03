using Microsoft.AspNetCore.Http;
using LibManagarr.Api.Services;
using LibManagarr.Api.Services.Backup;

namespace LibManagarr.Api.Tests.Services;

public sealed class BackupCoordinationTests
{
    [Fact]
    public async Task WritersWaitForTheLease_AndResumeAfterItIsReleased()
    {
        BackupOperationCoordinator coordinator = new(TimeProvider.System);
        using (IWriteScope completed = await coordinator.EnterWriteAsync(CancellationToken.None))
        {
        }

        using IOperationLease lease = await coordinator.PauseWritersAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None);

        Task<IWriteScope> blocked = coordinator.EnterWriteAsync(CancellationToken.None);
        Assert.False(blocked.IsCompleted);

        lease.Dispose();

        using IWriteScope resumed = await blocked.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SecondOperationIsRefusedWhileOneHoldsTheSlot()
    {
        BackupOperationCoordinator coordinator = new(TimeProvider.System);
        using IOperationLease lease = await coordinator.PauseWritersAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None);

        BackupException conflict = await Assert.ThrowsAsync<BackupException>(
            () => coordinator.PauseWritersAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None));

        Assert.Equal(BackupErrorCodes.OperationInProgress, conflict.Code);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task DrainTimeoutAbortsThePause_AndWritersKeepWorking()
    {
        BackupOperationCoordinator coordinator = new(TimeProvider.System);
        IWriteScope running = await coordinator.EnterWriteAsync(CancellationToken.None);

        BackupException notPaused = await Assert.ThrowsAsync<BackupException>(
            () => coordinator.PauseWritersAsync(TimeSpan.FromMilliseconds(50), false, CancellationToken.None));
        Assert.Equal(BackupErrorCodes.WritersNotPaused, notPaused.Code);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, notPaused.StatusCode);
        Assert.False(coordinator.IsInMaintenanceMode);

        running.Dispose();
        using IWriteScope afterFailure = await coordinator.EnterWriteAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MaintenanceModeKeepsWritersOutEvenAfterTheLeaseIsGone()
    {
        BackupOperationCoordinator coordinator = new(TimeProvider.System);
        IOperationLease lease = await coordinator.PauseWritersAsync(TimeSpan.FromSeconds(5), keepPausedAfterRelease: true, CancellationToken.None);
        Assert.True(coordinator.IsInMaintenanceMode);
        lease.Dispose();

        Assert.True(coordinator.IsInMaintenanceMode);
        BackupException restorePending = await Assert.ThrowsAsync<BackupException>(() => coordinator.EnterWriteAsync(CancellationToken.None));
        Assert.Equal(BackupErrorCodes.RestorePending, restorePending.Code);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, restorePending.StatusCode);
    }

    [Fact]
    public async Task NestedWriteScopesNeverWaitBehindTheOuterOne()
    {
        BackupOperationCoordinator coordinator = new(TimeProvider.System);
        IWriteScope outer = await coordinator.EnterWriteAsync(CancellationToken.None);

        Task<IWriteScope> nested = coordinator.EnterWriteAsync(CancellationToken.None);
        Assert.True(nested.IsCompleted);
        (await nested).Dispose();

        // The outer scope alone still counts as one running writer, so a pause has to drain it.
        BackupException notPaused = await Assert.ThrowsAsync<BackupException>(
            () => coordinator.PauseWritersAsync(TimeSpan.FromMilliseconds(50), false, CancellationToken.None));
        Assert.Equal(BackupErrorCodes.WritersNotPaused, notPaused.Code);

        outer.Dispose();
        using IOperationLease drained = await coordinator.PauseWritersAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None);
    }

    [Fact]
    public void StatusService_ReportsPhaseCodeAndChangeTime()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero));
        BackupStatusService status = new(clock);

        Assert.Equal(BackupOperationPhase.Idle, status.Phase);
        Assert.Null(status.Code);

        status.Update(BackupOperationPhase.Exporting);
        Assert.Equal(BackupOperationPhase.Exporting, status.Phase);

        status.Update(BackupOperationPhase.Idle, BackupErrorCodes.InvalidPassword);
        BackupStatusDto snapshot = status.Snapshot();
        Assert.Equal("Idle", snapshot.Phase);
        Assert.Equal(BackupErrorCodes.InvalidPassword, snapshot.Code);
        Assert.Equal(clock.GetUtcNow(), snapshot.ChangedAt);

        status.Update(BackupOperationPhase.RestartPending, operationId: "restore-1");
        Assert.Equal("restore-1", status.Snapshot().OperationId);
        status.Update(BackupOperationPhase.Restored, operationId: "restore-1");
        Assert.Equal("restore-1", status.Snapshot().OperationId);
        status.Update(BackupOperationPhase.Exporting);
        Assert.Null(status.Snapshot().OperationId);
    }

    [Fact]
    public void TicketIsUsableUntilItsLifetimeEnds_AndIsThenOfferedForDeletion()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero));
        BackupImportRegistry registry = new(clock);
        BackupImportRegistry.ImportEntry entry = registry.Add(
            "staging-dir", "state-dir", "archive-sha", 4096, Manifest(), 7, "session-hash");

        clock.Advance(TimeSpan.FromMinutes(14));
        Assert.Equal(entry.ImportId, registry.RequireUsable(entry.ImportId, 7, "session-hash").ImportId);

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(new[] { "staging-dir" }, registry.TakeExpired());

        BackupException unknown = Assert.Throws<BackupException>(() => registry.RequireUsable(entry.ImportId, 7, "session-hash"));
        Assert.Equal(BackupErrorCodes.ImportUnknown, unknown.Code);
        Assert.Equal(StatusCodes.Status404NotFound, unknown.StatusCode);
    }

    [Fact]
    public void TicketBelongsToTheAccountAndSessionThatCreatedIt()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero));
        BackupImportRegistry registry = new(clock);
        BackupImportRegistry.ImportEntry entry = registry.Add(
            "staging-dir", "state-dir", "archive-sha", 4096, Manifest(), 7, "session-hash");

        BackupException otherAccount = Assert.Throws<BackupException>(() => registry.RequireUsable(entry.ImportId, 8, "session-hash"));
        Assert.Equal(BackupErrorCodes.ImportConflict, otherAccount.Code);
        Assert.Equal(StatusCodes.Status403Forbidden, otherAccount.StatusCode);

        BackupException otherSession = Assert.Throws<BackupException>(() => registry.RequireUsable(entry.ImportId, 7, "other-session"));
        Assert.Equal(BackupErrorCodes.ImportConflict, otherSession.Code);
    }

    [Fact]
    public void TicketCanBeConfirmedExactlyOnce()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero));
        BackupImportRegistry registry = new(clock);
        BackupImportRegistry.ImportEntry entry = registry.Add(
            "staging-dir", "state-dir", "archive-sha", 4096, Manifest(), 7, "session-hash");

        Assert.Equal(entry.ImportId, registry.ClaimForApply(entry.ImportId, 7, "session-hash").ImportId);

        BackupException reused = Assert.Throws<BackupException>(() => registry.ClaimForApply(entry.ImportId, 7, "session-hash"));
        Assert.Equal(BackupErrorCodes.ImportConflict, reused.Code);
        Assert.Equal(StatusCodes.Status409Conflict, reused.StatusCode);
    }

    [Fact]
    public void RemovingATicketMakesItUnknown_AndExpiresItOutOfTheDeletionSweep()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero));
        BackupImportRegistry registry = new(clock);
        BackupImportRegistry.ImportEntry entry = registry.Add(
            "staging-dir", "state-dir", "archive-sha", 4096, Manifest(), 7, "session-hash");

        registry.Remove(entry.ImportId);

        clock.Advance(BackupImportRegistry.ImportLifetime);
        Assert.Empty(registry.TakeExpired());
        Assert.Throws<BackupException>(() => registry.RequireUsable(entry.ImportId, 7, "session-hash"));
    }

    [Fact]
    public void JournalSurvivesARecreatedStore_AndADamagedFileIsIgnored()
    {
        TestStorageEnvironment environment = new();
        try
        {
            StoragePaths paths = new(environment, configDirectory: null);
            RestoreJournalStore store = new(paths);
            Assert.Null(store.Read());

            RestoreJournal journal = new()
            {
                OperationId = "op-1",
                State = RestoreJournalState.Prepared,
                PreparedAt = new DateTimeOffset(2026, 10, 31, 9, 30, 0, TimeSpan.Zero),
                ArchiveSha256 = "a1b2c3",
                StagingDirectory = Path.Combine(paths.BackupsDirectory, "import-1", "state"),
                RollbackDirectory = Path.Combine(paths.BackupsDirectory, "rollback-op-1"),
                Components =
                [
                    new RestoreComponent { SourcePath = "state/settings.json", TargetPath = paths.SettingsFilePath },
                    new RestoreComponent { SourcePath = "state/data-protection-keys", TargetPath = paths.DataProtectionKeysDirectory, IsDirectory = true }
                ]
            };
            store.Write(journal);

            RestoreJournal reloaded = new RestoreJournalStore(paths).Read()!;
            Assert.Equal("op-1", reloaded.OperationId);
            Assert.Equal(RestoreJournalState.Prepared, reloaded.State);
            Assert.Equal(journal.PreparedAt, reloaded.PreparedAt);
            Assert.Equal("a1b2c3", reloaded.ArchiveSha256);
            Assert.Equal(journal.StagingDirectory, reloaded.StagingDirectory);
            Assert.Equal(journal.RollbackDirectory, reloaded.RollbackDirectory);
            Assert.Equal(2, reloaded.Components.Count);
            Assert.True(reloaded.Components[1].IsDirectory);
            Assert.Equal(paths.DataProtectionKeysDirectory, reloaded.Components[1].TargetPath);

            File.WriteAllText(store.JournalPath, "{ torn write");
            Assert.Null(store.Read());
        }
        finally
        {
            environment.Dispose();
        }
    }

    private static BackupManifest Manifest() => new()
    {
        FormatVersion = BackupArchiveFormat.FormatVersion,
        ApplicationVersion = "1.2.3",
        CreatedAt = new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero),
        LibrarySchemaVersion = 3,
        Entries = [new BackupManifestEntry { Path = "libmanagarr.db", SizeBytes = 4096, Sha256 = "aa" }]
    };
}
