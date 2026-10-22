using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class LibraryFilterCountBuilderTests
{
    [Fact]
    public void Movies_ShouldCountEachItemOncePerValueAndKeepTotalIndependentOfOverlappingValues()
    {
        RadarrInstanceSnapshot snapshot = new()
        {
            Movies = [
                new() { Genres = ["Drama", "DRAMA", "Comedy"], Monitored = true, HasAired = true,
                    File = new() { Resolution = "1920x800", Container = "matroska", AudioLanguages = ["German", "GERMAN", "Japanese"] } },
                new() { Genres = ["Drama"], Monitored = false, HasAired = false,
                    File = new() { Resolution = "1920x1080", Container = ".mkv", AudioLanguages = ["German"] } },
                new() { Genres = [], File = new() }
            ]
        };
        Dictionary<string, Dictionary<string, int>> counts = LibraryFilterCountBuilder.Build(snapshot);
        Assert.Equal(2, counts["genre-filter"]["Drama"]);
        Assert.Equal(1, counts["genre-filter"]["Comedy"]);
        Assert.Equal(3, counts["genre-filter"]["__all__"]);
        Assert.Equal(2, counts["audio-language-filter"]["German"]);
        Assert.Equal(1, counts["audio-language-filter"]["Japanese"]);
        Assert.Equal(3, counts["audio-language-filter"]["__all__"]);
        Assert.Equal(2, counts["resolution-filter"]["1080p"]);
        Assert.Equal(1, counts["resolution-filter"]["Unknown"]);
        Assert.Equal(2, counts["container-format-filter"]["MKV"]);
        Assert.Equal(1, counts["episode-monitored-filter"]["monitored"]);
        Assert.Equal(2, counts["episode-monitored-filter"]["notMonitored"]);
        Assert.Equal(1, counts["aired-filter"]["aired"]);
        Assert.Equal(2, counts["aired-filter"]["notAired"]);
    }

    [Fact]
    public void SeriesMetadata_ShouldCountSeriesWhileMediaValuesCountEpisodes()
    {
        SonarrInstanceSnapshot snapshot = new()
        {
            Series = [new()
            {
                Genres = ["Drama"], Status = "continuing", Profile = new() { Name = "HD" }, Monitored = true,
                Seasons = [new() { Episodes = [
                    new() { AirDateUtc = DateTimeOffset.UtcNow.AddDays(-1), File = new() { VideoCodec = "HEVC", AudioCodec = "AAC", VideoFormat = "HDR10" } },
                    new() { AirDateUtc = DateTimeOffset.UtcNow.AddDays(1), File = new() { VideoCodec = "hevc", AudioCodec = "AAC", VideoFormat = "SDR" } },
                    new() { File = new() { VideoCodec = "AV1", AudioCodec = "FLAC", VideoFormat = "HDR10" } }
                ] }]
            }]
        };
        Dictionary<string, Dictionary<string, int>> counts = LibraryFilterCountBuilder.Build(snapshot);
        Assert.Equal(1, counts["genre-filter"]["Drama"]);
        Assert.Equal(1, counts["genre-filter"]["__all__"]);
        Assert.Equal(1, counts["profile-filter"]["HD"]);
        Assert.Equal(1, counts["status-filter"]["continuing"]);
        Assert.Equal(2, counts["video-codec-filter"]["HEVC"]);
        Assert.Equal(1, counts["video-codec-filter"]["AV1"]);
        Assert.Equal(3, counts["video-codec-filter"]["__all__"]);
        Assert.Equal(2, counts["audio-codec-filter"]["AAC"]);
        Assert.Equal(2, counts["dynamic-range-filter"]["HDR10"]);
        Assert.Equal(1, counts["aired-filter"]["aired"]);
        Assert.Equal(2, counts["aired-filter"]["notAired"]);
    }

    [Fact]
    public void Merge_ShouldAddCountsAcrossInstancesWithoutMutatingSources()
    {
        Dictionary<string, Dictionary<string, int>> first = new()
        {
            ["video-codec-filter"] = new() { ["HEVC"] = 3, ["__all__"] = 3 }
        };
        Dictionary<string, Dictionary<string, int>> second = new()
        {
            ["video-codec-filter"] = new() { ["hevc"] = 2, ["AV1"] = 1, ["__all__"] = 3 }
        };
        Dictionary<string, Dictionary<string, int>> merged = [];
        LibraryFilterCountBuilder.MergeInto(merged, first);
        LibraryFilterCountBuilder.MergeInto(merged, second);
        Assert.Equal(5, merged["video-codec-filter"]["HEVC"]);
        Assert.Equal(1, merged["video-codec-filter"]["AV1"]);
        Assert.Equal(6, merged["video-codec-filter"]["__all__"]);
        Assert.Equal(3, merged["video-codec-filter"].Count);
        Assert.Equal(3, first["video-codec-filter"]["HEVC"]);
        Assert.Equal(2, second["video-codec-filter"]["hevc"]);
    }
}
