namespace LibManagarr.Api.Models;

public sealed class RadarrLibraryEntryResponse
{
    public string InstanceName { get; init; } = string.Empty;

    public int MovieId { get; init; }

    public string MovieTitle { get; init; } = string.Empty;

    public string MovieTitleSlug { get; init; } = string.Empty;

    public string QualityProfileName { get; init; } = string.Empty;
    public int? UpgradeUntilCustomFormatScore { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];

    public DateTimeOffset? AddedAt { get; init; }

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset? AirDateUtc { get; init; }

    public bool Monitored { get; init; }

    public bool HasFile { get; init; }

    public int? CustomFormatScore { get; init; }

    public bool HasAired { get; init; }
    public LibraryMediaDetailsResponse? MediaDetails { get; init; }

    public string? Resolution { get; init; }

    public long? Size { get; init; }
}
