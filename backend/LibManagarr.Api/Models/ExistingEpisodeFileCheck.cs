namespace LibManagarr.Api.Models;

public enum ExistingEpisodeFileStatus
{
    Matches,
    TargetMismatch,
    BrokenSymlink,
    RegularFile,
    MissingFile,
    Unavailable,
    NoFile
}

/// <summary>A read-only, point-in-time inspection. It never makes an existing file a managed link.</summary>
public sealed class ExistingEpisodeFileCheck
{
    public ExistingEpisodeFileStatus Status { get; set; }
    public string Detail { get; set; } = string.Empty;
    public string? EpisodePath { get; set; }
    public string? ExpectedTarget { get; set; }
    public string? ActualTarget { get; set; }
    public int? SonarrFileId { get; set; }
    public string? LinkText { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
}
