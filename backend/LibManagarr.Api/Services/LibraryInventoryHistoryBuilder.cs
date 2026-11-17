using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>Records measured inventory differences without pretending to know the missing titles or event dates.</summary>
internal static class LibraryInventoryHistoryBuilder
{
    internal const string EventType = "libraryInventoryReconciled";

    internal static List<LibraryChangeHistoryEntry> Observe(IReadOnlyList<LibraryChangeHistoryEntry> events,
        ArrApplicationKind kind, string instanceId, string instanceName, DateTimeOffset at, long sizeBytes)
    {
        if (at == default) return [.. events];
        var measurement = new LibraryChangeHistoryEntry
        {
            SourceKey = $"inventory-reconciliation:{kind}:{instanceId}:{at:O}",
            Kind = kind,
            InstanceId = instanceId,
            InstanceName = instanceName,
            OccurredAt = at,
            MediaKey = $"inventory:{kind}:{instanceId}",
            EventType = EventType,
            ChangeType = LibraryChangeType.Changed,
            EntityTitle = "Library inventory reconciliation",
            SizeBytes = sizeBytes,
            IsInferred = true,
            IsSizeDeltaKnown = true
        };
        var original = events.Where(entry => entry.EventType != EventType).ToList();
        var projected = LibraryHistoryReconciler.Project(original);
        var balances = LibraryHistoryStorageBalance.OpeningBalances(projected);
        var observations = events.Where(entry => entry.EventType == EventType).Append(measurement)
            .GroupBy(entry => (entry.Kind, entry.InstanceId, entry.SourceKey)).Select(group => group.Last());
        List<LibraryChangeHistoryEntry> result = [.. original];
        foreach (var instance in observations.GroupBy(entry => (entry.Kind, entry.InstanceId)))
        {
            long reconciled = 0;
            var history = projected.Where(entry => entry.Kind == instance.Key.Kind && entry.InstanceId == instance.Key.InstanceId).ToArray();
            var opening = balances.Where(balance => balance.Kind == instance.Key.Kind && balance.InstanceId == instance.Key.InstanceId).ToArray();
            foreach (LibraryChangeHistoryEntry observation in instance.OrderBy(entry => entry.OccurredAt))
            {
                if (!observation.SizeBytes.HasValue) continue;
                long booked = history.Where(entry => entry.OccurredAt <= observation.OccurredAt).Sum(entry => entry.DeltaSizeBytes)
                    + opening.Where(balance => balance.At <= observation.OccurredAt).Sum(balance => balance.SizeBytes) + reconciled;
                long difference = observation.SizeBytes.Value - booked;
                reconciled += difference;
                // Keep the measurement even when its current difference is zero: recovering an
                // older real event later can change the difference without losing this evidence.
                result.Add(new LibraryChangeHistoryEntry
                {
                    SourceKey = observation.SourceKey,
                    Kind = observation.Kind,
                    InstanceId = observation.InstanceId,
                    InstanceName = observation.InstanceName,
                    OccurredAt = observation.OccurredAt,
                    MediaKey = observation.MediaKey,
                    EventType = EventType,
                    ChangeType = LibraryChangeType.Changed,
                    EntityTitle = observation.EntityTitle,
                    SizeBytes = observation.SizeBytes,
                    DeltaSizeBytes = difference,
                    IsInferred = true,
                    IsSizeDeltaKnown = true
                });
            }
        }
        return result;
    }
}
