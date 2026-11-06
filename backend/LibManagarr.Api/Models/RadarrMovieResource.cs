namespace LibManagarr.Api.Models;

public sealed class RadarrMovieResource
{
    public int? Id { get; set; }

    public string Title { get; init; } = string.Empty;

    public string TitleSlug { get; init; } = string.Empty;

    public int? QualityProfileId { get; set; }

    public int? MovieFileId { get; set; }

    public bool? HasFile { get; set; }

    public DateTimeOffset? Added { get; set; }

    public string? Status { get; set; }

    public DateTimeOffset? InCinemas { get; set; }

    public DateTimeOffset? DigitalRelease { get; set; }

    public DateTimeOffset? PhysicalRelease { get; set; }

    public bool? Monitored { get; set; }

    public IReadOnlyList<RadarrMovieImage>? Images { get; init; }

    public IReadOnlyList<RadarrAlternativeTitleResource>? AlternativeTitles { get; init; }

    public IReadOnlyList<string>? Genres { get; init; }
    public IReadOnlyList<int>? Tags { get; init; }

    public string? GetPosterRemoteUrl()
    {
        return Images?.FirstOrDefault(i => i.CoverType == "poster")?.RemoteUrl;
    }
}

public sealed class RadarrMovieImage
{
    public string? CoverType { get; init; }

    public string? RemoteUrl { get; init; }
}

public sealed class RadarrAlternativeTitleResource
{
    public string? Title { get; init; }

    /// <summary>Radarr's own label for the variant, for example an official or localized title.</summary>
    public string? Type { get; init; }
}
