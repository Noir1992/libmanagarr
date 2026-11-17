using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

internal static partial class LibraryHistoryArchiveRepair
{
    internal sealed record Snapshot(ArrApplicationKind Kind, string InstanceId, string InstanceName, DateTimeOffset At, string Payload);

    internal static List<LibraryChangeHistoryEntry> Repair(
        IReadOnlyList<Snapshot> archive, IReadOnlyList<LibraryChangeHistoryEntry> history, JsonSerializerOptions options)
    {
        List<LibraryChangeHistoryEntry> inferred = [];
        foreach (var group in archive.GroupBy(s => (s.Kind, s.InstanceId)))
        {
            Snapshot[] snapshots = group.OrderBy(s => s.At).ToArray();
            if (group.Key.Kind == ArrApplicationKind.Sonarr)
            {
                SonarrInstanceSnapshot? previous = null;
                foreach (Snapshot item in snapshots)
                {
                    SonarrInstanceSnapshot? current = JsonSerializer.Deserialize<SonarrInstanceSnapshot>(item.Payload, options);
                    if (current is null) continue;
                    var instance = new ArrInstanceSettings { Id = item.InstanceId, Name = item.InstanceName };
                    if (current.SeriesSyncTimes.Count == 0)
                        inferred.AddRange(LibraryHistoryReconciler.InferSonarrDeletions(instance, previous, current, [.. history, .. inferred]));
                    else
                        foreach (int seriesId in current.SeriesSyncTimes.Where(pair => pair.Value == current.SyncedAt).Select(pair => pair.Key))
                            inferred.AddRange(LibraryHistoryReconciler.InferSonarrDeletions(instance, previous, current, [.. history, .. inferred], seriesId));
                    previous = current;
                }
            }
            else
            {
                RadarrInstanceSnapshot? previous = null;
                foreach (Snapshot item in snapshots)
                {
                    RadarrInstanceSnapshot? current = JsonSerializer.Deserialize<RadarrInstanceSnapshot>(item.Payload, options);
                    if (current is null) continue;
                    var instance = new ArrInstanceSettings { Id = item.InstanceId, Name = item.InstanceName };
                    inferred.AddRange(LibraryHistoryReconciler.InferRadarrDeletions(instance, previous, current, [.. history, .. inferred]));
                    previous = current;
                }
            }

            // The last snapshot strictly before the deletion can prove the old size of a legacy
            // merged update. A snapshot after the import alone cannot prove the old size.
            foreach (LibraryChangeHistoryEntry entry in history.Where(e => e.Kind == group.Key.Kind
                && e.InstanceId == group.Key.InstanceId && e.ChangeType == LibraryChangeType.Updated && !e.IsSizeDeltaKnown))
            {
                Match match = UpdatedKey().Match(entry.SourceKey);
                if (!match.Success || !DateTimeOffset.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out DateTimeOffset deletedAt)) continue;
                Snapshot? before = snapshots.LastOrDefault(s => s.At < deletedAt);
                if (before is null || !int.TryParse(entry.MediaKey.AsSpan(entry.MediaKey.LastIndexOf(':') + 1), out int id)) continue;
                long? oldSize = before.Kind == ArrApplicationKind.Sonarr
                    ? LibraryChangeHistoryBuilder.SonarrFiles(JsonSerializer.Deserialize<SonarrInstanceSnapshot>(before.Payload, options)!).GetValueOrDefault(id)?.File.Size
                    : LibraryChangeHistoryBuilder.RadarrFiles(JsonSerializer.Deserialize<RadarrInstanceSnapshot>(before.Payload, options)!).GetValueOrDefault(id)?.File.Size;
                if (oldSize.HasValue && entry.SizeBytes.HasValue)
                {
                    entry.DeltaSizeBytes = entry.SizeBytes.Value - oldSize.Value;
                    entry.IsSizeDeltaKnown = true;
                }
            }
        }
        return inferred;
    }

    [GeneratedRegex(@"^updated:.*:(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}[+-]\d{2}:\d{2}):(\d{4}-.*)$")]
    private static partial Regex UpdatedKey();
}
