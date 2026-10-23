namespace LibManagarr.Api.Models;

public sealed class MediaFileSnapshot
{
    public TimeSpan? Duration { get; set; }

    public int CustomFormatScore { get; set; }

    public List<string> AudioLanguages { get; set; } = [];

    public List<string> SubtitleLanguages { get; set; } = [];

    public long? VideoBitrate { get; set; }

    public string? VideoCodec { get; set; }

    public long? Size { get; set; }

    public string? ReleaseGroup { get; set; }

    public string? AudioCodec { get; set; }

    public string? Container { get; set; }

    public string? VideoFormat { get; set; }

    public string? Resolution { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public string? RelativePath { get; set; }
}
