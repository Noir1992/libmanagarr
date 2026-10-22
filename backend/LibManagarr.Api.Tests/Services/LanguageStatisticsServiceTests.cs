using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class LanguageStatisticsServiceTests
{
    private readonly LanguageStatisticsService service = new();

    [Fact]
    public void Build_ShouldAggregateAudioAndSubtitleLanguagesAcrossSonarrAndRadarr()
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
                            Title = "Series A",
                            Seasons =
                            [
                                new SonarrSeasonSnapshot
                                {
                                    SeasonNumber = 1,
                                    Episodes =
                                    [
                                        new SonarrEpisodeSnapshot
                                        {
                                            EpisodeNumber = 1,
                                            Title = "Episode 1",
                                            File = new MediaFileSnapshot
                                            {
                                                Size = 100,
                                                AudioLanguages = ["English", "German"],
                                                SubtitleLanguages = []
                                            }
                                        },
                                        new SonarrEpisodeSnapshot
                                        {
                                            EpisodeNumber = 2,
                                            Title = "Episode 2",
                                            File = new MediaFileSnapshot
                                            {
                                                Size = 200,
                                                AudioLanguages = [],
                                                SubtitleLanguages = ["English"]
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
                            Title = "Movie A",
                            File = new MediaFileSnapshot
                            {
                                Size = 300,
                                AudioLanguages = ["English"],
                                SubtitleLanguages = ["German", "English"]
                            }
                        }
                    ]
                }
            ]
        };

        LanguageStatisticsResponse response = service.Build(settings, null, null);

        Assert.Equal(3, response.AudioLanguages.Count);
        Assert.Equal("English", response.AudioLanguages[0].Label);
        Assert.Equal(2, response.AudioLanguages[0].Count);
        Assert.Equal(50d, response.AudioLanguages[0].Percentage);
        Assert.Equal(400, response.AudioLanguages[0].TotalSizeBytes);

        Assert.Equal("German", response.AudioLanguages[1].Label);
        Assert.Equal(1, response.AudioLanguages[1].Count);
        Assert.Equal(16.67d, response.AudioLanguages[1].Percentage);

        Assert.Equal("Unknown", response.AudioLanguages[2].Label);
        Assert.Equal(1, response.AudioLanguages[2].Count);
        Assert.Equal(33.33d, response.AudioLanguages[2].Percentage);

        Assert.Equal(3, response.SubtitleLanguages.Count);
        Assert.Equal("English", response.SubtitleLanguages[0].Label);
        Assert.Equal(2, response.SubtitleLanguages[0].Count);
        Assert.Equal(50d, response.SubtitleLanguages[0].Percentage);

        Assert.Equal("German", response.SubtitleLanguages[1].Label);
        Assert.Equal(1, response.SubtitleLanguages[1].Count);
        Assert.Equal(16.67d, response.SubtitleLanguages[1].Percentage);

        Assert.Equal("Unknown", response.SubtitleLanguages[2].Label);
        Assert.Equal(1, response.SubtitleLanguages[2].Count);
        Assert.Equal(33.33d, response.SubtitleLanguages[2].Percentage);
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
                            Title = "Series A",
                            Seasons =
                            [
                                new SonarrSeasonSnapshot
                                {
                                    Episodes =
                                    [
                                        new SonarrEpisodeSnapshot
                                        {
                                            Title = "Episode 1",
                                            File = new MediaFileSnapshot
                                            {
                                                AudioLanguages = ["English"],
                                                SubtitleLanguages = ["German"]
                                            }
                                        }
                                    ]
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
                            Title = "Series B",
                            Seasons =
                            [
                                new SonarrSeasonSnapshot
                                {
                                    Episodes =
                                    [
                                        new SonarrEpisodeSnapshot
                                        {
                                            Title = "Episode 2",
                                            File = new MediaFileSnapshot
                                            {
                                                AudioLanguages = ["French"],
                                                SubtitleLanguages = ["French"]
                                            }
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        HashSet<string> sonarrFilter = ["sonarr-2"];
        LanguageStatisticsResponse response = service.Build(settings, sonarrFilter, null);

        Assert.Single(response.AudioLanguages);
        Assert.Equal("French", response.AudioLanguages[0].Label);
        Assert.Single(response.SubtitleLanguages);
        Assert.Equal("French", response.SubtitleLanguages[0].Label);
    }

    [Fact]
    public void Build_ShouldNormalizeUnknownValuesAndDeduplicateRepeatedLanguageLabelsPerFile()
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
                        new RadarrMovieSnapshot
                        {
                            Title = "Movie A",
                            File = new MediaFileSnapshot
                            {
                                AudioLanguages = ["unknown", "English", " english ", "n/a"],
                                SubtitleLanguages = ["-", "German", "German", "none"]
                            }
                        }
                    ]
                }
            ]
        };

        LanguageStatisticsResponse response = service.Build(settings, null, null);

        Assert.Equal(2, response.AudioLanguages.Count);
        Assert.Contains(response.AudioLanguages, (item) => item.Label == "Unknown" && item.Count == 1);
        Assert.Contains(response.AudioLanguages, (item) => item.Label == "English" && item.Count == 1);

        Assert.Equal(2, response.SubtitleLanguages.Count);
        Assert.Contains(response.SubtitleLanguages, (item) => item.Label == "Unknown" && item.Count == 1);
        Assert.Contains(response.SubtitleLanguages, (item) => item.Label == "German" && item.Count == 1);
    }

    [Fact]
    public void Build_ShouldMergeCommonLanguageAliases()
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
                            Title = "Series A",
                            Seasons =
                            [
                                new SonarrSeasonSnapshot
                                {
                                    SeasonNumber = 1,
                                    Episodes =
                                    [
                                        new SonarrEpisodeSnapshot
                                        {
                                            EpisodeNumber = 1,
                                            Title = "Episode 1",
                                            File = new MediaFileSnapshot
                                            {
                                                AudioLanguages = ["ger", "German"],
                                                SubtitleLanguages = ["deu", "German"]
                                            }
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        LanguageStatisticsResponse response = service.Build(settings, null, null);

        Assert.Single(response.AudioLanguages);
        Assert.Equal("German", response.AudioLanguages[0].Label);
        Assert.Equal(1, response.AudioLanguages[0].Count);

        Assert.Single(response.SubtitleLanguages);
        Assert.Equal("German", response.SubtitleLanguages[0].Label);
        Assert.Equal(1, response.SubtitleLanguages[0].Count);
    }
}
