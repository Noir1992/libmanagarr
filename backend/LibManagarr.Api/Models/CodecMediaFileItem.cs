namespace LibManagarr.Api.Models;

public sealed class CodecMediaFileItem
{
    public string Title { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string InstanceName { get; set; } = string.Empty;

    public string? TitleSlug { get; set; }

    public long? SizeBytes { get; set; }

    public string? RelativePath { get; set; }

    public string RootTitle { get; set; } = string.Empty;

    // Detail popup payload, mirroring the library details dialog. Movie-only fields stay empty for episode files and vice versa.
    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public bool? Monitored { get; set; }

    public DateTimeOffset? AirDateUtc { get; set; }

    public string? Status { get; set; }

    public DateTimeOffset? AddedAt { get; set; }

    public string? QualityProfileName { get; set; }

    public int? UpgradeUntilCustomFormatScore { get; set; }

    public string? PosterUrl { get; set; }

    public IReadOnlyList<string>? Genres { get; set; }

    public IReadOnlyList<string>? Tags { get; set; }

    public int? CustomFormatScore { get; set; }

    public LibraryMediaDetailsResponse? MediaDetails { get; set; }
}
