namespace LibManagarr.Api.Models;

/// <summary>
/// Where a managed link stands in its own lifecycle. Every value is a point a run can restart from,
/// because the register and the file system never share a transaction.
/// </summary>
public enum SymlinkOperationStatus
{
    Pending,
    Active,
    Conflict,
    DeletePending,

    /// <summary>
    /// The library noticed that Radarr has another file for this movie than the entry points at, and the
    /// entry is owed the way onto it. It is a status of its own because the register has to say "the
    /// source moved and the follow-up is queued" apart from "this entry is fine" and "this entry stopped
    /// for a person".
    /// </summary>
    Updating
}

/// <summary>
/// Which setup step a confirmed link still has open. The steps are separate on purpose: a link that
/// exists is not yet an episode Sonarr recognised, and a recognised episode can still be missing its
/// file information or its final name.
/// </summary>
public enum SymlinkSetupStep
{
    LinkCreated,
    MonitoringDisabled,
    RescanRequested,
    EpisodeAssigned,
    MetadataMatched,
    Renamed,
    Completed
}

/// <summary>
/// File information either read from the chosen Radarr file or confirmed from Sonarr afterwards,
/// together with which of the two it came from. Metadata of a replaced file never counts as
/// confirmed for the new one, which is what <see cref="SourceVersion"/> guards.
/// </summary>
public sealed class SymlinkFileMetadata
{
    /// <summary>"radarr" or "sonarr"; an unattributed value is not a usable provenance.</summary>
    public string? Origin { get; set; }

    public string? ReleaseGroup { get; set; }

    public List<string> Languages { get; set; } = [];

    public string? Quality { get; set; }

    public long? Size { get; set; }
}

/// <summary>One structured health finding. The code stays machine readable so the UI can name an action.</summary>
public sealed record SymlinkFinding(string Code, string? Detail, DateTimeOffset DetectedAt);

/// <summary>
/// One file symlink LibManagarr owns. The Arr identities are stored next to the paths because a
/// Radarr upgrade replaces the file behind the entry while the pairing itself stays the same, and
/// file ids are only meaningful inside the instance that issued them.
/// </summary>
public sealed class SymlinkLink
{
    public string Id { get; set; } = string.Empty;

    public long Revision { get; set; }

    public string RadarrInstanceId { get; set; } = string.Empty;

    public int RadarrMovieId { get; set; }

    public int RadarrFileId { get; set; }

    public string SonarrInstanceId { get; set; } = string.Empty;

    public int SonarrSeriesId { get; set; }

    public int SonarrEpisodeId { get; set; }

    /// <summary>
    /// The pairing this row is about, as the one tuple every caller wants it in. The ids stay separate
    /// columns above, because each is only meaningful inside the instance that issued it.
    /// </summary>
    public SpecialMoviePair Pair => new(RadarrInstanceId, RadarrMovieId, SonarrInstanceId, SonarrSeriesId, SonarrEpisodeId);

    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    public string StorageAreaId { get; set; } = string.Empty;

    /// <summary>Path of the link itself, seen by this process.</summary>
    public string LinkPath { get; set; } = string.Empty;

    /// <summary>Link content as stored, which for a relative link is resolved from LinkPath's folder.</summary>
    public string LinkText { get; set; } = string.Empty;

    /// <summary>The Radarr file this entry was last confirmed to point at, in this process' view.</summary>
    public string SourcePath { get; set; } = string.Empty;

    public SymlinkOperationStatus OperationStatus { get; set; }

    public SymlinkSetupStep? SetupStep { get; set; }

    public string? SuggestionId { get; set; }

    public int? SonarrFileId { get; set; }

    public string? SonarrScanCommandId { get; set; }

    /// <summary>
    /// The rename this link last queued with Sonarr. It is a second field and not the scan's, because a
    /// run that resumes between queueing the rename and its end must not queue a second one — and a
    /// series-wide rename would touch episodes this entry has no claim on.
    /// </summary>
    public string? SonarrRenameCommandId { get; set; }

    /// <summary>What monitoring was before the entry touched it, kept for transparency, never restored silently.</summary>
    public bool? MonitoringOriginal { get; set; }

    public bool MonitoringDisabled { get; set; }

    public bool AutoFollowUp { get; set; }

    public string? DesiredFilename { get; set; }

    public string? AppliedFilename { get; set; }

    /// <summary>Identifies the Sonarr naming rules the desired name was computed with.</summary>
    public string? NamingRevision { get; set; }

    /// <summary>Version of the Radarr file the entry currently reflects, so an older run cannot win.</summary>
    public string? SourceVersion { get; set; }

    public long UpdateRevision { get; set; }

    public SymlinkFileMetadata? ExpectedMetadata { get; set; }

    public SymlinkFileMetadata? ConfirmedMetadata { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? LastCheckedAt { get; set; }

    public DateTimeOffset? LastSuccessfulCheckAt { get; set; }

    public List<SymlinkFinding> Findings { get; set; } = [];
}
