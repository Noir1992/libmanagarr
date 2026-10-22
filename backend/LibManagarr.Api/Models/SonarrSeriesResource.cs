namespace LibManagarr.Api.Models;

public sealed class SonarrSeriesResource
{
    public int Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string TitleSlug { get; init; } = string.Empty;

    public int? QualityProfileId { get; init; }

    public DateTimeOffset? Added { get; init; }

    public string? Status { get; init; }

    public bool? Monitored { get; init; }

    public string? MonitorNewItems { get; init; }

    public IReadOnlyList<SonarrSeriesImage>? Images { get; init; }

    public IReadOnlyList<string>? Genres { get; init; }

    public IReadOnlyList<int>? Tags { get; init; }

    public string? GetPosterRemoteUrl()
    {
        return Images?.FirstOrDefault(i => i.CoverType == "poster")?.RemoteUrl;
    }
}

public sealed class SonarrSeriesImage
{
    public string? CoverType { get; init; }

    public string? Url { get; init; }

    public string? RemoteUrl { get; init; }
}
