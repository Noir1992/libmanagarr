using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>
/// Durable storage for the sync data (snapshot archive, library change history, the latest
/// instance snapshots, the library filter values), kept
/// outside of settings.json so the settings file stays a small configuration file.
/// </summary>
public interface IHistoryStore
{
    /// <summary>
    /// Appends one instance snapshot to the archive of the given application kind.
    /// </summary>
    Task AppendSnapshotHistoryAsync(ArrApplicationKind kind, string instanceId, string instanceName, DateTimeOffset syncedAt, object payload, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces all persisted library change history entries of the given application kind atomically.
    /// </summary>
    Task ReplaceLibraryHistoryAsync(ArrApplicationKind kind, IReadOnlyList<LibraryChangeHistoryEntry> entries, CancellationToken cancellationToken);

    /// <summary>
    /// Loads the persisted library change history, optionally limited to a single application kind.
    /// </summary>
    Task<IReadOnlyList<LibraryChangeHistoryEntry>> GetLibraryHistoryAsync(ArrApplicationKind? kind, CancellationToken cancellationToken);

    /// <summary>
    /// Indicates whether any library change history exists for the given instance.
    /// </summary>
    Task<bool> HasLibraryHistoryAsync(ArrApplicationKind kind, string instanceId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the persisted latest Sonarr snapshots atomically; the list is the authoritative
    /// state for the kind, so instances missing from it lose their stored snapshot.
    /// </summary>
    Task ReplaceSonarrInstanceSnapshotsAsync(IReadOnlyList<SonarrInstanceSnapshot> snapshots, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the persisted latest Radarr snapshots atomically; the list is the authoritative
    /// state for the kind, so instances missing from it lose their stored snapshot.
    /// </summary>
    Task ReplaceRadarrInstanceSnapshotsAsync(IReadOnlyList<RadarrInstanceSnapshot> snapshots, CancellationToken cancellationToken);

    /// <summary>
    /// Loads the persisted latest Sonarr snapshots, one per instance.
    /// </summary>
    Task<IReadOnlyList<SonarrInstanceSnapshot>> GetSonarrInstanceSnapshotsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Loads the persisted latest Radarr snapshots, one per instance.
    /// </summary>
    Task<IReadOnlyList<RadarrInstanceSnapshot>> GetRadarrInstanceSnapshotsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the persisted library filter values atomically; the list is the authoritative
    /// state, so instances missing from it lose their stored values.
    /// </summary>
    Task ReplaceLibraryFilterValuesAsync(IReadOnlyList<LibraryFilterValues> values, CancellationToken cancellationToken);

    /// <summary>
    /// Loads the persisted library filter values, one entry per instance.
    /// </summary>
    Task<IReadOnlyList<LibraryFilterValues>> GetLibraryFilterValuesAsync(CancellationToken cancellationToken);

}
