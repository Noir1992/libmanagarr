namespace LibManagarr.Api.Models;

public enum ReleaseOverviewStatus
{
    Ok,
    Unreachable,
    RateLimited,
    Rejected
}

public sealed class ReleaseOverview
{
    public required string CurrentVersion { get; init; }

    public required bool UpdateAvailable { get; init; }

    public required ReleaseOverviewStatus Status { get; init; }

    public required DateTimeOffset CheckedAt { get; init; }

    public required IReadOnlyList<ReleaseVersion> Versions { get; init; }
}
