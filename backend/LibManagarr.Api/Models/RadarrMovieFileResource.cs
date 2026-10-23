namespace LibManagarr.Api.Models;

public sealed class RadarrMovieFileResource
{
    public int? Id { get; set; }

    public int? MovieId { get; set; }

    public int? CustomFormatScore { get; set; }

    public long? Size { get; set; }

    public string? RelativePath { get; set; }

    public string? Path { get; set; }

    public string? ReleaseGroup { get; set; }

    public List<LanguageResource>? Languages { get; set; }

    public List<LanguageResource>? SubtitleLanguages { get; set; }

    public MediaInfoResource? MediaInfo { get; set; }

    public QualityResource? Quality { get; set; }

    public DateTimeOffset? DateAdded { get; set; }
}
