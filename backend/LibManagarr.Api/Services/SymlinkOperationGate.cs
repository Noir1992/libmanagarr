using System.Text.Json;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>Serializes changes to the same link, episode or path. Only configuration changes
/// exclude all link operations; unrelated links share access to the configuration.</summary>
public static class SymlinkOperationGate
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim Admission = new(1, 1);
    private static readonly SemaphoreSlim Configuration = new(1, 1);
    private static int readers;

    public static Task<IDisposable> EnterAsync(SpecialMoviePair pair, CancellationToken token) =>
        EnterKeysAsync([EpisodeKey(pair)], token);

    public static async Task<IDisposable> EnterLinkAsync(ISymlinkStore store, string id, CancellationToken token,
        SpecialMoviePair? destination = null)
    {
        while (true)
        {
            SymlinkLink? observed = await store.GetLinkAsync(id, token);
            string[] keys = LinkKeys(id, observed, destination);
            IDisposable lease = await EnterKeysAsync(keys, token);
            try
            {
                SymlinkLink? current = await store.GetLinkAsync(id, token);
                // A queued edit may have changed the episode or path while we waited.
                if (LinkKeys(id, current, destination).All(key => keys.Contains(key, StringComparer.Ordinal)))
                    return lease;
            }
            catch { lease.Dispose(); throw; }
            lease.Dispose();
        }
    }

    public static async Task<IDisposable> EnterExclusiveAsync(CancellationToken token)
    {
        await Admission.WaitAsync(token);
        try
        {
            await Configuration.WaitAsync(token);
            return new Lease(() => { Configuration.Release(); Admission.Release(); });
        }
        catch { Admission.Release(); throw; }
    }

    private static async Task<IDisposable> EnterSharedAsync(CancellationToken token)
    {
        await Admission.WaitAsync(token);
        try
        {
            lock (Sync)
            {
                if (readers > 0)
                {
                    readers++;
                    return new Lease(ReleaseShared);
                }
            }
            await Configuration.WaitAsync(token);
            lock (Sync) readers = 1;
            return new Lease(ReleaseShared);
        }
        finally { Admission.Release(); }
    }

    private static void ReleaseShared()
    {
        lock (Sync)
        {
            readers--;
            if (readers == 0) Configuration.Release();
        }
    }

    private static async Task<IDisposable> EnterKeysAsync(IEnumerable<string> resources, CancellationToken token)
    {
        IDisposable configuration = await EnterSharedAsync(token);
        List<(string Key, Entry Entry)> acquired = [];
        try
        {
            foreach (string key in resources.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                Entry entry;
                lock (Sync)
                {
                    if (!Entries.TryGetValue(key, out entry!)) Entries[key] = entry = new Entry();
                    entry.Users++;
                }
                try { await entry.Gate.WaitAsync(token); }
                catch { Dereference(key, entry); throw; }
                acquired.Add((key, entry));
            }
            return new Lease(() => Release(acquired, configuration));
        }
        catch { Release(acquired, configuration); throw; }
    }

    private static void Release(List<(string Key, Entry Entry)> acquired, IDisposable configuration)
    {
        for (int index = acquired.Count - 1; index >= 0; index--)
        {
            var (key, entry) = acquired[index];
            entry.Gate.Release();
            Dereference(key, entry);
        }
        configuration.Dispose();
    }

    private static void Dereference(string key, Entry entry)
    {
        lock (Sync)
        {
            if (--entry.Users != 0) return;
            Entries.Remove(key);
            entry.Gate.Dispose();
        }
    }

    private static string EpisodeKey(SpecialMoviePair pair) => "episode:" + JsonSerializer.Serialize(
        new object[] { pair.SonarrInstanceId.ToUpperInvariant(), pair.SonarrSeriesId, pair.SonarrEpisodeId });

    private static string[] LinkKeys(string id, SymlinkLink? link, SpecialMoviePair? destination)
    {
        List<string> keys = ["link:" + id];
        if (destination is not null) keys.Add(EpisodeKey(destination));
        if (link is not null)
        {
            keys.Add(EpisodeKey(link.Pair));
            if (link.PendingUpdate?.Pair is { } pair) keys.Add(EpisodeKey(pair));
            if (link.PendingUpdate?.PreviousPair is { } previous) keys.Add(EpisodeKey(previous));
            foreach (string? path in new[] { link.LinkPath, link.PendingUpdate?.LinkPath, link.NamingMove?.PreviousPath, link.NamingMove?.NewPath })
            {
                if (string.IsNullOrEmpty(path)) continue;
                string full = Path.GetFullPath(path);
                keys.Add("path:" + (OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full));
            }
        }
        return keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int Users { get; set; }
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? releaseAction = release;
        public void Dispose() => Interlocked.Exchange(ref releaseAction, null)?.Invoke();
    }
}
