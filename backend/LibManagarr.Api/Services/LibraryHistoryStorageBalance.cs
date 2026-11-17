using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

internal sealed record LibraryStorageAdjustment(ArrApplicationKind Kind, string InstanceId, DateTimeOffset At, long SizeBytes);

internal static class LibraryHistoryStorageBalance
{
    internal static List<LibraryStorageAdjustment> OpeningBalances(IReadOnlyList<LibraryChangeHistoryEntry> entries)
    {
        List<LibraryStorageAdjustment> balances = [];
        foreach (var media in entries.Where(entry => !string.IsNullOrWhiteSpace(entry.MediaKey)
            && entry.EventType != LibraryInventoryHistoryBuilder.EventType)
            .GroupBy(entry => (entry.Kind, entry.InstanceId, entry.MediaKey)))
        {
            long booked = 0;
            foreach (LibraryChangeHistoryEntry entry in media.OrderBy(entry => entry.OccurredAt)
                .ThenBy(entry => entry.HistoryId ?? NumericId(entry.SourceKey)).ThenBy(entry => entry.SourceKey, StringComparer.Ordinal))
            {
                if (!entry.IsSizeDeltaKnown) break;
                booked += entry.DeltaSizeBytes;
                long? sizeAfterEvent = entry.ChangeType == LibraryChangeType.Deleted ? 0 : entry.SizeBytes;
                if (!sizeAfterEvent.HasValue) continue;
                long initialSize = sizeAfterEvent.Value - booked;
                if (initialSize > 0) balances.Add(new(entry.Kind, entry.InstanceId, media.Min(entry => entry.OccurredAt), initialSize));
                break;
            }
        }
        return balances;
    }

    private static long NumericId(string key) => long.TryParse(key.AsSpan(key.LastIndexOf(':') + 1), out long id) ? id : long.MaxValue;
}
