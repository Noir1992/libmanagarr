namespace LibManagarr.Api.Models;

/// <summary>
/// State of a detected Radarr movie / Sonarr special pair. <see cref="WaitingForFile"/> keeps a
/// match alive that has no downloadable file yet, and <see cref="Stale"/> marks a pair whose data
/// has to be looked at again before anything may act on it.
/// </summary>
public enum SymlinkSuggestionStatus
{
    Pending,
    WaitingForFile,
    Rejected,
    Creating,
    Completed,
    Stale,
    Conflict,
    Failed
}

/// <summary>
/// The one identity a decision belongs to. It deliberately leaves the file id out, because a Radarr
/// upgrade replaces the file while the pairing — and with it a user's decline — stays the same.
/// </summary>
public sealed record SpecialMoviePair(
    string RadarrInstanceId,
    int RadarrMovieId,
    string SonarrInstanceId,
    int SonarrSeriesId,
    int SonarrEpisodeId);

/// <summary>A competing source the user has to choose between, so title equality alone never decides.</summary>
public sealed record SymlinkSuggestionAlternative(
    string RadarrInstanceId,
    int RadarrMovieId,
    int? RadarrFileId,
    string? Detail);

/// <summary>
/// A stored finding in two parts: a code the interface translates and the sentence that produced it,
/// so a decision stays readable in the log and in the database after the wording changed.
/// </summary>
public sealed record SymlinkSuggestionReason(string Code, string Detail);

/// <summary>
/// A detected candidate pair with the reasons that produced it. Reasons are stored, not recomputed,
/// so a decision stays explainable after the matcher changes.
/// </summary>
public sealed class SymlinkSuggestion
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Which matcher produced this row; a later matcher version makes old rows comparable, not equal.</summary>
    public string MatcherVersion { get; set; } = string.Empty;

    public SymlinkSuggestionStatus Status { get; set; }

    public SpecialMoviePair Pair { get; set; } = new(string.Empty, 0, string.Empty, 0, 0);

    /// <summary>The file the suggestion would link to. Absent while the movie has no file yet.</summary>
    public int? RadarrFileId { get; set; }

    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    /// <summary>
    /// Why this pair matched, as code plus sentence. Stored rather than recomputed, so a decision stays
    /// explainable after the matcher changes and the interface can label the codes itself.
    /// </summary>
    public List<SymlinkSuggestionReason> Reasons { get; set; } = [];

    public List<SymlinkSuggestionAlternative> Alternatives { get; set; } = [];

    /// <summary>Step-level cause for <see cref="SymlinkSuggestionStatus.Failed"/> and <see cref="SymlinkSuggestionStatus.Conflict"/>.</summary>
    public string? Detail { get; set; }

    public int ConfigRevision { get; set; }

    public string? LinkId { get; set; }

    /// <summary>Guard against two writers deciding on the same pair at the same time.</summary>
    public long Revision { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }
}

/// <summary>
/// A declined pair. Keyed by the pair itself, so neither a movie upgrade nor another sync round
/// brings a decline back; reactivating removes the row instead of rewriting history.
/// </summary>
public sealed class SymlinkRejection
{
    public SpecialMoviePair Pair { get; set; } = new(string.Empty, 0, string.Empty, 0, 0);

    public string? Detail { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
