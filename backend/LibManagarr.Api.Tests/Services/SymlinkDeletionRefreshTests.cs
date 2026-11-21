using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibManagarr.Api.Tests.Services;

public sealed class SymlinkDeletionRefreshTests
{
    [Fact]
    public async Task FailedRefreshRemainsQueuedAndASubsequentPassFinishesIt()
    {
        InMemorySymlinkStore store = new();
        for (int id = 1; id <= 2; id++)
        {
            SymlinkLink link = new() { Id = $"link-{id}", SonarrInstanceId = "sonarr", SonarrSeriesId = 12, SonarrEpisodeId = id };
            await store.InsertLinkAsync(link, CancellationToken.None);
            Assert.True(await store.CompleteDeletionAsync(link, CancellationToken.None));
        }
        SyncProbe snapshots = new();
        SymlinkSuggestionService matching = new(new Settings(), store, TimeProvider.System, NullLogger<SymlinkSuggestionService>.Instance);
        SymlinkDeletionRefreshService service = new(store, snapshots, matching, NullLogger<SymlinkDeletionRefreshService>.Instance);

        Assert.True(await service.RunAsync(CancellationToken.None));
        Assert.Equal(2, (await store.GetDeletionRefreshesAsync(CancellationToken.None)).Count);
        Assert.Empty(await store.GetLinksAsync(CancellationToken.None));
        snapshots.Fail = false;
        Assert.False(await service.RunAsync(CancellationToken.None));
        Assert.Empty(await store.GetDeletionRefreshesAsync(CancellationToken.None));
        Assert.Equal(2, snapshots.Calls); // One series read per pass, even for multiple deleted links.
    }

    [Fact]
    public async Task NewRequestInterruptsTheRetryDelayAndCallerCancellationIsPreserved()
    {
        SymlinkSetupRequests requests = new();
        Task waiting = requests.WaitForRequestOrDelayAsync(TimeSpan.FromMinutes(5), TimeProvider.System, CancellationToken.None);
        Assert.False(waiting.IsCompleted);
        requests.Request();
        await waiting.WaitAsync(TimeSpan.FromSeconds(2));
        using CancellationTokenSource cancellation = new();
        Task next = requests.WaitForRequestOrDelayAsync(TimeSpan.FromMinutes(5), TimeProvider.System, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
    }

    private sealed class Settings : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken token) => Task.FromResult(new AppSettings());
        public Task<AppSettings> SaveAsync(AppSettings settings, CancellationToken token) => Task.FromResult(settings);
    }

    private sealed class SyncProbe : ISnapshotSyncService
    {
        public bool Fail { get; set; } = true;
        public int Calls { get; private set; }
        public Task<SyncInstanceResult> SyncSonarrSeriesAsync(string instanceId, int seriesId, CancellationToken token)
        {
            Calls++;
            if (Fail) throw new HttpRequestException("Sonarr is offline");
            return Task.FromResult(new SyncInstanceResult());
        }
        public Task<SyncInstanceResult> SyncAsync(SyncInstanceRequest request, CancellationToken token) => throw new InvalidOperationException("A deletion must only refresh its series.");
    }
}
