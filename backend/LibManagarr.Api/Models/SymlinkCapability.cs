namespace LibManagarr.Api.Models;

/// <summary>
/// Outcome of a real file-system probe. Only <see cref="Unsupported"/> may ever blame the platform,
/// and an unclear I/O error is never folded into it, so a mount that is merely offline stays fixable
/// instead of looking like a missing feature.
/// </summary>
public enum SymlinkCapabilityStatus
{
    NotTested,
    Available,
    PermissionDenied,
    Unsupported,
    StorageUnavailable,
    Failed
}

/// <summary>
/// A probe result belongs to one storage area and to the configuration revision that was in effect
/// while it ran. Reading it back under a different revision reports it as stale rather than as truth.
/// </summary>
public sealed class SymlinkCapabilityCheck
{
    public required string StorageAreaId { get; init; }

    public SymlinkCapabilityStatus Status { get; init; }

    /// <summary>What the probe was doing when it stopped, naming the root it touched.</summary>
    public string? Detail { get; init; }

    public DateTimeOffset? CheckedAt { get; init; }

    public int ConfigRevision { get; init; }

    /// <summary>True when the link test passed under the revision that is still configured.</summary>
    public bool IsCurrent(int currentRevision) =>
        Status != SymlinkCapabilityStatus.NotTested && CheckedAt is not null && ConfigRevision == currentRevision;
}
