using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using LibManagarr.Api.Services.Backup;

namespace LibManagarr.Api.Tests.Services;

public sealed class ScheduledTaskExecutionTests : IDisposable
{
    private readonly TestStorageEnvironment environment = new();
    private readonly SchedulerClock clock = new(new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero));
    private readonly TestHandler sync = new("arrDataSync");
    private readonly TestHandler cleanup = new("cleanup");
    private readonly TestLifetime lifetime = new();
    private readonly ServiceProvider provider;
    private readonly SqliteScheduledTaskStore store;
    private readonly ScheduledTaskService tasks;
    private readonly ScheduledTaskExecutionService execution;

    public ScheduledTaskExecutionTests()
    {
        var catalog = ScheduledTaskStoreTests.CreateCatalog();
        BackupOperationCoordinator writes = new(clock);
        store = new(new StoragePaths(environment, null), catalog, clock, writes, NullLogger<SqliteScheduledTaskStore>.Instance);
        tasks = new(store, catalog, clock);
        ServiceCollection services = new();
        services.AddScoped<IScheduledTaskHandler>(_ => sync);
        services.AddScoped<IScheduledTaskHandler>(_ => cleanup);
        provider = services.BuildServiceProvider();
        execution = new(store, tasks, provider.GetRequiredService<IServiceScopeFactory>(), writes, clock,
            lifetime, NullLogger<ScheduledTaskExecutionService>.Instance);
    }

    private async Task MakeDueAsync(string key)
    {
        ScheduledTask task = await tasks.GetAsync(key, default);
        if (!task.Enabled)
            task = await tasks.UpdateAsync(key, ScheduledTaskStoreTests.Request(task, true), default);
        Assert.True(await store.UpdateRunAsync(key, task.Revision, null, clock.GetUtcNow().AddDays(-4), default));
    }

    [Fact]
    public async Task Cleanup_SkipsMissedRunUntilNextScheduledMinute_AndPreservesLastRun()
    {
        await MakeDueAsync("cleanup");
        ScheduledTask before = await tasks.GetAsync("cleanup", default);
        DateTimeOffset lastRun = clock.GetUtcNow().AddDays(-5);
        await store.UpdateRunAsync("cleanup", before.Revision, lastRun, before.NextRunAt, default);
        clock.Advance(TimeSpan.FromHours(12));

        Assert.Null(await execution.RunAsync("cleanup", false, default));

        ScheduledTask skipped = await tasks.GetAsync("cleanup", default);
        Assert.Equal(0, cleanup.Calls);
        Assert.Equal(lastRun, skipped.LastRunAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero), skipped.NextRunAt);

        clock.Advance(TimeSpan.FromHours(12) + TimeSpan.FromSeconds(45));
        ScheduledTask completed = (await execution.RunAsync("cleanup", false, default))!;
        Assert.Equal(1, cleanup.Calls);
        Assert.Equal(clock.GetUtcNow(), completed.LastRunAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero), completed.NextRunAt);
        Assert.Null(await execution.RunAsync("cleanup", false, default));
    }

    [Fact]
    public async Task Cleanup_DoesNotRunAfterScheduledMinute_ButSyncStillCatchesUp()
    {
        await MakeDueAsync("cleanup");
        await MakeDueAsync("arrDataSync");
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Null(await execution.RunAsync("cleanup", false, default));
        Assert.Equal(0, cleanup.Calls);
        Assert.NotNull(await execution.RunAsync("arrDataSync", false, default));
        Assert.Equal(1, sync.Calls);
    }

    [Fact]
    public async Task Cleanup_UsesApplicationTimeZoneForScheduledMinute()
    {
        clock.Zone = TimeZoneInfo.CreateCustomTimeZone("UTC+2", TimeSpan.FromHours(2), "UTC+2", "UTC+2");
        await MakeDueAsync("cleanup");
        Assert.Null(await execution.RunAsync("cleanup", false, default));
        Assert.Equal(0, cleanup.Calls);

        clock.Advance(TimeSpan.FromHours(22));
        Assert.NotNull(await execution.RunAsync("cleanup", false, default));
        Assert.Equal(1, cleanup.Calls);
    }

    [Fact]
    public async Task Cleanup_CanRunManuallyOutsideScheduledMinute_WithoutChangingSchedule()
    {
        clock.Advance(TimeSpan.FromHours(12));
        ScheduledTask before = await tasks.GetAsync("cleanup", default);

        ScheduledTask completed = (await execution.RunAsync("cleanup", true, default))!;

        Assert.Equal(1, cleanup.Calls);
        Assert.Equal(clock.GetUtcNow(), completed.LastRunAt);
        Assert.Equal(before.NextRunAt, completed.NextRunAt);
        Assert.True(completed.ReadOnly);
        Assert.Null(await execution.RunAsync("cleanup", false, default));
    }

    [Fact]
    public async Task SameTaskCannotOverlap_ButOtherTaskRunsIndependently()
    {
        await MakeDueAsync("arrDataSync");
        await MakeDueAsync("cleanup");
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        sync.Work = token => release.Task.WaitAsync(token);
        Task<ScheduledTask?> first = execution.RunAsync("arrDataSync", false, default);
        await sync.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.Null(await execution.RunAsync("arrDataSync", false, default));
            ScheduledTaskException conflict = await Assert.ThrowsAsync<ScheduledTaskException>(() => execution.RunAsync("arrDataSync", true, default));
            Assert.Equal(409, conflict.StatusCode);
            Assert.NotNull(await execution.RunAsync("cleanup", false, default));
            Assert.Equal(1, cleanup.Calls);
            Assert.False(first.IsCompleted);
        }
        finally { release.TrySetResult(); }
        ScheduledTask completed = (await first)!;
        Assert.Equal(clock.GetUtcNow(), completed.LastRunAt);
        Assert.True(completed.NextRunAt > clock.GetUtcNow());
        Assert.Null(await execution.RunAsync("arrDataSync", false, default));
        Assert.Equal(1, sync.Calls);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 5)]
    public async Task CompletionUsesLatestConfiguration_AndNeverReenablesTask(bool enabled, int interval)
    {
        await MakeDueAsync("arrDataSync");
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        sync.Work = token => release.Task.WaitAsync(token);
        Task<ScheduledTask?> running = execution.RunAsync("arrDataSync", false, default);
        await sync.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ScheduledTask current = await tasks.GetAsync("arrDataSync", default);
        ScheduledTask updated = await tasks.UpdateAsync("arrDataSync", ScheduledTaskStoreTests.Request(current, enabled, interval), default);
        release.TrySetResult();
        await running;
        ScheduledTask completed = await tasks.GetAsync("arrDataSync", default);
        Assert.Equal(enabled, completed.Enabled);
        Assert.Equal(interval, completed.IntervalValue);
        Assert.Equal(updated.Revision, completed.Revision);
        Assert.Equal(clock.GetUtcNow(), completed.LastRunAt);
        Assert.Equal(enabled ? clock.GetUtcNow().AddHours(interval) : (DateTimeOffset?)null, completed.NextRunAt);
    }

    [Fact]
    public async Task FailedHandler_PreservesLastRunAndPersistsFutureRetry_OtherTasksStillRun()
    {
        await MakeDueAsync("arrDataSync");
        await MakeDueAsync("cleanup");
        ScheduledTask current = await tasks.GetAsync("arrDataSync", default);
        DateTimeOffset lastRun = clock.GetUtcNow().AddDays(-5);
        await store.UpdateRunAsync(current.TaskKey, current.Revision, lastRun, clock.GetUtcNow().AddDays(-1), default);
        sync.Work = _ => Task.FromException(new InvalidOperationException("test failure"));
        ScheduledTask failed = (await execution.RunAsync("arrDataSync", false, default))!;
        Assert.Equal(lastRun, failed.LastRunAt);
        Assert.Equal(clock.GetUtcNow().AddHours(1), failed.NextRunAt);
        Assert.Null(await execution.RunAsync("arrDataSync", false, default));
        Assert.Equal(1, sync.Calls);
        Assert.NotNull(await execution.RunAsync("cleanup", false, default));
        ScheduledTaskException error = await Assert.ThrowsAsync<ScheduledTaskException>(() => execution.RunAsync("arrDataSync", true, default));
        Assert.Equal(500, error.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_DoesNotMarkRunComplete_AndReleasesLock(bool shutdown)
    {
        await MakeDueAsync("arrDataSync");
        ScheduledTask before = await tasks.GetAsync("arrDataSync", default);
        using CancellationTokenSource request = new();
        sync.Work = token => Task.Delay(Timeout.Infinite, token);
        Task<ScheduledTask?> running = execution.RunAsync("arrDataSync", true, request.Token);
        await sync.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (shutdown) lifetime.StopApplication();
        else await request.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        ScheduledTask after = await tasks.GetAsync("arrDataSync", default);
        Assert.Null(after.LastRunAt);
        Assert.Equal(before.NextRunAt, after.NextRunAt);
        if (!shutdown)
        {
            sync.Work = _ => Task.CompletedTask;
            Assert.NotNull(await execution.RunAsync("arrDataSync", true, default));
        }
    }

    [Fact]
    public async Task ManualRunWorksWhenDisabled_WithoutSchedulingAnAutomaticRun()
    {
        // The task arrives enabled from the catalog defaults, so the disabled state has to be switched off first.
        ScheduledTask disabled = await tasks.GetAsync("arrDataSync", default);
        await tasks.UpdateAsync("arrDataSync", ScheduledTaskStoreTests.Request(disabled, false), default);
        ScheduledTask task = (await execution.RunAsync("arrDataSync", true, default))!;
        Assert.False(task.Enabled);
        Assert.NotNull(task.LastRunAt);
        Assert.Null(task.NextRunAt);
        Assert.Equal(1, sync.Calls);
        ScheduledTaskException unknown = await Assert.ThrowsAsync<ScheduledTaskException>(() => execution.RunAsync("unknown", true, default));
        Assert.Equal(404, unknown.StatusCode);
    }

    public void Dispose()
    {
        provider.Dispose();
        lifetime.Dispose();
        SqliteConnection.ClearAllPools();
        environment.Dispose();
    }

    private sealed class SchedulerClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset current = start;
        public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;
        public override TimeZoneInfo LocalTimeZone => Zone;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan by) => current += by;
    }

    private sealed class TestHandler(string key) : IScheduledTaskHandler
    {
        public string TaskKey => key;
        public int Calls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<CancellationToken, Task> Work { get; set; } = _ => Task.CompletedTask;
        public Task ExecuteAsync(CancellationToken token)
        {
            Calls++;
            Started.TrySetResult();
            return Work(token);
        }
    }

    private sealed class TestLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource stopping = new();
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => stopping.Cancel();
        public void Dispose() => stopping.Dispose();
    }
}
