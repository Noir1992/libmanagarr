using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class LibraryMediaFilterTests
{
    [Theory]
    [InlineData("1920x800", "1080p")]
    [InlineData("3840x1600", "4k")]
    [InlineData("1280x720", "720p")]
    [InlineData("1440x1080", "1080p")]
    [InlineData("2560x1440", "1440p")]
    [InlineData("720x576", "576p")]
    [InlineData("720x480", "480p")]
    [InlineData("7680x4320", "4k")]
    [InlineData("0x0", "unknown")]
    [InlineData(null, "unknown")]
    [InlineData("2160p", "4k")]
    [InlineData("4320p", "4k")]
    [InlineData("4K", "4k")]
    [InlineData("UHD", "4k")]
    [InlineData("8k", "4k")]
    [InlineData("1440p", "1440p")]
    [InlineData("1080i", "1080p")]
    [InlineData("1280x960", "1080p")]
    [InlineData("1424x1068", "1080p")]
    [InlineData("1898x804", "1080p")]
    [InlineData("736x544", "576p")]
    [InlineData("720x540", "576p")]
    [InlineData("608x448", "480p")]
    [InlineData("848x480", "480p")]
    [InlineData("672x496", "480p")]
    [InlineData("640x352", "SD")]
    [InlineData("576x432", "SD")]
    [InlineData("640x272", "SD")]
    [InlineData("SD", "SD")]
    [InlineData("garbage", "unknown")]
    [InlineData("Unknown", "unknown")]
    [InlineData("1920 × 800", "1080p")]
    [InlineData(" 1080p ", "1080p")]
    public void Resolution_ShouldCategorizeCroppedAndAnamorphicFiles(string? raw, string expected) =>
        Assert.Equal(expected, MediaFileValueNormalizer.Resolution(raw));

    [Theory]
    [InlineData("matroska", "MKV")]
    [InlineData(".mp4", "MP4")]
    [InlineData(" MPEG-4 ", "MP4")]
    [InlineData("n/a", "Unknown")]
    public void Container_ShouldUseCanonicalValues(string raw, string expected) =>
        Assert.Equal(expected, MediaFileValueNormalizer.Container(raw));

    [Theory]
    [InlineData("resolution-filter")]
    [InlineData("video-codec-filter")]
    [InlineData("audio-codec-filter")]
    [InlineData("dynamic-range-filter")]
    [InlineData("container-format-filter")]
    [InlineData("video-bitrate-range")]
    public void MediaFilters_ShouldMatchMoviesAndEpisodesAndInvertExclusions(string id)
    {
        LibraryRequestFilter filter = id switch
        {
            "resolution-filter" => new() { Resolutions = ["1080p", "720p"] },
            "video-codec-filter" => new() { VideoCodecs = ["hevc"] },
            "audio-codec-filter" => new() { AudioCodecs = ["aac"] },
            "dynamic-range-filter" => new() { DynamicRanges = ["hdr10"] },
            "container-format-filter" => new() { ContainerFormats = ["MKV"] },
            _ => new() { VideoBitrateMin = 12_500_000, VideoBitrateMax = 12_500_000 }
        };
        MediaFileSnapshot match = File();
        MediaFileSnapshot different = new()
        {
            Resolution = "3840x2160", VideoCodec = "AV1", AudioCodec = "FLAC",
            VideoFormat = "SDR", Container = "mp4", VideoBitrate = 13_000_000
        };
        LibraryFilterMatcher matcher = new(filter);
        Assert.True(matcher.MatchesMovie(new() { File = match }));
        Assert.False(matcher.MatchesMovie(new() { File = different }));
        Assert.True(matcher.MatchesEpisode(new() { File = match }, DateTimeOffset.UtcNow));
        Assert.False(matcher.MatchesEpisode(new() { File = different }, DateTimeOffset.UtcNow));
        filter.ExcludedFilters = [id];
        matcher = new(filter);
        Assert.False(matcher.MatchesMovie(new() { File = match }));
        Assert.True(matcher.MatchesMovie(new() { File = different }));
        Assert.False(matcher.MatchesEpisode(new() { File = match }, DateTimeOffset.UtcNow));
        Assert.True(matcher.MatchesEpisode(new() { File = different }, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CombinedFilters_ShouldRequireTheSameEpisodeToMatch()
    {
        LibraryFilterMatcher matcher = new(new() { Resolutions = ["1080p"], VideoCodecs = ["HEVC"] });
        Assert.False(matcher.MatchesEpisode(new() { File = new() { Resolution = "1920x1080", VideoCodec = "AV1" } }, DateTimeOffset.UtcNow));
        Assert.False(matcher.MatchesEpisode(new() { File = new() { Resolution = "3840x2160", VideoCodec = "HEVC" } }, DateTimeOffset.UtcNow));
        Assert.True(matcher.MatchesEpisode(new() { File = File() }, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public void MissingBitrate_ShouldNotMatchAnActiveRange(long? bitrate)
    {
        RadarrMovieSnapshot movie = new() { File = new() { VideoBitrate = bitrate } };
        LibraryRequestFilter filter = new() { VideoBitrateMax = 10_000_000 };
        Assert.False(new LibraryFilterMatcher(filter).MatchesMovie(movie));
        filter.ExcludedFilters = ["video-bitrate-range"];
        Assert.True(new LibraryFilterMatcher(filter).MatchesMovie(movie));
        Assert.True(new LibraryFilterMatcher(new()).MatchesMovie(movie));
    }

    [Fact]
    public void UnknownValues_ShouldBeSelectableWithoutBeingMistakenForSdr()
    {
        LibraryFilterMatcher unknown = new(new() { DynamicRanges = ["Unknown"], ContainerFormats = ["Unknown"] });
        Assert.True(unknown.MatchesMovie(new()));
        Assert.False(new LibraryFilterMatcher(new() { DynamicRanges = ["SDR"] }).MatchesMovie(new()));
    }

    [Fact]
    public void FilterValues_ShouldNormalizeAndDeduplicateMediaValuesForBothSources()
    {
        MediaFileSnapshot first = File();
        MediaFileSnapshot second = File();
        second.Resolution = "1920x1080";
        second.Container = ".mkv";
        second.VideoCodec = "hevc";
        LibraryFilterValues radarr = LibraryFilterValuesBuilder.Build("r", new RadarrInstanceSnapshot
        {
            Movies = [new() { File = first }, new() { File = second }, new()]
        });
        LibraryFilterValues sonarr = LibraryFilterValuesBuilder.Build("s", new SonarrInstanceSnapshot
        {
            Series = [new() { Seasons = [new() { Episodes = [new() { File = first }, new() { File = second }, new()] }] }]
        });
        foreach (LibraryFilterValues values in new[] { radarr, sonarr })
        {
            Assert.Equal(LibraryFilterValues.CurrentVersion, values.Version);
            Assert.Equal(new[] { "1080p", "unknown" }, values.Resolutions.Order());
            Assert.Equal(new[] { "MKV", "Unknown" }, values.ContainerFormats.Order());
            Assert.Equal(2, values.VideoCodecs.Count);
            Assert.Contains("HEVC", values.VideoCodecs);
            Assert.Contains("Unknown", values.VideoCodecs);
            Assert.Equal(new[] { "AAC", "Unknown" }, values.AudioCodecs.Order());
            Assert.Equal(new[] { "HDR10", "Unknown" }, values.DynamicRanges.Order());
        }
    }

    [Theory]
    [InlineData("4k", "3840x1600")]
    [InlineData("2160p", "3840x2160")]
    [InlineData("4320p", "7680x4320")]
    [InlineData("1440p", "2560x1440")]
    [InlineData("SD", "640x352")]
    [InlineData("unknown", "invalid")]
    public void ResolutionGroups_ShouldMatchFilesInBothSourcesIncludingLegacySelections(string selection, string raw)
    {
        LibraryFilterMatcher matcher = new(new() { Resolutions = [selection] });
        Assert.True(matcher.MatchesMovie(new() { File = new() { Resolution = raw } }));
        Assert.True(matcher.MatchesEpisode(new() { File = new() { Resolution = raw } }, DateTimeOffset.UtcNow));
        Assert.False(matcher.MatchesMovie(new() { File = new() { Resolution = "1280x720" } }));
    }

    [Fact]
    public void ResolutionGroups_ShouldUseTheSameValuesForOptionsCountsAndDetails()
    {
        RadarrInstanceSnapshot snapshot = new() { Movies = [
            new() { File = new() { Resolution = "2160p" } },
            new() { File = new() { Resolution = "3840x1600" } },
            new() { File = new() { Resolution = "640x352" } },
            new() { File = new() { Resolution = "invalid" } }
        ] };
        LibraryFilterValues values = LibraryFilterValuesBuilder.Build("radarr", snapshot);
        Assert.True(values.Resolutions.SetEquals(["4k", "SD", "unknown"]));
        Assert.Equal(2, values.Counts["resolution-filter"]["4k"]);
        Assert.Equal(1, values.Counts["resolution-filter"]["SD"]);
        Assert.Equal(1, values.Counts["resolution-filter"]["unknown"]);
        Assert.Equal("4k", LibraryMediaDetailsResponse.From(snapshot.Movies[1].File, true)!.Resolution);
        Assert.Equal("3840x1600", LibraryMediaDetailsResponse.From(snapshot.Movies[1].File, true)!.OriginalResolution);
    }

    private static MediaFileSnapshot File() => new()
    {
        Resolution = "1920x800", VideoCodec = " HEVC ", AudioCodec = "AAC",
        VideoFormat = "HDR10", Container = "matroska", VideoBitrate = 12_500_000
    };
}
