using System.Collections.Concurrent;
using System.Text.Json;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>Display-only results. Confirmation and preview continue to inspect live state.</summary>
public sealed class SymlinkInspectionCache
{
    private sealed record Entry(AppSettings Settings, long Revision, SpecialMoviePair Pair, int? FileId, ExistingEpisodeFileCheck? Check);
    private readonly ConcurrentDictionary<string, Entry> entries = new();
    public (ExistingEpisodeFileCheck? Check, bool Stale, bool Checked) Read(AppSettings settings, SymlinkSuggestion suggestion)
    {
        if (!entries.TryGetValue(suggestion.Id, out Entry? entry)) return (null, true, false);
        bool stale = !ReferenceEquals(entry.Settings, settings) || entry.Revision != suggestion.Revision
            || entry.Pair != suggestion.Pair || entry.FileId != suggestion.RadarrFileId;
        // A result for another source or configuration must never offer an adoption action.
        return (stale ? null : entry.Check, stale, true);
    }
    public bool Store(AppSettings settings, SymlinkSuggestion suggestion, ExistingEpisodeFileCheck? check)
    {
        entries.TryGetValue(suggestion.Id, out Entry? old);
        entries[suggestion.Id] = new(settings, suggestion.Revision, suggestion.Pair, suggestion.RadarrFileId, check);
        return old is null || !ReferenceEquals(old.Settings, settings) || old.Revision != suggestion.Revision || old.Pair != suggestion.Pair || old.FileId != suggestion.RadarrFileId
            || Fingerprint(old.Check) != Fingerprint(check);
    }
    public void Prune(HashSet<string> ids)
    {
        foreach (string id in entries.Keys) if (!ids.Contains(id)) entries.TryRemove(id, out _);
    }
    private static string Fingerprint(ExistingEpisodeFileCheck? check) => check is null ? "null" :
        JsonSerializer.Serialize(new { check.Status, check.Detail, check.EpisodePath, check.ExpectedTarget,
            check.ActualTarget, check.SonarrFileId, check.LinkText });
}
