using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

[CollectionDefinition("Symlink gate isolation", DisableParallelization = true)]
public sealed class SymlinkGateCollection;

[Collection("Symlink gate isolation")]
public sealed class SymlinkOperationGateTests
{
    private readonly string instance = "gate-test-" + Guid.NewGuid().ToString("N");
    private SpecialMoviePair Pair(int episode) => new("radarr", 42, instance, 12, episode);
    private SymlinkLink Row(string id, int episode) => new()
    {
        Id = id, RadarrInstanceId = "radarr", RadarrMovieId = 42, SonarrInstanceId = instance,
        SonarrSeriesId = 12, SonarrEpisodeId = episode,
        LinkPath = Path.Combine(Path.GetTempPath(), instance, id + ".mkv"),
        SourcePath = Path.Combine(Path.GetTempPath(), "shared-movie.mkv")
    };

    [Fact]
    public async Task DifferentLinksCanProceedWhileTheSameEpisodeWaitsEvenWithAnotherMovie()
    {
        InMemorySymlinkStore store = new();
        SymlinkLink firstRow = Row("first", 1), otherRow = Row("other", 2);
        await store.InsertLinkAsync(firstRow, CancellationToken.None);
        await store.InsertLinkAsync(otherRow, CancellationToken.None);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        using IDisposable first = await SymlinkOperationGate.EnterLinkAsync(store, firstRow.Id, deadline.Token);
        Task<IDisposable> same = SymlinkOperationGate.EnterAsync(Pair(1) with { RadarrMovieId = 99 }, deadline.Token);
        Assert.False(same.IsCompleted);
        using (await SymlinkOperationGate.EnterLinkAsync(store, otherRow.Id, deadline.Token).WaitAsync(TimeSpan.FromSeconds(2)))
            Assert.False(same.IsCompleted);
        first.Dispose();
        using IDisposable next = await same;
    }

    [Fact]
    public async Task DifferentEpisodesSharingALinkPathStillSerialize()
    {
        InMemorySymlinkStore store = new();
        SymlinkLink firstRow = Row("first", 1), otherRow = Row("other", 2);
        otherRow.LinkPath = firstRow.LinkPath;
        await store.InsertLinkAsync(firstRow, CancellationToken.None);
        await store.InsertLinkAsync(otherRow, CancellationToken.None);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        using IDisposable first = await SymlinkOperationGate.EnterLinkAsync(store, firstRow.Id, deadline.Token);
        Task<IDisposable> samePath = SymlinkOperationGate.EnterLinkAsync(store, otherRow.Id, deadline.Token);
        Assert.False(samePath.IsCompleted);
        first.Dispose();
        using IDisposable next = await samePath;
    }

    [Fact]
    public async Task OppositePairEditsAcquireBothEpisodesInTheSameOrder()
    {
        InMemorySymlinkStore store = new();
        await store.InsertLinkAsync(Row("first", 1), CancellationToken.None);
        await store.InsertLinkAsync(Row("other", 2), CancellationToken.None);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        using IDisposable first = await SymlinkOperationGate.EnterLinkAsync(store, "first", deadline.Token, Pair(2));
        Task<IDisposable> opposite = SymlinkOperationGate.EnterLinkAsync(store, "other", deadline.Token, Pair(1));
        Assert.False(opposite.IsCompleted);
        using (await SymlinkOperationGate.EnterAsync(Pair(3), deadline.Token).WaitAsync(TimeSpan.FromSeconds(2)))
            Assert.False(opposite.IsCompleted);
        first.Dispose();
        using IDisposable next = await opposite;
    }

    [Fact]
    public async Task ARowChangedWhileWaitingReacquiresItsNewEpisode()
    {
        InMemorySymlinkStore store = new();
        SymlinkLink row = Row("moving", 1);
        await store.InsertLinkAsync(row, CancellationToken.None);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        using IDisposable oldEpisode = await SymlinkOperationGate.EnterAsync(Pair(1), deadline.Token);
        Task<IDisposable> waiting = SymlinkOperationGate.EnterLinkAsync(store, row.Id, deadline.Token);
        Assert.False(waiting.IsCompleted);
        row.SonarrEpisodeId = 2;
        Assert.True(await store.UpdateLinkAsync(row, row.Revision, deadline.Token));
        using IDisposable newEpisode = await SymlinkOperationGate.EnterAsync(Pair(2), deadline.Token);
        oldEpisode.Dispose();
        // The waiter must release its outdated episode reservation before waiting for the new one.
        using (await SymlinkOperationGate.EnterAsync(Pair(1), deadline.Token).WaitAsync(TimeSpan.FromSeconds(2)))
            Assert.False(waiting.IsCompleted);
        newEpisode.Dispose();
        using IDisposable next = await waiting;
    }

    [Fact]
    public async Task CancellingAMultipleResourceWaitReleasesItsPartialReservations()
    {
        InMemorySymlinkStore store = new();
        await store.InsertLinkAsync(Row("moving", 1), CancellationToken.None);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        using IDisposable second = await SymlinkOperationGate.EnterAsync(Pair(2), deadline.Token);
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        Task<IDisposable> waiting = SymlinkOperationGate.EnterLinkAsync(store, "moving", cancel.Token, Pair(2));
        Assert.False(waiting.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        using (await SymlinkOperationGate.EnterAsync(Pair(1), deadline.Token).WaitAsync(TimeSpan.FromSeconds(2))) { }
        second.Dispose();
        using IDisposable configuration = await SymlinkOperationGate.EnterExclusiveAsync(deadline.Token);
    }

    [Fact]
    public async Task ConfigurationChangesWaitForExistingLinksAndExcludeNewOnesUntilReleased()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        using IDisposable link = await SymlinkOperationGate.EnterAsync(Pair(1), deadline.Token);
        Task<IDisposable> configuration = SymlinkOperationGate.EnterExclusiveAsync(deadline.Token);
        Assert.False(configuration.IsCompleted);
        Task<IDisposable> other = SymlinkOperationGate.EnterAsync(Pair(2), deadline.Token);
        Assert.False(other.IsCompleted);
        link.Dispose();
        using IDisposable exclusive = await configuration;
        Assert.False(other.IsCompleted);
        exclusive.Dispose();
        using IDisposable next = await other;
    }
}
