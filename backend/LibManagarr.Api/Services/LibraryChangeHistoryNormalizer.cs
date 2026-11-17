using System.Text.Json;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public static class LibraryChangeHistoryNormalizer
{
    private static readonly TimeSpan UpdateWindow = TimeSpan.FromMinutes(5);
    // An explicit upgrade reason permits slow imports, but never an unrelated later download.
    private static readonly TimeSpan UpgradeWindow = TimeSpan.FromHours(24);

    public static List<LibraryChangeHistoryEntry> Normalize(IEnumerable<LibraryChangeHistoryEntry> entries)
    {
        List<LibraryChangeHistoryEntry> result = [];
        foreach (var group in entries.GroupBy(e => (e.Kind, e.InstanceId, Key(e))))
        {
            List<LibraryChangeHistoryEntry> ordered = group.OrderBy(e => e.OccurredAt)
                .ThenBy(e => e.HistoryId ?? NumericId(e.SourceKey))
                .ThenBy(e => e.SourceKey, StringComparer.Ordinal).Select(Copy).ToList();
            HashSet<int> consumed = [];
            for (int i = 0; i < ordered.Count; i++)
            {
                LibraryChangeHistoryEntry deletion = ordered[i];
                if (deletion.ChangeType != LibraryChangeType.Deleted || deletion.IsInferred) continue;
                int partner = -1;
                for (int j = i + 1; j < ordered.Count; j++)
                {
                    if (ordered[j].ChangeType == LibraryChangeType.Changed) continue;
                    if (!consumed.Contains(j) && CanMerge(deletion, ordered[j])) partner = j;
                    break;
                }
                // Some imports record their deletion just after the import. Only explicit upgrades
                // can pair backwards; a normal user deletion must stay a deletion.
                if (partner < 0 && IsUpgrade(deletion))
                {
                    for (int j = i - 1; j >= 0; j--)
                    {
                        if (ordered[j].ChangeType == LibraryChangeType.Changed) continue;
                        if (!consumed.Contains(j) && CanMerge(deletion, ordered[j], reversed: true)) partner = j;
                        break;
                    }
                }
                if (partner < 0) continue;
                LibraryChangeHistoryEntry import = ordered[partner];
                LibraryChangeHistoryEntry update = Copy(import);
                update.SourceKey = $"updated:{import.MediaKey}:{deletion.OccurredAt:O}:{import.OccurredAt:O}";
                update.ChangeType = LibraryChangeType.Updated;
                update.SeriesTitle ??= deletion.SeriesTitle;
                update.FilePath ??= deletion.FilePath;
                update.IsSizeDeltaKnown = deletion.SizeBytes.HasValue && import.SizeBytes.HasValue;
                update.DeltaSizeBytes = update.IsSizeDeltaKnown ? import.SizeBytes!.Value - deletion.SizeBytes!.Value : 0;
                consumed.Add(i);
                consumed.Add(partner);
                result.Add(update);
            }
            result.AddRange(ordered.Where((_, index) => !consumed.Contains(index)));
        }
        return result.OrderBy(e => e.OccurredAt).ThenBy(e => e.Kind)
            .ThenBy(e => e.MediaKey, StringComparer.Ordinal).ThenBy(e => e.HistoryId ?? NumericId(e.SourceKey)).ToList();
    }

    private static bool CanMerge(LibraryChangeHistoryEntry deletion, LibraryChangeHistoryEntry import, bool reversed = false)
    {
        if (import.ChangeType != LibraryChangeType.Added || import.IsInferred || import.SourceKey.StartsWith("seed:", StringComparison.Ordinal)) return false;
        if (!string.IsNullOrEmpty(deletion.DeletionReason) && !IsUpgrade(deletion)) return false;
        if (deletion.FileId is not null && deletion.FileId == import.FileId) return false;
        TimeSpan distance = reversed ? deletion.OccurredAt - import.OccurredAt : import.OccurredAt - deletion.OccurredAt;
        return distance >= TimeSpan.Zero && distance <= (IsUpgrade(deletion) && !reversed ? UpgradeWindow : UpdateWindow);
    }

    private static bool IsUpgrade(LibraryChangeHistoryEntry entry) =>
        string.Equals(entry.DeletionReason, "Upgrade", StringComparison.OrdinalIgnoreCase);

    private static long NumericId(string key) => long.TryParse(key.AsSpan(key.LastIndexOf(':') + 1), out long id) ? id : long.MaxValue;
    private static string Key(LibraryChangeHistoryEntry entry) => string.IsNullOrWhiteSpace(entry.MediaKey) ? $"legacy:{entry.SourceKey}" : entry.MediaKey;

    private static LibraryChangeHistoryEntry Copy(LibraryChangeHistoryEntry entry)
    {
        LibraryChangeHistoryEntry copy = JsonSerializer.Deserialize<LibraryChangeHistoryEntry>(JsonSerializer.Serialize(entry))!;
        copy.MediaKey = Key(entry);
        return copy;
    }
}
