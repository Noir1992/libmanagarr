using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public static class LibraryChangeHistoryNormalizer
{
    private static readonly TimeSpan UpdateWindow = TimeSpan.FromMinutes(5);

    public static List<LibraryChangeHistoryEntry> Normalize(IEnumerable<LibraryChangeHistoryEntry> entries)
    {
        List<LibraryChangeHistoryEntry> orderedEntries = entries
            .OrderBy((entry) => entry.OccurredAt)
            .ThenBy((entry) => entry.Kind)
            .ThenBy((entry) => NormalizeMediaKey(entry), StringComparer.OrdinalIgnoreCase)
            .ThenBy((entry) => entry.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<LibraryChangeHistoryEntry> normalizedEntries = [];
        Dictionary<string, int> lastIndexByMediaKey = new(StringComparer.OrdinalIgnoreCase);

        foreach (LibraryChangeHistoryEntry entry in orderedEntries)
        {
            string mediaKey = NormalizeMediaKey(entry);
            if (lastIndexByMediaKey.TryGetValue(mediaKey, out int previousIndex) &&
                TryMergeUpdate(normalizedEntries[previousIndex], entry, out LibraryChangeHistoryEntry mergedEntry))
            {
                normalizedEntries[previousIndex] = mergedEntry;
                continue;
            }

            normalizedEntries.Add(Copy(entry));
            lastIndexByMediaKey[mediaKey] = normalizedEntries.Count - 1;
        }

        return normalizedEntries;
    }

    private static bool TryMergeUpdate(
        LibraryChangeHistoryEntry previousEntry,
        LibraryChangeHistoryEntry currentEntry,
        out LibraryChangeHistoryEntry mergedEntry)
    {
        mergedEntry = previousEntry;

        if (previousEntry.ChangeType != LibraryChangeType.Deleted ||
            currentEntry.ChangeType != LibraryChangeType.Added ||
            previousEntry.Kind != currentEntry.Kind ||
            previousEntry.InstanceId != currentEntry.InstanceId ||
            currentEntry.OccurredAt < previousEntry.OccurredAt ||
            currentEntry.OccurredAt - previousEntry.OccurredAt > UpdateWindow)
        {
            return false;
        }

        mergedEntry = new LibraryChangeHistoryEntry
        {
            SourceKey = BuildUpdatedSourceKey(previousEntry, currentEntry),
            Kind = currentEntry.Kind,
            InstanceId = currentEntry.InstanceId,
            InstanceName = currentEntry.InstanceName,
            OccurredAt = currentEntry.OccurredAt,
            MediaKey = currentEntry.MediaKey,
            ChangeType = LibraryChangeType.Updated,
            EntityTitle = currentEntry.EntityTitle,
            SeriesTitle = currentEntry.SeriesTitle ?? previousEntry.SeriesTitle,
            FilePath = currentEntry.FilePath ?? previousEntry.FilePath,
            SizeBytes = currentEntry.SizeBytes ?? previousEntry.SizeBytes,
            DeltaSizeBytes = ResolveDeltaSize(previousEntry, currentEntry)
        };

        return true;
    }

    private static long ResolveDeltaSize(
        LibraryChangeHistoryEntry previousEntry,
        LibraryChangeHistoryEntry currentEntry)
    {
        long currentSize = currentEntry.SizeBytes ?? 0;
        long previousSize = previousEntry.SizeBytes ?? 0;
        return currentSize - previousSize;
    }

    private static string BuildUpdatedSourceKey(
        LibraryChangeHistoryEntry previousEntry,
        LibraryChangeHistoryEntry currentEntry)
    {
        return $"updated:{currentEntry.MediaKey}:{previousEntry.OccurredAt:O}:{currentEntry.OccurredAt:O}";
    }

    private static string NormalizeMediaKey(LibraryChangeHistoryEntry entry)
    {
        return string.IsNullOrWhiteSpace(entry.MediaKey)
            ? $"legacy:{entry.SourceKey}"
            : entry.MediaKey;
    }

    private static LibraryChangeHistoryEntry Copy(LibraryChangeHistoryEntry entry)
    {
        return new LibraryChangeHistoryEntry
        {
            SourceKey = entry.SourceKey,
            Kind = entry.Kind,
            InstanceId = entry.InstanceId,
            InstanceName = entry.InstanceName,
            OccurredAt = entry.OccurredAt,
            MediaKey = NormalizeMediaKey(entry),
            ChangeType = entry.ChangeType,
            EntityTitle = entry.EntityTitle,
            SeriesTitle = entry.SeriesTitle,
            FilePath = entry.FilePath,
            SizeBytes = entry.SizeBytes,
            DeltaSizeBytes = entry.DeltaSizeBytes
        };
    }
}
