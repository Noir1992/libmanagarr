namespace LibManagarr.Api.Models;

/// <summary>Language-labelled metadata; source titles remain independent matching input.</summary>
public sealed class MediaTitles
{
    public string? OriginalTitle { get; set; }
    public Dictionary<string, MediaTitleTranslation> Translations { get; set; } = [];
}

public sealed class MediaTitleTranslation
{
    public string? Title { get; set; }
    public string Source { get; set; } = string.Empty;
    // Missing includes ambiguous answers identical to Radarr's English fallback.
    public MediaTitleFetchStatus Status { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public DateTimeOffset RetryAt { get; set; }
}

public enum MediaTitleFetchStatus { Available, Missing, Failed }
