using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

// In-memory store for the release overview so the navigation entry and the releases page share
// one snapshot per instance. Only the releaseCheck scheduled task writes it: the rhythm lives
// in the task schedule, so the store itself never expires its content.
public sealed class ReleaseOverviewCache
{
    private readonly record struct Snapshot(
        IReadOnlyList<ReleaseVersion> Versions,
        ReleaseOverviewStatus Status,
        DateTimeOffset CheckedAt);

    private Snapshot? snapshot;

    public bool TryGet(out IReadOnlyList<ReleaseVersion> versions, out ReleaseOverviewStatus status, out DateTimeOffset checkedAt)
    {
        if (snapshot is Snapshot cached)
        {
            versions = cached.Versions;
            status = cached.Status;
            checkedAt = cached.CheckedAt;
            return true;
        }

        versions = [];
        status = ReleaseOverviewStatus.Ok;
        checkedAt = default;
        return false;
    }

    public void Store(IReadOnlyList<ReleaseVersion> versions, ReleaseOverviewStatus status, DateTimeOffset checkedAt)
    {
        snapshot = new Snapshot(versions, status, checkedAt);
    }

    // A failed attempt keeps list and timestamp from the last success, so a brief outage neither
    // empties the page nor hides an update hint that was already visible.
    public (IReadOnlyList<ReleaseVersion> Versions, DateTimeOffset CheckedAt) StoreFailure(
        ReleaseOverviewStatus status,
        DateTimeOffset attemptedAt)
    {
        IReadOnlyList<ReleaseVersion> keptVersions = snapshot?.Versions ?? [];
        DateTimeOffset keptCheckedAt = snapshot?.CheckedAt ?? attemptedAt;
        snapshot = new Snapshot(keptVersions, status, keptCheckedAt);

        return (keptVersions, keptCheckedAt);
    }
}
