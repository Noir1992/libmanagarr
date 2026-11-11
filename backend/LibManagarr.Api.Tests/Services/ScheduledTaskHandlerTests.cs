using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LibManagarr.Api.Clients;
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

    [Fact]
    public async Task ArrCycle_WithoutAnyInstance_SkipsWithOneInfoLine_AndAsksNothing()
    {
        ReadOnlySettings settings = new(new());
        SyncStub sync = new();
        RecordingLogger<ArrDataSyncTaskHandler> log = new();
        ArrDataSyncTaskHandler handler = new(settings, sync, log);
        await handler.ExecuteAsync(default);
        Assert.Empty(sync.Requests);
        (LogLevel level, string message) = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.Equal("Skipping Arr data sync: no Sonarr or Radarr instances are configured.", message);
        Assert.Equal(0, settings.SaveCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ArrCycle_WithOnlyOneServiceConfigured_SynchronizesItWithoutSkipping(bool sonarrOnly)
    {
        AppSettings value = new();
        if (sonarrOnly) value.SonarrInstances = [new() { Id = "s1" }];
        else value.RadarrInstances = [new() { Id = "r1" }];
        ReadOnlySettings settings = new(value);
        SyncStub sync = new();
        RecordingLogger<ArrDataSyncTaskHandler> log = new();
        ArrDataSyncTaskHandler handler = new(settings, sync, log);
        await handler.ExecuteAsync(default);
        Assert.Equal([sonarrOnly ? "s1" : "r1"], sync.Requests.Select(request => request.InstanceId));
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task ArrCycle_IncompleteInstanceConfiguration_IsStillAttempted_NotTreatedAsAnEmptyList()
    {
        // An instance that is configured but cannot be reached (or lacks its URL) stays on the per-instance
        // error path; only a genuinely empty configuration may skip the run.
        ReadOnlySettings settings = new(new() { SonarrInstances = [new() { Id = "s1", BaseUrl = "" }] });
        SyncStub sync = new();
        RecordingLogger<ArrDataSyncTaskHandler> log = new();
        ArrDataSyncTaskHandler handler = new(settings, sync, log);
        await handler.ExecuteAsync(default);
        Assert.Equal(["s1"], sync.Requests.Select(request => request.InstanceId));
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task HealthPass_RunsAndReportsNormally_WhenTheEnvironmentIsProvenAndCurrent()
    {
        InMemorySymlinkStore store = new();
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 11, 3, 0, 0, TimeSpan.Zero));
        FakeLinkFileSystem files = new();
        await SeedCapabilityAsync(store, clock, SymlinkCapabilityStatus.Available);
        RecordingLogger<SymlinkHealthCheckTaskHandler> log = new();
        SymlinkHealthCheckTaskHandler handler = HealthHandler(store, SymlinkConfiguration(true), files, clock, log);
        await handler.ExecuteAsync(default);
        Assert.Contains(log.Entries, entry => entry.Message == "Starting the symlink health pass.");
        Assert.Contains(log.Entries, entry => entry.Message.StartsWith("Symlink health pass finished", StringComparison.Ordinal));
        // A planned run reads the stored answer and never probes the shelf again.
        Assert.Empty(files.CreatedFolders);
        Assert.Empty(files.CreatedLinks);
    }

    [Fact]
    public async Task HealthPass_SkipsWithoutAnyStorageArea_WithOneInfoLine()
    {
        InMemorySymlinkStore store = new();
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 11, 3, 0, 0, TimeSpan.Zero));
        FakeLinkFileSystem files = new();
        AppSettings settings = SymlinkConfiguration(true);
        settings.Symlinks.StorageAreas.Clear();
        RecordingLogger<SymlinkHealthCheckTaskHandler> log = new();
        await HealthHandler(store, settings, files, clock, log).ExecuteAsync(default);
        (LogLevel level, string message) = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.Contains("no storage area is configured", message, StringComparison.Ordinal);
        Assert.Contains(nameof(SymlinkCapabilityStatus.NotTested), message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthPass_SkipsWhenNothingWasEverTested_AndReportsNotTested()
    {
        InMemorySymlinkStore store = new();
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 11, 3, 0, 0, TimeSpan.Zero));
        FakeLinkFileSystem files = new();
        RecordingLogger<SymlinkHealthCheckTaskHandler> log = new();
        await HealthHandler(store, SymlinkConfiguration(true), files, clock, log).ExecuteAsync(default);
        (LogLevel level, string message) = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.Contains("Skipping symlink health check", message, StringComparison.Ordinal);
        Assert.Contains(nameof(SymlinkCapabilityStatus.NotTested), message, StringComparison.Ordinal);
        Assert.Empty(files.CreatedFolders);
    }

    [Theory]
    [InlineData(SymlinkCapabilityStatus.Unsupported)]
    [InlineData(SymlinkCapabilityStatus.PermissionDenied)]
    [InlineData(SymlinkCapabilityStatus.StorageUnavailable)]
    [InlineData(SymlinkCapabilityStatus.Failed)]
    public async Task HealthPass_SkipsAndNamesTheStoredResult_ForEveryProvenUnusableEnvironment(SymlinkCapabilityStatus status)
    {
        InMemorySymlinkStore store = new();
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 11, 3, 0, 0, TimeSpan.Zero));
        FakeLinkFileSystem files = new();
        await SeedCapabilityAsync(store, clock, status, detail: $"probe stopped at 'D:\\series' for status {status}");
        RecordingLogger<SymlinkHealthCheckTaskHandler> log = new();
        await HealthHandler(store, SymlinkConfiguration(true), files, clock, log).ExecuteAsync(default);
        (LogLevel level, string message) = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.Contains($"probe stopped at 'D:\\series' for status {status}", message, StringComparison.Ordinal);
        Assert.Contains(status.ToString(), message, StringComparison.Ordinal);
        Assert.Empty(files.CreatedFolders);
    }

    [Fact]
    public async Task HealthPass_SkipsAndCallsItStale_WhenTheStoredResultPredatesTheConfiguration()
    {
        InMemorySymlinkStore store = new();
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 11, 3, 0, 0, TimeSpan.Zero));
        FakeLinkFileSystem files = new();
        // A link that passed under an older media configuration, and one this process never ran, are both
        // stale: the path moved or the probe belongs to an earlier process.
        await store.SaveCapabilityCheckAsync(new SymlinkCapabilityCheck
        {
            StorageAreaId = "main",
            Status = SymlinkCapabilityStatus.Available,
            Detail = "passed under the old roots",
            CheckedAt = clock.GetUtcNow(),
            ConfigRevision = 3
        }, default);
        RecordingLogger<SymlinkHealthCheckTaskHandler> staleLog = new();
        await HealthHandler(store, SymlinkConfiguration(true), files, clock, staleLog).ExecuteAsync(default);
        (_, string staleMessage) = Assert.Single(staleLog.Entries);
        Assert.Contains("predates the current media configuration", staleMessage, StringComparison.Ordinal);

        InMemorySymlinkStore otherProcess = new();
        await otherProcess.SaveCapabilityCheckAsync(new SymlinkCapabilityCheck
        {
            StorageAreaId = "main",
            ProcessId = "a-process-that-ended",
            Status = SymlinkCapabilityStatus.Available,
            Detail = "passed in an earlier process",
            CheckedAt = clock.GetUtcNow(),
            ConfigRevision = 4
        }, default);
        RecordingLogger<SymlinkHealthCheckTaskHandler> foreignLog = new();
        await HealthHandler(otherProcess, SymlinkConfiguration(true), files, clock, foreignLog).ExecuteAsync(default);
        (_, string foreignMessage) = Assert.Single(foreignLog.Entries);
        Assert.Contains("predates the current media configuration", foreignMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthPass_SkipsASwitchedOffFeature_ButStillNamesTheFailureBehindTheSwitch()
    {
        InMemorySymlinkStore store = new();
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 11, 3, 0, 0, TimeSpan.Zero));
        FakeLinkFileSystem files = new();
        await SeedCapabilityAsync(store, clock, SymlinkCapabilityStatus.PermissionDenied, detail: "Creating a link was refused for lack of a privilege.");
        RecordingLogger<SymlinkHealthCheckTaskHandler> log = new();
        await HealthHandler(store, SymlinkConfiguration(false), files, clock, log).ExecuteAsync(default);
        (_, string message) = Assert.Single(log.Entries);
        Assert.Contains("the symlink feature is switched off", message, StringComparison.Ordinal);
        Assert.Contains("Creating a link was refused", message, StringComparison.Ordinal);
        Assert.Contains(nameof(SymlinkCapabilityStatus.PermissionDenied), message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthPass_SkipsASwitchedOffFeature_ThatWasTurnedOffByHand()
    {
        InMemorySymlinkStore store = new();
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 11, 3, 0, 0, TimeSpan.Zero));
        FakeLinkFileSystem files = new();
        await SeedCapabilityAsync(store, clock, SymlinkCapabilityStatus.Available);
        RecordingLogger<SymlinkHealthCheckTaskHandler> log = new();
        await HealthHandler(store, SymlinkConfiguration(false), files, clock, log).ExecuteAsync(default);
        (LogLevel level, string message) = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.Contains("the symlink feature is switched off", message, StringComparison.Ordinal);
        Assert.Contains(nameof(SymlinkCapabilityStatus.Available), message, StringComparison.Ordinal);
    }

    private static AppSettings SymlinkConfiguration(bool featureEnabled) => new()
    {
        Symlinks = new SymlinkSettings
        {
            Enabled = featureEnabled,
            Revision = 4,
            StorageAreas =
            [
                new MediaStorageArea
                {
                    Id = "main",
                    Name = "Main",
                    MoviesRoot = @"D:\movies",
                    SeriesRoot = @"D:\series"
                }
            ]
        }
    };

    private static Task SeedCapabilityAsync(InMemorySymlinkStore store, FakeTimeProvider clock,
        SymlinkCapabilityStatus status, string? detail = null) =>
        store.SaveCapabilityCheckAsync(new SymlinkCapabilityCheck
        {
            StorageAreaId = "main",
            Status = status,
            Detail = detail,
            CheckedAt = clock.GetUtcNow(),
            ConfigRevision = 4
        }, default);

    /// <summary>
    /// The handler over the real capability and health services, both reading only what the store and the
    /// settings hand them. The Arr clients refuse every request and the file system records every write,
    /// so a test that sees either of them move has caught the scheduled run probing instead of reading.
    /// </summary>
    private static SymlinkHealthCheckTaskHandler HealthHandler(InMemorySymlinkStore store, AppSettings settings,
        FakeLinkFileSystem files, FakeTimeProvider clock, ILogger<SymlinkHealthCheckTaskHandler> log)
    {
        FixedSettings snapshot = new(settings);
        SymlinkCapabilityService capability = new(store, snapshot, files, clock, NullLogger<SymlinkCapabilityService>.Instance);
        SymlinkHealthService health = new(
            store,
            snapshot,
            new SymlinkService(
                store,
                snapshot,
                new MediaPathMappingService(snapshot),
                capability,
                new SymlinkSetupRequests(),
                new RadarrClient(new HttpClient(new NeverAskedHandler())),
                new SonarrClient(new HttpClient(new NeverAskedHandler())),
                files,
                clock,
                NullLogger<SymlinkService>.Instance),
            files,
            new SymlinkSetupRequests(),
            clock,
            NullLogger<SymlinkHealthService>.Instance);
        return new SymlinkHealthCheckTaskHandler(health, snapshot, capability, log);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
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
