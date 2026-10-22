using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

internal sealed class InMemoryHistoryStore : IHistoryStore
{
    public List<LibraryChangeHistoryEntry> SonarrLibraryHistory { get; } = [];

    public List<LibraryChangeHistoryEntry> RadarrLibraryHistory { get; } = [];

    public List<object> SnapshotHistory { get; } = [];

    public List<SonarrInstanceSnapshot> SonarrInstanceSnapshots { get; } = [];

    public List<RadarrInstanceSnapshot> RadarrInstanceSnapshots { get; } = [];

    public List<LibraryFilterValues> FilterValues { get; } = [];


    public Task AppendSnapshotHistoryAsync(ArrApplicationKind kind, string instanceId, string instanceName, DateTimeOffset syncedAt, object payload, CancellationToken cancellationToken)
    {
        SnapshotHistory.Add(payload);
        return Task.CompletedTask;
    }

    public Task ReplaceLibraryHistoryAsync(ArrApplicationKind kind, IReadOnlyList<LibraryChangeHistoryEntry> entries, CancellationToken cancellationToken)
    {
        List<LibraryChangeHistoryEntry> target = kind == ArrApplicationKind.Sonarr
            ? SonarrLibraryHistory
            : RadarrLibraryHistory;

        target.Clear();
        target.AddRange(entries.Where((entry) => entry.Kind == kind));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LibraryChangeHistoryEntry>> GetLibraryHistoryAsync(ArrApplicationKind? kind, CancellationToken cancellationToken)
    {
        if (kind == ArrApplicationKind.Sonarr)
        {
            return Task.FromResult<IReadOnlyList<LibraryChangeHistoryEntry>>(SonarrLibraryHistory);
        }

        if (kind == ArrApplicationKind.Radarr)
        {
            return Task.FromResult<IReadOnlyList<LibraryChangeHistoryEntry>>(RadarrLibraryHistory);
        }

        return Task.FromResult<IReadOnlyList<LibraryChangeHistoryEntry>>([.. SonarrLibraryHistory, .. RadarrLibraryHistory]);
    }

    public Task<bool> HasLibraryHistoryAsync(ArrApplicationKind kind, string instanceId, CancellationToken cancellationToken)
    {
        List<LibraryChangeHistoryEntry> history = kind == ArrApplicationKind.Sonarr
            ? SonarrLibraryHistory
            : RadarrLibraryHistory;

        return Task.FromResult(history.Any((entry) => entry.InstanceId == instanceId));
    }

    public Task ReplaceSonarrInstanceSnapshotsAsync(IReadOnlyList<SonarrInstanceSnapshot> snapshots, CancellationToken cancellationToken)
    {
        SonarrInstanceSnapshots.Clear();
        SonarrInstanceSnapshots.AddRange(snapshots);
        return Task.CompletedTask;
    }

    public Task ReplaceRadarrInstanceSnapshotsAsync(IReadOnlyList<RadarrInstanceSnapshot> snapshots, CancellationToken cancellationToken)
    {
        RadarrInstanceSnapshots.Clear();
        RadarrInstanceSnapshots.AddRange(snapshots);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SonarrInstanceSnapshot>> GetSonarrInstanceSnapshotsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SonarrInstanceSnapshot>>([.. SonarrInstanceSnapshots]);

    public Task<IReadOnlyList<RadarrInstanceSnapshot>> GetRadarrInstanceSnapshotsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RadarrInstanceSnapshot>>([.. RadarrInstanceSnapshots]);

    public Task ReplaceLibraryFilterValuesAsync(IReadOnlyList<LibraryFilterValues> values, CancellationToken cancellationToken)
    {
        FilterValues.Clear();
        FilterValues.AddRange(values);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LibraryFilterValues>> GetLibraryFilterValuesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LibraryFilterValues>>([.. FilterValues]);

}
