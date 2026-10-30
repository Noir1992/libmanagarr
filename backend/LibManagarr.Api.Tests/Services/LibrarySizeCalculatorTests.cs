using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class LibrarySizeCalculatorTests
{
    [Fact]
    public void SeriesSize_SumsAllSeasons_AndCountsMultiEpisodeFilesOnce()
    {
        SonarrSeriesSnapshot series = new()
        {
            Seasons =
            [
                new() { Episodes = [Episode(3_000_000_000, "season1/double.mkv"), Episode(3_000_000_000, "season1/double.mkv")] },
                new() { Episodes = [Episode(4_000_000_000, "season2/episode.mkv"), Episode(200, null), Episode(300, null),
                    new() { HasFile = false, File = new() { Size = 999 } }] },
            ],
        };
        Assert.Equal(7_000_000_500, LibrarySizeCalculator.SeriesSize(series));
    }

    [Fact]
    public void SeriesSize_DistinguishesEmptySeriesFromUnknownFileSizes()
    {
        Assert.Equal(0, LibrarySizeCalculator.SeriesSize(new()));
        Assert.Null(LibrarySizeCalculator.SeriesSize(new()
        {
            Seasons = [new() { Episodes = [Episode(null, "unknown.mkv")] }],
        }));
    }

    private static SonarrEpisodeSnapshot Episode(long? size, string? path) => new()
    {
        HasFile = true,
        File = new() { Size = size, RelativePath = path },
    };
}
