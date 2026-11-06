namespace LibManagarr.Api.Models;

/// <summary>
/// What the last matching pass saw for one Arr instance. The signature is the whole point: while it
/// stays the same, another pass would only recompute the same candidates, and a run that dies halfway
/// leaves the previous value in place so the next pass picks the work up again.
/// </summary>
public sealed class SymlinkMatchingState
{
    public string InstanceId { get; set; } = string.Empty;

    public ArrApplicationKind Kind { get; set; }

    public string Signature { get; set; } = string.Empty;

    /// <summary>A stronger matcher has to look at pairs the old one passed over, so its rows age out.</summary>
    public int MatcherVersion { get; set; }

    /// <summary>Path configuration the pass ran under; a new revision means the sources may have moved.</summary>
    public int ConfigRevision { get; set; }

    public DateTimeOffset? CheckedAt { get; set; }
}
