using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

// In-memory store for the release overview so the navigation entry and the releases page share
// one GitHub request per instance instead of one per browser and refresh.
public sealed class ReleaseOverviewCache(TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    // After a failed attempt the next one waits a few minutes instead of a full lifetime, so a
    // brief outage does not silence the update hint for half a day.
    public static readonly TimeSpan FailureRetryDelay = TimeSpan.FromMinutes(5);

    private readonly record struct Snapshot(
        IReadOnlyList<ReleaseVersion> Versions,
        ReleaseOverviewStatus Status,
        DateTimeOffset CheckedAt,
        DateTimeOffset ExpiresAt);

    private Snapshot? snapshot;

    public bool TryGetFresh(bool refresh, out IReadOnlyList<ReleaseVersion> versions, out ReleaseOverviewStatus status, out DateTimeOffset checkedAt)
    {
        if (!refresh && snapshot is Snapshot cached && timeProvider.GetUtcNow() < cached.ExpiresAt)
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
        TimeSpan lifetime = status == ReleaseOverviewStatus.Ok ? Lifetime : FailureRetryDelay;
        snapshot = new Snapshot(versions, status, checkedAt, timeProvider.GetUtcNow() + lifetime);
    }

    // A failed attempt keeps list and timestamp from the last success, so a brief outage neither
    // empties the page nor hides an update hint that was already visible.
    public (IReadOnlyList<ReleaseVersion> Versions, DateTimeOffset CheckedAt) StoreFailure(
        ReleaseOverviewStatus status,
        DateTimeOffset attemptedAt)
    {
        IReadOnlyList<ReleaseVersion> keptVersions = snapshot?.Versions ?? [];
        DateTimeOffset keptCheckedAt = snapshot?.CheckedAt ?? attemptedAt;
        snapshot = new Snapshot(keptVersions, status, keptCheckedAt, timeProvider.GetUtcNow() + FailureRetryDelay);

        return (keptVersions, keptCheckedAt);
    }
}
