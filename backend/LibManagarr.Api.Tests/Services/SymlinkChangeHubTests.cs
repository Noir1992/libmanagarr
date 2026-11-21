using LibManagarr.Api.Services;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Tests.Services;

public sealed class SymlinkChangeHubTests
{
    [Fact]
    public void SubscribersStartWithResyncAndShareMonotonicRevision()
    {
        SymlinkChangeHub hub = new();
        using var first = hub.Subscribe();
        using var second = hub.Subscribe();
        Assert.True(first.Reader.TryRead(out var initial));
        Assert.Contains("links", initial!.Areas);
        Assert.Equal(0, initial.Revision);
        Assert.True(second.Reader.TryRead(out _));
        hub.Publish("links");
        Assert.True(first.Reader.TryRead(out var change));
        Assert.True(second.Reader.TryRead(out var other));
        Assert.Equal(change, other);
        Assert.Equal(1, change!.Revision);
        Assert.Equal(hub.Generation, change.Generation);
        using var reconnect = hub.Subscribe();
        Assert.True(reconnect.Reader.TryRead(out var sync));
        Assert.Equal(change.Revision, sync!.Revision);
        Assert.Contains("suggestions", sync.Areas);
        Assert.NotEqual(hub.Generation, new SymlinkChangeHub().Generation);
    }

    [Fact]
    public async Task SlowAndDisconnectedSubscribersNeverBlockWritersOrLoseInvalidations()
    {
        SymlinkChangeHub hub = new();
        var slow = hub.Subscribe();
        await Task.Run(() => { for (int index = 0; index < 10000; index++) hub.Publish("links"); });
        Assert.True(slow.Reader.TryRead(out var sync));
        Assert.Equal(10000, sync!.Revision);
        Assert.Contains("rejections", sync.Areas);
        Assert.Contains("suggestions", sync.Areas);
        Assert.False(slow.Reader.TryRead(out _));
        slow.Dispose();
        await slow.Reader.Completion;
        hub.Publish("count");
        Assert.Equal(10001, hub.Revision);
    }

    [Fact]
    public void CacheInvalidatesSourceRevisionsAndSettingsAndIgnoresTimestampOnlyChanges()
    {
        SymlinkInspectionCache cache = new();
        AppSettings settings = new();
        SymlinkSuggestion suggestion = new() { Id = "offer", Revision = 1 };
        Assert.True(cache.Read(settings, suggestion).Stale);
        ExistingEpisodeFileCheck check = new() { Status = ExistingEpisodeFileStatus.Matches, CheckedAt = DateTimeOffset.UtcNow };
        Assert.True(cache.Store(settings, suggestion, check));
        Assert.False(cache.Read(settings, suggestion).Stale);
        Assert.Same(check, cache.Read(settings, suggestion).Check);
        Assert.False(cache.Store(settings, suggestion, new() { Status = check.Status, CheckedAt = check.CheckedAt.AddMinutes(5) }));
        suggestion.Revision++;
        Assert.True(cache.Read(settings, suggestion).Stale);
        Assert.Null(cache.Read(settings, suggestion).Check);
        Assert.True(cache.Read(new AppSettings(), suggestion).Stale);
        cache.Prune([]);
        Assert.True(cache.Read(settings, suggestion).Stale);
    }
}
