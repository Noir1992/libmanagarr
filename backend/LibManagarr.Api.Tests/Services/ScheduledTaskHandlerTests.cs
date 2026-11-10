using Microsoft.Extensions.Logging.Abstractions;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class ScheduledTaskHandlerTests
{
    [Fact]
    public async Task ArrCycle_ContinuesAfterInstanceFailure_WithoutSavingGeneralSettings()
    {
        ReadOnlySettings settings = new(new()
        {
            SonarrInstances = [new() { Id = "s1" }, new() { Id = "s2" }],
            RadarrInstances = [new() { Id = "r1" }]
        });
        SyncStub sync = new() { FailFirstInstance = true };
        ArrDataSyncTaskHandler handler = new(settings, sync, NullLogger<ArrDataSyncTaskHandler>.Instance);
        await handler.ExecuteAsync(default);
        Assert.Equal(["s1", "s2", "r1"], sync.Requests.Select(request => request.InstanceId));
        Assert.Equal([ArrApplicationKind.Sonarr, ArrApplicationKind.Sonarr, ArrApplicationKind.Radarr], sync.Requests.Select(request => request.Kind));
        Assert.Equal(0, settings.SaveCalls);
    }

    [Fact]
    public async Task ArrCycle_PropagatesCancellation_InsteadOfContinuingToOtherInstances()
    {
        ReadOnlySettings settings = new(new() { SonarrInstances = [new() { Id = "s1" }, new() { Id = "s2" }] });
        SyncStub sync = new();
        ArrDataSyncTaskHandler handler = new(settings, sync, NullLogger<ArrDataSyncTaskHandler>.Instance);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.ExecuteAsync(cancellation.Token));
        Assert.Single(sync.Requests);
        Assert.Equal(0, settings.SaveCalls);
    }

    [Fact]
    public async Task Cleanup_LoadsConfiguredRetentionBeforeAnySettingsApiRequest_WithoutSavingSettings()
    {
        using TestStorageEnvironment environment = new();
        StoragePaths paths = new(environment, null);
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
        LogFileService logs = new(clock, paths);
        string directory = Path.Combine(paths.ConfigDirectory, "logs");
        Directory.CreateDirectory(directory);
        string expired = Path.Combine(directory, "2026-10-19.log");
        string today = Path.Combine(directory, "2026-10-21.log");
        await File.WriteAllTextAsync(expired, "old");
        await File.WriteAllTextAsync(today, "today");
        ReadOnlySettings settings = new(new() { Logging = new() { RetentionDays = 1 } });
        LogCleanupTaskHandler handler = new(logs, settings, NullLogger<LogCleanupTaskHandler>.Instance);
        await handler.ExecuteAsync(default);
        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(today));
        Assert.Equal(0, settings.SaveCalls);
    }

    private sealed class ReadOnlySettings(AppSettings value) : ISettingsService
    {
        public int SaveCalls { get; private set; }
        public Task<AppSettings> GetAsync(CancellationToken token) => Task.FromResult(value);
        public Task<AppSettings> SaveAsync(AppSettings settings, CancellationToken token)
        {
            SaveCalls++;
            throw new InvalidOperationException("Task handlers must not save general settings.");
        }
    }

    private sealed class SyncStub : ISnapshotSyncService
    {
        public Task<SyncInstanceResult> SyncSonarrSeriesAsync(string instanceId, int seriesId, CancellationToken token) =>
            throw new NotSupportedException("A scheduled sync must refresh the whole instance.");

        public bool FailFirstInstance { get; init; }
        public List<SyncInstanceRequest> Requests { get; } = [];
        public Task<SyncInstanceResult> SyncAsync(SyncInstanceRequest request, CancellationToken token)
        {
            Requests.Add(request);
            token.ThrowIfCancellationRequested();
            if (FailFirstInstance && Requests.Count == 1) throw new InvalidOperationException("Instance unavailable");
            return Task.FromResult(new SyncInstanceResult());
        }
    }
}
