using System.Collections.Concurrent;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

// Short-lived per-range cache in front of the arr calendar endpoints so rapid navigation and
// the visibility-driven polling do not hammer every configured instance on each view change.
public sealed class CalendarCache(TimeProvider timeProvider)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, CachedEntries> cache = new(StringComparer.Ordinal);

    public bool TryGet(string key, out IReadOnlyList<CalendarEntry> entries)
    {
        if (cache.TryGetValue(key, out CachedEntries? cached) && timeProvider.GetUtcNow() < cached.ExpiresAt)
        {
            entries = cached.Entries;
            return true;
        }

        entries = [];
        return false;
    }

    public void Set(string key, IReadOnlyList<CalendarEntry> entries)
    {
        cache[key] = new CachedEntries(entries, timeProvider.GetUtcNow() + Lifetime);
    }

    private sealed record CachedEntries(IReadOnlyList<CalendarEntry> Entries, DateTimeOffset ExpiresAt);
}
