namespace LibManagarr.Api.Models;

/// <summary>
/// A detected pair as the view sees it: the stored row plus the titles the current snapshots have for
/// those ids. The titles are not stored with the row, because a renamed movie stays the same movie and
/// the row must not pretend to know what the instance calls it today.
/// </summary>
public sealed class SymlinkSuggestionDto
{
    public required string Id { get; set; }

    public string MatcherVersion { get; set; } = string.Empty;

    public SymlinkSuggestionStatus Status { get; set; }

    public string RadarrInstanceId { get; set; } = string.Empty;

    public int RadarrMovieId { get; set; }

    public string? RadarrMovieTitle { get; set; }

    public int? RadarrFileId { get; set; }

    /// <summary>Source file as the Radarr instance names it, which is the form a mapping is applied to.</summary>
    public string? RadarrFilePath { get; set; }

    public string SonarrInstanceId { get; set; } = string.Empty;

    public int SonarrSeriesId { get; set; }

    public string? SonarrSeriesTitle { get; set; }

    public int SonarrEpisodeId { get; set; }

    public string? SonarrEpisodeTitle { get; set; }

    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    public List<SymlinkSuggestionReason> Reasons { get; set; } = [];

    public List<SymlinkSuggestionAlternative> Alternatives { get; set; } = [];

    public string? Detail { get; set; }

    public int ConfigRevision { get; set; }

    /// <summary>The paths moved since this row was written, so its preview no longer describes a runnable link.</summary>
    public bool Stale { get; set; }

    public string? LinkId { get; set; }

    public long Revision { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }
}

/// <summary>A declined pair, with the titles that made it recognisable in the first place.</summary>
public sealed class SymlinkRejectionDto
{
    public string RadarrInstanceId { get; set; } = string.Empty;

    public int RadarrMovieId { get; set; }

    public string? RadarrMovieTitle { get; set; }

    public string SonarrInstanceId { get; set; } = string.Empty;

    public int SonarrSeriesId { get; set; }

    public string? SonarrSeriesTitle { get; set; }

    public int SonarrEpisodeId { get; set; }

    public string? SonarrEpisodeTitle { get; set; }

    public string? Detail { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Which pair a stored decision belongs to, as the interface sends it back. Not sealed, because asking
/// what a pair would look like as a link is the same pair plus the file it should point at.
/// </summary>
public class SpecialMoviePairRequest
{
    public required string RadarrInstanceId { get; set; }

    public int RadarrMovieId { get; set; }

    public required string SonarrInstanceId { get; set; }

    public int SonarrSeriesId { get; set; }

    public int SonarrEpisodeId { get; set; }

    public SpecialMoviePair ToPair() =>
        new(RadarrInstanceId, RadarrMovieId, SonarrInstanceId, SonarrSeriesId, SonarrEpisodeId);
}

/// <summary>The reason a pair was declined, shown again when the user looks at the archive.</summary>
public sealed class RejectSuggestionRequest
{
    public string? Detail { get; set; }
}

/// <summary>
/// Confirming an offered pair with the path configuration the preview was shown with. The revision is
/// part of the request because paths that moved since then make that preview describe a different link,
/// and saying so beats quietly building the new one.
/// </summary>
public sealed class ConfirmSuggestionRequest
{
    public int ConfigRevision { get; set; }
}
