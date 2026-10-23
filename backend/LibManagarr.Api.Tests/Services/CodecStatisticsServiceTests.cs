using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class CodecStatisticsServiceTests
{
    private readonly CodecStatisticsService service = new();

    [Fact]
    public void Build_ShouldReuseFileIndexUntilPublishedSnapshotChanges()
    {
        AppSettings firstSnapshot = FilterSettings();
        CodecStatisticsResponse first = service.Build(firstSnapshot, null, null);
        CodecStatisticsResponse repeated = service.Build(firstSnapshot, null, null);
        Assert.Same(first.FilterValues, repeated.FilterValues);
        Assert.Same(first.VideoCodecs[0].MediaFiles[0], repeated.VideoCodecs[0].MediaFiles[0]);

        AppSettings nextSnapshot = FilterSettings();
        nextSnapshot.RadarrSnapshots.Clear();
        CodecStatisticsResponse next = service.Build(nextSnapshot, null, null);
        Assert.NotSame(first.FilterValues, next.FilterValues);
        Assert.DoesNotContain("AV1", next.FilterValues.VideoCodecs);
        Assert.Contains("AV1", first.FilterValues.VideoCodecs);
    }

    [Fact]
    public void Compact_ShouldShareMetadataAcrossAllChartsAndPreserveMembershipAndWeights()
    {
        CodecStatisticsResponse original = service.Build(FilterSettings(), null, null);
        CodecStatisticsResponse compact = CodecStatisticsTransport.Compact(original);
        Assert.Equal(3, compact.Files!.Count);
        foreach ((IReadOnlyList<CodecStatisticItem> Before, IReadOnlyList<CodecStatisticItem> After) pair in new[]
        {
            (original.ContainerFormats, compact.ContainerFormats), (original.Resolutions, compact.Resolutions),
            (original.DynamicRanges, compact.DynamicRanges), (original.VideoCodecs, compact.VideoCodecs),
            (original.AudioCodecs, compact.AudioCodecs), (original.VideoBitrates, compact.VideoBitrates)
        })
        {
            Assert.Equal(pair.Before.Count, pair.After.Count);
            for (int i = 0; i < pair.Before.Count; i++)
            {
                CodecStatisticItem before = pair.Before[i];
                CodecStatisticItem after = pair.After[i];
                Assert.Equal(before.Label, after.Label);
                Assert.Equal(before.Count, after.Count);
                Assert.Equal(before.Percentage, after.Percentage);
                Assert.Equal(before.TotalSizeBytes, after.TotalSizeBytes);
                Assert.Empty(after.MediaFiles);
                Assert.Equal(before.MediaFiles, after.FileIds!.Select(id => compact.Files[id]));
            }
        }
        foreach (var pair in new[] { (original.AudioLanguages, compact.AudioLanguages), (original.SubtitleLanguages, compact.SubtitleLanguages) })
            for (int i = 0; i < pair.Item1.Count; i++)
            {
                Assert.Equal(pair.Item1[i].Label, pair.Item2[i].Label);
                Assert.Equal(pair.Item1[i].Percentage, pair.Item2[i].Percentage);
                Assert.Equal(pair.Item1[i].Count, pair.Item2[i].Count);
                Assert.Equal(pair.Item1[i].TotalSizeBytes, pair.Item2[i].TotalSizeBytes);
                Assert.Equal(pair.Item1[i].MediaFiles.Select(file => file.Title), pair.Item2[i].FileIds!.Select(id => compact.Files[id].Title));
            }
        Assert.Same(original.Counts, compact.Counts);
        Assert.Same(original.FilterValues, compact.FilterValues);
        Assert.All(original.VideoCodecs, item => Assert.NotEmpty(item.MediaFiles));
        Assert.True(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(compact).Length < System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(original).Length);
    }

    [Fact]
    public void Build_ShouldCombineMediaFiltersOnTheSameFileAcrossEveryChart()
    {
        AppSettings settings = FilterSettings();
        LibraryRequestFilter filter = new()
        {
            SonarrInstanceFilter = ["__all__"], RadarrInstanceFilter = ["__all__"],
            Resolutions = ["1080p"], VideoCodecs = ["HEVC"], AudioCodecs = ["AAC"],
            DynamicRanges = ["SDR"], ContainerFormats = ["MKV"],
            AudioLanguages = ["Japanese"], SubtitleLanguages = ["English"],
            VideoBitrateMin = 5_000_000, VideoBitrateMax = 5_000_000
        };
        CodecStatisticsResponse result = service.Build(settings, null, null, filter);
        foreach (IReadOnlyList<CodecStatisticItem> groups in new[] { result.ContainerFormats, result.Resolutions, result.VideoCodecs, result.AudioCodecs, result.DynamicRanges, result.VideoBitrates })
        {
            CodecStatisticItem group = Assert.Single(groups);
            Assert.Equal(1, group.Count);
            Assert.Equal("Matching episode", Assert.Single(group.MediaFiles).Title.Split(" - ").Last());
        }
        Assert.Equal("Japanese", Assert.Single(result.AudioLanguages).Label);
        Assert.Equal("English", Assert.Single(result.SubtitleLanguages).Label);
        Assert.Equal(1, result.Counts["audio-language-filter"]["Japanese"]);
        Assert.Equal(1, result.Counts["sonarr-instance-filter"]["s"]);
        Assert.Equal(0, result.Counts["radarr-instance-filter"]["r"]);
        Assert.Contains("4k", result.FilterValues.Resolutions);
        Assert.Contains("AV1", result.FilterValues.VideoCodecs);
        Assert.Contains("Unknown", result.FilterValues.SubtitleLanguages);
        Assert.DoesNotContain("jap", result.FilterValues.AudioLanguages);
    }

    [Fact]
    public void Build_ShouldExcludeInstancesAndMediaValuesAndKeepOptionsForEmptyResults()
    {
        LibraryRequestFilter filter = new()
        {
            SonarrInstanceFilter = ["s"], RadarrInstanceFilter = ["__all__"],
            ExcludedFilters = ["sonarr-instance-filter", "video-codec-filter"], VideoCodecs = ["HEVC"]
        };
        CodecStatisticsResponse result = service.Build(FilterSettings(), null, null, filter);
        Assert.Equal("AV1", Assert.Single(result.VideoCodecs).Label);
        Assert.Equal("English", Assert.Single(result.AudioLanguages).Label);
        Assert.Equal(1, result.Counts["resolution-filter"]["4k"]);
        filter.Resolutions = ["720p"];
        result = service.Build(FilterSettings(), null, null, filter);
        Assert.Empty(result.VideoCodecs);
        Assert.Empty(result.AudioLanguages);
        Assert.Empty(result.SubtitleLanguages);
        Assert.Equal(0, result.Counts["resolution-filter"]["__all__"]);
        Assert.Contains("1080p", result.FilterValues.Resolutions);
        Assert.Contains("4k", result.FilterValues.Resolutions);
    }

    [Fact]
    public void Build_ShouldMatchUnknownLanguagesAndLeaveDefaultMediaFiltersUnrestricted()
    {
        LibraryRequestFilter filter = new() { SonarrInstanceFilter = ["__all__"], RadarrInstanceFilter = ["__all__"], VideoCodecs = ["__all__"] };
        CodecStatisticsResponse result = service.Build(FilterSettings(), null, null, filter);
        Assert.Equal(3, result.VideoCodecs.Sum(group => group.Count));
        filter.SubtitleLanguages = ["Unknown"];
        result = service.Build(FilterSettings(), null, null, filter);
        Assert.Equal(2, result.VideoCodecs.Sum(group => group.Count));
        Assert.Equal(2, Assert.Single(result.SubtitleLanguages).Count);
        filter.RadarrInstanceFilter = [];
        Assert.Empty(service.Build(FilterSettings(), null, null, filter).VideoCodecs);
    }

    private static AppSettings FilterSettings() => new()
    {
        SonarrSnapshots = [new() { InstanceId = "s", Series = [new() { Title = "Series", Seasons = [new() { SeasonNumber = 1, Episodes = [
            new() { Title = "Matching episode", EpisodeNumber = 1, File = new() { Resolution = "1920x800", VideoCodec = "HEVC", AudioCodec = "AAC", Container = "matroska", VideoFormat = "SDR", AudioLanguages = ["jap", "Japanese"], SubtitleLanguages = ["eng"], VideoBitrate = 5_000_000 } },
            new() { HasFile = false, File = new() { VideoCodec = "Should not appear" } }
        ] }] }] }],
        RadarrSnapshots = [new() { InstanceId = "r", Movies = [
            new() { Title = "4K AV1", File = new() { Resolution = "3840x2160", VideoCodec = "AV1", AudioLanguages = ["English"], VideoBitrate = 6_000_000 } },
            new() { Title = "4K HEVC", File = new() { Resolution = "3840x2160", VideoCodec = "HEVC", AudioLanguages = ["jap"], VideoBitrate = 5_000_000 } }
        ] }]
    };

    [Fact]
    public void Build_ShouldMergeLanguageNamesAndAbbreviationsWithoutCountingAFileTwice()
    {
        AppSettings settings = new() { RadarrSnapshots = [new() { Movies = [
            new() { Title = "First", File = new() { AudioLanguages = ["Japanese", "jap", "jpn"], SubtitleLanguages = ["Japanese", "jap"], Size = 100 } },
            new() { Title = "Second", File = new() { AudioLanguages = ["jap"], SubtitleLanguages = ["jpn"], Size = 200 } },
            new() { Title = "Third", File = new() { AudioLanguages = ["jap", "Japanese", "English"], SubtitleLanguages = ["ja", "English"], Size = 300 } }
        ] }] };
        CodecStatisticsResponse result = service.Build(settings, null, null);
        foreach (IReadOnlyList<LanguageStatisticItem> groups in new[] { result.AudioLanguages, result.SubtitleLanguages })
        {
            Assert.Equal(2, groups.Count);
            LanguageStatisticItem japanese = Assert.Single(groups, group => group.Label == "Japanese");
            Assert.Equal(3, japanese.Count);
            Assert.Equal(83.33, japanese.Percentage);
            Assert.Equal(600, japanese.TotalSizeBytes);
            Assert.Equal(3, japanese.MediaFiles.Count);
            LanguageStatisticItem english = Assert.Single(groups, group => group.Label == "English");
            Assert.Equal(1, english.Count);
            Assert.Equal(16.67, english.Percentage);
        }
    }

    [Theory]
    [InlineData(null, "Unknown")]
    [InlineData(0L, "Unknown")]
    [InlineData(-1L, "Unknown")]
    [InlineData(1L, "< 2 Mbit/s")]
    [InlineData(1_999_999L, "< 2 Mbit/s")]
    [InlineData(2_000_000L, "2–5 Mbit/s")]
    [InlineData(4_999_999L, "2–5 Mbit/s")]
    [InlineData(5_000_000L, "5–10 Mbit/s")]
    [InlineData(9_999_999L, "5–10 Mbit/s")]
    [InlineData(10_000_000L, "10–20 Mbit/s")]
    [InlineData(19_999_999L, "10–20 Mbit/s")]
    [InlineData(20_000_000L, "20–40 Mbit/s")]
    [InlineData(39_999_999L, "20–40 Mbit/s")]
    [InlineData(40_000_000L, "≥ 40 Mbit/s")]
    [InlineData(100_000_000L, "≥ 40 Mbit/s")]
    public void Build_ShouldGroupVideoBitratesWithExclusiveUpperBounds(long? bitrate, string label)
    {
        AppSettings settings = new() { RadarrSnapshots = [new() { Movies = [
            new() { Title = "Movie", File = new() { VideoBitrate = bitrate, Size = 100 } }
        ] }] };
        CodecStatisticItem group = Assert.Single(service.Build(settings, null, null).VideoBitrates);
        Assert.Equal(label, group.Label);
        Assert.Equal(1, group.Count);
        Assert.Equal(100, group.Percentage);
        Assert.Equal(100, group.TotalSizeBytes);
        Assert.Equal("Movie", Assert.Single(group.MediaFiles).Title);
    }

    [Fact]
    public void Build_ShouldApplyInstanceAndFileFiltersToBitratesAndLanguagesTogether()
    {
        AppSettings settings = new()
        {
            RadarrSnapshots = [new() { InstanceId = "r", Movies = [
                new() { File = new() { VideoBitrate = 40_000_000, AudioLanguages = ["French"] } }
            ] }],
            SonarrSnapshots = [new() { InstanceId = "s", InstanceName = "Sonarr", Series = [new() { Title = "Series", Seasons = [new() { SeasonNumber = 1, Episodes = [
                new() { EpisodeNumber = 1, Title = "Episode", File = new() { VideoBitrate = 8_000_000, AudioLanguages = ["ger", "German", "English"], SubtitleLanguages = ["German"], Size = 200 } },
                new() { HasFile = false, File = new() { VideoBitrate = 1_000_000, AudioLanguages = ["French"] } }
            ] }] }] }]
        };
        CodecStatisticsResponse result = service.Build(settings, new HashSet<string> { "s" }, new HashSet<string>());
        Assert.Equal("5–10 Mbit/s", Assert.Single(result.VideoBitrates).Label);
        Assert.Equal(200, result.VideoBitrates[0].TotalSizeBytes);
        Assert.Equal("Series - S01E01 - Episode", Assert.Single(result.VideoBitrates[0].MediaFiles).Title);
        Assert.Equal(2, result.AudioLanguages.Count);
        Assert.All(result.AudioLanguages, group => { Assert.Equal(1, group.Count); Assert.Equal(50, group.Percentage); Assert.Equal(200, group.TotalSizeBytes); });
        Assert.DoesNotContain(result.AudioLanguages, group => group.Label == "French");
        Assert.Equal("German", Assert.Single(result.SubtitleLanguages).Label);
        Assert.Equal(100, result.SubtitleLanguages[0].Percentage);
        CodecStatisticsResponse empty = service.Build(settings, new HashSet<string>(), new HashSet<string>());
        Assert.Empty(empty.VideoBitrates);
        Assert.Empty(empty.AudioLanguages);
        Assert.Empty(empty.SubtitleLanguages);
    }

    [Fact]
    public void Build_ShouldAggregateCategoriesAcrossSonarrAndRadarr()
    {
        AppSettings settings = new()
        {
            SonarrSnapshots =
            [
                new SonarrInstanceSnapshot
                {
                    InstanceId = "sonarr-1",
                    InstanceName = "Sonarr A",
                    Series =
                    [
                        new SonarrSeriesSnapshot
                        {
                            Seasons =
                            [
                                new SonarrSeasonSnapshot
                                {
                                    Episodes =
                                    [
                                        new SonarrEpisodeSnapshot
                                        {
                                            File = new MediaFileSnapshot
                                            {
                                                Container = "mkv",
                                                Resolution = "1080p",
                                                VideoFormat = "HDR10",
                                                VideoCodec = "x265",
                                                AudioCodec = "DTS"
                                            }
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ],
            RadarrSnapshots =
            [
                new RadarrInstanceSnapshot
                {
                    InstanceId = "radarr-1",
                    InstanceName = "Radarr A",
                    Movies =
                    [
                        new RadarrMovieSnapshot
                        {
                            File = new MediaFileSnapshot
                            {
                                Container = "mp4",
                                Resolution = "1080p",
                                VideoFormat = "SDR",
                                VideoCodec = "x264",
                                AudioCodec = "AAC"
                            }
                        }
                    ]
                }
            ]
        };

        CodecStatisticsResponse response = service.Build(settings, null, null);

        Assert.Equal(2, response.ContainerFormats.Count);
        Assert.Equal("1080p", response.Resolutions[0].Label);
        Assert.Equal(100, response.Resolutions[0].Percentage);
    }

    [Fact]
    public void Build_ShouldRespectInstanceFilters()
    {
        AppSettings settings = new()
        {
            SonarrSnapshots =
            [
                new SonarrInstanceSnapshot
                {
                    InstanceId = "sonarr-1",
                    Series =
                    [
                        new SonarrSeriesSnapshot
                        {
                            Seasons =
                            [
                                new SonarrSeasonSnapshot
                                {
                                    Episodes = [new SonarrEpisodeSnapshot { File = new MediaFileSnapshot { VideoCodec = "x265" } }]
                                }
                            ]
                        }
                    ]
                },
                new SonarrInstanceSnapshot
                {
                    InstanceId = "sonarr-2",
                    Series =
                    [
                        new SonarrSeriesSnapshot
                        {
                            Seasons =
                            [
                                new SonarrSeasonSnapshot
                                {
                                    Episodes = [new SonarrEpisodeSnapshot { File = new MediaFileSnapshot { VideoCodec = "x264" } }]
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        HashSet<string> sonarrFilter = ["sonarr-2"];
        CodecStatisticsResponse response = service.Build(settings, sonarrFilter, null);

        Assert.Single(response.VideoCodecs);
        Assert.Equal("x264", response.VideoCodecs[0].Label);
    }

    [Fact]
    public void Build_ShouldNormalizeUnknownValues_WithoutGroupingAsOther()
    {
        AppSettings settings = new()
        {
            RadarrSnapshots =
            [
                new RadarrInstanceSnapshot
                {
                    InstanceId = "radarr-1",
                    Movies =
                    [
                        new RadarrMovieSnapshot { File = new MediaFileSnapshot { VideoCodec = null } },
                        new RadarrMovieSnapshot { File = new MediaFileSnapshot { VideoCodec = "unknown" } },
                        new RadarrMovieSnapshot { File = new MediaFileSnapshot { VideoCodec = "n/a" } },
                        new RadarrMovieSnapshot { File = new MediaFileSnapshot { VideoCodec = "x264" } },
                        new RadarrMovieSnapshot { File = new MediaFileSnapshot { VideoCodec = "x265" } },
                        new RadarrMovieSnapshot { File = new MediaFileSnapshot { VideoCodec = "AV1" } },
                        new RadarrMovieSnapshot { File = new MediaFileSnapshot { VideoCodec = "VP9" } },
                        new RadarrMovieSnapshot { File = new MediaFileSnapshot { VideoCodec = "MPEG2" } },
                        new RadarrMovieSnapshot { File = new MediaFileSnapshot { VideoCodec = "WMV" } },
                        new RadarrMovieSnapshot { File = new MediaFileSnapshot { VideoCodec = "Theora" } },
                        new RadarrMovieSnapshot { File = new MediaFileSnapshot { VideoCodec = "H264" } },
                    ]
                }
            ]
        };

        CodecStatisticsResponse response = service.Build(settings, null, null);

        Assert.Contains(response.VideoCodecs, (item) => item.Label == "Unknown" && item.Count == 3);
        Assert.DoesNotContain(response.VideoCodecs, (item) => item.Label == "Other");
        Assert.True(response.VideoCodecs.Count > 8);
    }

    [Fact]
    public void Build_ShouldNormalizeContainerValues_AndKeepDynamicRangeRaw()
    {
        AppSettings settings = new()
        {
            RadarrSnapshots =
            [
                new RadarrInstanceSnapshot
                {
                    InstanceId = "radarr-1",
                    Movies =
                    [
                        new RadarrMovieSnapshot { File = new MediaFileSnapshot { Container = "matroska", VideoFormat = "hdr10" } },
                        new RadarrMovieSnapshot { File = new MediaFileSnapshot { Container = ".mp4", VideoFormat = "DV HDR10" } },
                        new RadarrMovieSnapshot { File = new MediaFileSnapshot { Container = "avi", VideoFormat = "sdr" } },
                        new RadarrMovieSnapshot { File = new MediaFileSnapshot { Container = "mkv", VideoFormat = "DOVIWithHDR10" } },
                    ]
                }
            ]
        };

        CodecStatisticsResponse response = service.Build(settings, null, null);

        Assert.Contains(response.ContainerFormats, (item) => item.Label == "MKV");
        Assert.Contains(response.ContainerFormats, (item) => item.Label == "MP4");
        Assert.Contains(response.ContainerFormats, (item) => item.Label == "AVI");
        Assert.Contains(response.DynamicRanges, (item) => item.Label == "hdr10");
        Assert.Contains(response.DynamicRanges, (item) => item.Label == "DV HDR10");
        Assert.Contains(response.DynamicRanges, (item) => item.Label == "sdr");
        Assert.Contains(response.DynamicRanges, (item) => item.Label == "DOVIWithHDR10");
    }
}
