namespace LibManagarr.Api.Models;

/// <summary>
/// One managed link as the view sees it: the register row plus the titles the current snapshots have for
/// its ids. Paths are the way this process sees them, which is the view the entry was written in and the
/// one the storage areas speak; an instance's own spelling of the same file is not shown here.
/// </summary>
public sealed class SymlinkLinkDto
{
    public required string Id { get; set; }

    public string RadarrInstanceId { get; set; } = string.Empty;
    public string? RadarrInstanceName { get; set; }

    public int RadarrMovieId { get; set; }

    public string? RadarrMovieTitle { get; set; }

    public int RadarrFileId { get; set; }

    public string SonarrInstanceId { get; set; } = string.Empty;
    public string? SonarrInstanceName { get; set; }

    public int SonarrSeriesId { get; set; }

    public string? SonarrSeriesTitle { get; set; }

    public int SonarrEpisodeId { get; set; }
    public int? SonarrFileId { get; set; }

    public string? SonarrEpisodeTitle { get; set; }

    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    public string StorageAreaId { get; set; } = string.Empty;

    public string LinkPath { get; set; } = string.Empty;

    /// <summary>The link content as stored, so a relative one can be seen for what it is.</summary>
    public string LinkText { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public SymlinkOperationStatus OperationStatus { get; set; }

    /// <summary>Which setup step still is open; null means nothing was ever started on the Sonarr side.</summary>
    public SymlinkSetupStep? SetupStep { get; set; }

    public List<SymlinkFinding> Findings { get; set; } = [];

    public bool? MonitoringOriginal { get; set; }

    public bool MonitoringDisabled { get; set; }

    public bool AutoFollowUp { get; set; }

    public string? DesiredFilename { get; set; }

    public string? AppliedFilename { get; set; }

    /// <summary>Which Radarr file the entry currently reflects, so a replaced one is visible.</summary>
    public string? SourceVersion { get; set; }

    public SymlinkFileMetadata? ExpectedMetadata { get; set; }

    public SymlinkFileMetadata? ConfirmedMetadata { get; set; }

    public string? SuggestionId { get; set; }

    /// <summary>What a write against this entry has to present, so a stale view cannot overwrite a new one.</summary>
    public long Revision { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>When a health pass last looked; null means the entry was never examined as it stands.</summary>
    public DateTimeOffset? LastCheckedAt { get; set; }

    /// <summary>The last pass that had nothing to say about this entry, which is what makes "checked and quiet" an answer.</summary>
    public DateTimeOffset? LastSuccessfulCheckAt { get; set; }
}

/// <summary>
/// What confirming a pair would do and what stands in the way, read from both live instances. The draft is
/// the entry the confirm would write, without an id, because nothing exists yet — a preview describes an
/// intention and never pretends to be a registered entry.
/// </summary>
public sealed class SymlinkLinkPlanDto
{
    public bool AdoptsExisting { get; set; }
    public bool Runnable { get; set; }

    public required SymlinkLinkDto Draft { get; set; }

    public List<SymlinkFinding> Blockers { get; set; } = [];

    /// <summary>Things the entry has to live with rather than stop for, shown next to it afterwards.</summary>
    public List<SymlinkFinding> Warnings { get; set; } = [];
}

/// <summary>The pair a preview is asked for, and which Radarr file is meant to be its source.</summary>
public sealed class PreviewSymlinkLinkRequest : SpecialMoviePairRequest
{
    /// <summary>
    /// Which file of the movie to link. Left out, the source is only chosen when the movie has exactly
    /// one file; several is a decision for a person, not a tie to break here.
    /// </summary>
    public int? RadarrFileId { get; set; }

    public string? ReplacesId { get; set; }
}

public sealed class SaveSymlinkLinkRequest : SpecialMoviePairRequest
{
    public int? RadarrFileId { get; set; }
    public int ConfigRevision { get; set; }
    public long Revision { get; set; }
    public string? SourceVersion { get; set; }
    public ExistingLinkConfirmation? ExistingLink { get; set; }
}

public sealed class RetrySymlinkRequest
{
    public long Revision { get; set; }
}

/// <summary>Whether later upgrades of the source keep being followed, plus the row that was looked at.</summary>
public sealed class SetSymlinkFollowUpRequest
{
    public bool AutoFollowUp { get; set; }

    public long Revision { get; set; }
}
