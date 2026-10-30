using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class LibrarySorterTests
{
    [Theory]
    [InlineData(LibrarySortDirection.Ascending, "Batman,Office,The Office,A Quiet Place")]
    [InlineData(LibrarySortDirection.Descending, "A Quiet Place,The Office,Office,Batman")]
    public void TitleRules_ApplyToMoviesAndSeriesWithOriginalTitleTieBreak(LibrarySortDirection direction, string expected)
    {
        string[] titles = ["The Office", "Office", "A Quiet Place", "Batman"];
        TitleSortingSettings rules = new() { Enabled = true, IgnoredLeadingWords = ["The", "A"] };
        LibraryRequestFilter filter = new() { SortDirection = direction };
        Assert.Equal(expected.Split(','), LibrarySorter.SortRadarr(titles.Select(title => new RadarrLibraryEntryResponse { MovieTitle = title }), filter, rules).Select(entry => entry.MovieTitle));
        Assert.Equal(expected.Split(','), LibrarySorter.SortSonarr(titles.Select(title => new SonarrLibraryEntryResponse { SeriesTitle = title }), filter, rules).Select(entry => entry.SeriesTitle));
    }

    [Theory]
    [InlineData(LibrarySortDirection.Ascending)]
    [InlineData(LibrarySortDirection.Descending)]
    public void TitleRules_DateTieBreakAlwaysAscendingAndUnknownDatesLast(LibrarySortDirection direction)
    {
        DateTimeOffset date = DateTimeOffset.Parse("2025-01-01T00:00:00Z");
        RadarrLibraryEntryResponse[] movies = [new() { MovieTitle = "The Office", AddedAt = date }, new() { MovieTitle = "Office", AddedAt = date }, new() { MovieTitle = "A Quiet Place", AddedAt = date }, new() { MovieTitle = "A Missing" }];
        var sorted = LibrarySorter.SortRadarr(movies, new() { SortField = LibrarySortField.Added, SortDirection = direction }, new() { Enabled = true, IgnoredLeadingWords = ["The", "A"] });
        Assert.Equal(["Office", "The Office", "A Quiet Place", "A Missing"], sorted.Select(entry => entry.MovieTitle));
    }

    [Fact]
    public void TitleRules_MatchSharedExpectedOrdersForMoviesAndSeries()
    {
        using var fixtures = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test-data", "title-sorting-cases.json")));
        foreach (var fixture in fixtures.RootElement.GetProperty("orderCases").EnumerateArray())
        {
            string[] titles = fixture.GetProperty("titles").EnumerateArray().Select(value => value.GetString()!).ToArray();
            TitleSortingSettings rules = new() { Enabled = true, IgnoredLeadingWords = fixture.GetProperty("words").EnumerateArray().Select(value => value.GetString()!).ToList() };
            foreach (LibrarySortDirection direction in new[] { LibrarySortDirection.Ascending, LibrarySortDirection.Descending })
            {
                string[] expected = fixture.GetProperty(direction == LibrarySortDirection.Ascending ? "ascending" : "descending").EnumerateArray().Select(value => value.GetString()!).ToArray();
                LibraryRequestFilter filter = new() { SortDirection = direction };
                Assert.Equal(expected, LibrarySorter.SortRadarr(titles.Select(title => new RadarrLibraryEntryResponse { MovieTitle = title }), filter, rules).Select(entry => entry.MovieTitle));
                Assert.Equal(expected, LibrarySorter.SortSonarr(titles.Select(title => new SonarrLibraryEntryResponse { SeriesTitle = title }), filter, rules).Select(entry => entry.SeriesTitle));
            }
        }
    }

    public static TheoryData<LibrarySortField, LibrarySortDirection, string> DateOrders => new()
    {
        { LibrarySortField.Aired, LibrarySortDirection.Ascending, "Oldest,Middle,Newest,Unknown" },
        { LibrarySortField.Aired, LibrarySortDirection.Descending, "Newest,Middle,Oldest,Unknown" },
        { LibrarySortField.Added, LibrarySortDirection.Ascending, "Oldest,Middle,Newest,Unknown" },
        { LibrarySortField.Added, LibrarySortDirection.Descending, "Newest,Middle,Oldest,Unknown" },
    };

    [Fact]
    public void SortRadarr_WithoutSortRequest_OrdersTitlesAscendingIgnoringCase()
    {
        string[] expected = ["alice", "Bob", "Carol"];

        string[] titles = LibrarySorter.SortRadarr(
                [Movie("Carol", added: "2020-01-01"), Movie("alice", added: "2021-01-01"), Movie("Bob")],
                new())
            .Select((entry) => entry.MovieTitle)
            .ToArray();

        Assert.Equal(expected, titles);
    }

    [Fact]
    public void SortRadarr_AlphabeticalDescending_OrdersTitlesDescending()
    {
        string[] expected = ["Carol", "Bob", "alice"];

        string[] titles = LibrarySorter
            .SortRadarr([Movie("Carol"), Movie("alice"), Movie("Bob")],
                new() { SortField = LibrarySortField.Alphabetical, SortDirection = LibrarySortDirection.Descending })
            .Select((entry) => entry.MovieTitle)
            .ToArray();

        Assert.Equal(expected, titles);
    }

    [Theory]
    [MemberData(nameof(DateOrders))]
    public void SortRadarr_DateFields_OrderValuesAndAlwaysTrailEntriesWithoutValue(
        LibrarySortField field, LibrarySortDirection direction, string expected)
    {
        string[] titles = LibrarySorter.SortRadarr(Movies(), new() { SortField = field, SortDirection = direction })
            .Select((entry) => entry.MovieTitle)
            .ToArray();

        Assert.Equal(expected.Split(','), titles);
    }

    [Theory]
    [InlineData(LibrarySortDirection.Ascending)]
    [InlineData(LibrarySortDirection.Descending)]
    public void SortRadarr_EqualDates_BreakTiesByTitleAscending(LibrarySortDirection direction)
    {
        string[] expected = ["Alpha", "Bravo", "Charlie"];

        string[] titles = LibrarySorter.SortRadarr(
                [
                    Movie("Charlie", aired: "2024-05-05T00:00:00Z"),
                    Movie("Alpha", aired: "2024-05-05T00:00:00Z"),
                    Movie("Bravo", aired: "2024-05-05T00:00:00Z"),
                ],
                new() { SortField = LibrarySortField.Aired, SortDirection = direction })
            .Select((entry) => entry.MovieTitle)
            .ToArray();

        Assert.Equal(expected, titles);
    }

    [Fact]
    public void SortRadarr_AiredDoesNotUseAddedAt()
    {
        string[] expected = ["EarlyAiredLateAdded", "LateAiredEarlyAdded"];

        string[] titles = LibrarySorter.SortRadarr(
                [
                    Movie("LateAiredEarlyAdded", aired: "2025-01-01T00:00:00Z", added: "2020-01-01T00:00:00Z"),
                    Movie("EarlyAiredLateAdded", aired: "2021-01-01T00:00:00Z", added: "2026-01-01T00:00:00Z"),
                ],
                new() { SortField = LibrarySortField.Aired })
            .Select((entry) => entry.MovieTitle)
            .ToArray();

        Assert.Equal(expected, titles);
    }

    [Fact]
    public void SortSonarr_WithoutSortRequest_OrdersTitlesAscendingIgnoringCase()
    {
        string[] expected = ["alpha", "Bravo", "Charlie"];

        string[] titles = LibrarySorter
            .SortSonarr([Series("Charlie", "2020-01-01T00:00:00Z"), Series("alpha", null), Series("Bravo", null)], new())
            .Select((entry) => entry.SeriesTitle)
            .ToArray();

        Assert.Equal(expected, titles);
    }

    [Theory]
    [InlineData(LibrarySortDirection.Ascending, "Oldest,Middle,Newest,Unknown")]
    [InlineData(LibrarySortDirection.Descending, "Newest,Middle,Oldest,Unknown")]
    public void SortSonarr_Added_OrdersByAddedAt(LibrarySortDirection direction, string expected)
    {
        List<SonarrLibraryEntryResponse> entries =
        [
            Series("Unknown", null),
            Series("Newest", "2026-09-09T00:00:00Z"),
            Series("Oldest", "2024-01-01T00:00:00Z"),
            Series("Middle", "2025-06-15T00:00:00Z"),
        ];

        string[] titles = LibrarySorter.SortSonarr(entries, new() { SortField = LibrarySortField.Added, SortDirection = direction })
            .Select((entry) => entry.SeriesTitle)
            .ToArray();

        Assert.Equal(expected.Split(','), titles);
    }

    [Theory]
    [InlineData(LibrarySortDirection.Ascending, "Early,Later,Latest,Unaired")]
    [InlineData(LibrarySortDirection.Descending, "Latest,Later,Early,Unaired")]
    public void SortSonarr_Aired_UsesLatestAirDateOfAllEpisodes(LibrarySortDirection direction, string expected)
    {
        List<SonarrLibraryEntryResponse> entries =
        [
            Series("Unaired", "2026-01-01T00:00:00Z", new string?[] { null, null }),
            Series("Latest", "2020-01-01T00:00:00Z", new[] { "2021-01-01T00:00:00Z", "2024-06-30T00:00:00Z", null }),
            Series("Early", "2021-01-01T00:00:00Z", new[] { "2022-01-01T00:00:00Z", "2022-02-01T00:00:00Z" }),
            Series("Later", "2022-01-01T00:00:00Z", new[] { "2023-01-01T00:00:00Z", null }),
        ];

        string[] titles = LibrarySorter.SortSonarr(entries, new() { SortField = LibrarySortField.Aired, SortDirection = direction })
            .Select((entry) => entry.SeriesTitle)
            .ToArray();

        Assert.Equal(expected.Split(','), titles);
    }

    [Fact]
    public void SortSonarr_AiredIgnoresAddedAt()
    {
        string[] expected = ["OldEpisodeNewLibrary", "NewEpisodeOldLibrary"];

        string[] titles = LibrarySorter.SortSonarr(
                [
                    Series("NewEpisodeOldLibrary", "2020-01-01T00:00:00Z", new[] { "2025-01-01T00:00:00Z" }),
                    Series("OldEpisodeNewLibrary", "2026-01-01T00:00:00Z", new[] { "2021-01-01T00:00:00Z" }),
                ],
                new() { SortField = LibrarySortField.Aired })
            .Select((entry) => entry.SeriesTitle)
            .ToArray();

        Assert.Equal(expected, titles);
    }

    [Theory]
    [InlineData(LibrarySortDirection.Ascending, "Empty,Alpha,Bravo,Large,Unknown")]
    [InlineData(LibrarySortDirection.Descending, "Large,Alpha,Bravo,Empty,Unknown")]
    public void Size_OrdersMovieSizesAndSeriesTotals_WithUnknownLastAndTitleTieBreak(
        LibrarySortDirection direction, string expected)
    {
        (string Title, long? Size)[] values = [("Unknown", null), ("Large", 9_000_000_000),
            ("Bravo", 3_000_000_000), ("Empty", 0), ("Alpha", 3_000_000_000)];
        LibraryRequestFilter filter = new() { SortField = LibrarySortField.Size, SortDirection = direction };
        Assert.Equal(expected.Split(','), LibrarySorter.SortRadarr(values.Select(value =>
            new RadarrLibraryEntryResponse { MovieTitle = value.Title, Size = value.Size }), filter).Select(entry => entry.MovieTitle));
        Assert.Equal(expected.Split(','), LibrarySorter.SortSonarr(values.Select(value =>
            new SonarrLibraryEntryResponse { SeriesTitle = value.Title, TotalSize = value.Size }), filter).Select(entry => entry.SeriesTitle));
    }

    private static List<RadarrLibraryEntryResponse> Movies() =>
    [
        Movie("Unknown"),
        Movie("Newest", "2024-12-31T00:00:00Z", "2026-09-09T00:00:00Z"),
        Movie("Oldest", "2020-01-01T00:00:00Z", "2024-01-01T00:00:00Z"),
        Movie("Middle", "2022-06-15T00:00:00Z", "2025-06-15T00:00:00Z"),
    ];

    private static RadarrLibraryEntryResponse Movie(string title, string? aired = null, string? added = null) => new()
    {
        MovieTitle = title,
        AirDateUtc = aired is null ? null : DateTimeOffset.Parse(aired),
        AddedAt = added is null ? null : DateTimeOffset.Parse(added),
    };

    private static SonarrLibraryEntryResponse Series(string title, string? added, IReadOnlyList<string?>? aired = null)
    {
        return new SonarrLibraryEntryResponse
        {
            SeriesTitle = title,
            AddedAt = added is null ? null : DateTimeOffset.Parse(added),
            Seasons =
            [
                new SonarrSeasonResponse
                {
                    Number = 1,
                    Episodes = (aired ?? []).Select((value, index) => new SonarrEpisodeResponse
                    {
                        Number = index + 1,
                        Title = $"Episode {index + 1}",
                        AirDateUtc = value is null ? null : DateTimeOffset.Parse(value),
                    }).ToArray(),
                },
            ],
        };
    }
}
