using LibManagarr.Api.Services;

namespace LibManagarr.Api.Models;

public sealed class LibraryMediaDetailsResponse
{
    public string? Resolution { get; init; }
    public string? OriginalResolution { get; init; }
    public string? VideoCodec { get; init; }
    public string? AudioCodec { get; init; }
    public string? DynamicRange { get; init; }
    public string? ContainerFormat { get; init; }
    public long? VideoBitrate { get; init; }
    public IReadOnlyList<string> AudioLanguages { get; init; } = [];
    public IReadOnlyList<string> SubtitleLanguages { get; init; } = [];
    public long? Size { get; init; }
    public double? DurationSeconds { get; init; }
    public string? ReleaseGroup { get; init; }
    public string? RelativePath { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    public static LibraryMediaDetailsResponse? From(MediaFileSnapshot file, bool hasFile) => !hasFile ? null : new()
    {
        Resolution = MediaFileValueNormalizer.Resolution(file.Resolution),
        OriginalResolution = file.Resolution,
        VideoCodec = MediaFileValueNormalizer.Value(file.VideoCodec),
        AudioCodec = MediaFileValueNormalizer.Value(file.AudioCodec),
        DynamicRange = MediaFileValueNormalizer.Value(file.VideoFormat),
        ContainerFormat = MediaFileValueNormalizer.Container(file.Container),
        VideoBitrate = file.VideoBitrate,
        AudioLanguages = file.AudioLanguages,
        SubtitleLanguages = file.SubtitleLanguages,
        Size = file.Size,
        DurationSeconds = file.Duration?.TotalSeconds,
        ReleaseGroup = file.ReleaseGroup,
        RelativePath = file.RelativePath,
        CreatedAt = file.CreatedAt,
    };
}
