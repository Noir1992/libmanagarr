using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Endpoints;

public sealed class LibrarySortEndpointTests
{
    [Theory]
    [InlineData(null, null, "Alpha,Bravo,Charlie,NoDate")]
    [InlineData(LibrarySortField.Alphabetical, LibrarySortDirection.Descending, "NoDate,Charlie,Bravo,Alpha")]
    [InlineData(LibrarySortField.Aired, LibrarySortDirection.Ascending, "Bravo,Charlie,Alpha,NoDate")]
    [InlineData(LibrarySortField.Aired, LibrarySortDirection.Descending, "Alpha,Charlie,Bravo,NoDate")]
    [InlineData(LibrarySortField.Added, LibrarySortDirection.Ascending, "Bravo,Alpha,Charlie,NoDate")]
    [InlineData(LibrarySortField.Added, LibrarySortDirection.Descending, "Charlie,Alpha,Bravo,NoDate")]
    public async Task PostRadarrLibrary_OrdersRowsByRequestedField(
        LibrarySortField? field, LibrarySortDirection? direction, string expected)
    {
        IReadOnlyList<RadarrLibraryEntryResponse> rows = await RadarrRows(field, direction);

        Assert.Equal(expected.Split(','), rows.Select(row => row.MovieTitle));
    }

    [Theory]
    [InlineData(null, null, "Alpha,Bravo,Charlie,NoDate")]
    [InlineData(LibrarySortField.Alphabetical, LibrarySortDirection.Descending, "NoDate,Charlie,Bravo,Alpha")]
    [InlineData(LibrarySortField.Aired, LibrarySortDirection.Ascending, "Bravo,Charlie,Alpha,NoDate")]
    [InlineData(LibrarySortField.Aired, LibrarySortDirection.Descending, "Alpha,Charlie,Bravo,NoDate")]
    [InlineData(LibrarySortField.Added, LibrarySortDirection.Ascending, "Bravo,Alpha,Charlie,NoDate")]
    [InlineData(LibrarySortField.Added, LibrarySortDirection.Descending, "Charlie,Alpha,Bravo,NoDate")]
    public async Task PostSonarrLibrary_OrdersSeriesByRequestedField(
        LibrarySortField? field, LibrarySortDirection? direction, string expected)
    {
        IReadOnlyList<SonarrLibraryEntryResponse> rows = await SonarrRows(field, direction);

        Assert.Equal(expected.Split(','), rows.Select(row => row.SeriesTitle));
    }

    [Fact]
    public async Task PostSonarrLibrary_KeepsSeasonsAndEpisodesOrderedByNumberWhenSeriesOrderFlips()
    {
        string[] expected = ["1,3", "1,2", "1", "1"];

        IReadOnlyList<SonarrLibraryEntryResponse> rows = await SonarrRows(LibrarySortField.Aired, LibrarySortDirection.Descending);

        Assert.Equal(["Alpha", "Charlie", "Bravo", "NoDate"], rows.Select(row => row.SeriesTitle));
        Assert.Equal(expected, rows.Select(row => string.Join(',', row.Seasons.SelectMany(season => season.Episodes)
            .Select(episode => episode.Number))));
    }

    [Fact]
    public void LibraryRequestFilter_BindsSortNamesFromCamelCaseRequestBody()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

        LibraryRequestFilter? filter = JsonSerializer.Deserialize<LibraryRequestFilter>(
            """{"sortField":"aired","sortDirection":"descending"}""", options);

        Assert.Equal(LibrarySortField.Aired, filter!.SortField);
        Assert.Equal(LibrarySortDirection.Descending, filter.SortDirection);
    }

    [Fact]
    public void LibraryRequestFilter_WithoutSortNamesLeavesSortingToTheAlphabeticalDefault()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

        LibraryRequestFilter? filter = JsonSerializer.Deserialize<LibraryRequestFilter>("""{"genres":["comedy"]}""", options);

        Assert.Null(filter!.SortField);
        Assert.Null(filter.SortDirection);
    }

    private static async Task<IReadOnlyList<RadarrLibraryEntryResponse>> RadarrRows(
        LibrarySortField? field, LibrarySortDirection? direction)
    {
        MethodInfo handler = typeof(RadarrEndpoints)
            .GetMethod("PostLibraryAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task<Results<Ok<RadarrLibraryBatchResponse>, NotFound<ProblemDetails>>>)handler.Invoke(
            null, [Filter(field, direction), new StubSettingsService(RadarrSettings()), CancellationToken.None])!;
        RadarrLibraryBatchResponse result = Assert.IsType<Ok<RadarrLibraryBatchResponse>>((await task).Result).Value!;

        return Assert.Single(result.Instances).Rows;
    }

    private static async Task<IReadOnlyList<SonarrLibraryEntryResponse>> SonarrRows(
        LibrarySortField? field, LibrarySortDirection? direction)
    {
        MethodInfo handler = typeof(SonarrEndpoints)
            .GetMethod("PostLibraryAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task<Results<Ok<SonarrLibraryBatchResponse>, NotFound<ProblemDetails>>>)handler.Invoke(
            null, [Filter(field, direction), new StubSettingsService(SonarrSettings()), CancellationToken.None])!;
        SonarrLibraryBatchResponse result = Assert.IsType<Ok<SonarrLibraryBatchResponse>>((await task).Result).Value!;

        return result.Instances.SelectMany(instance => instance.Rows).ToArray();
    }

    private static LibraryRequestFilter Filter(LibrarySortField? field, LibrarySortDirection? direction) => new()
    {
        RadarrInstanceFilter = ["__all__"],
        SonarrInstanceFilter = ["__all__"],
        SortField = field,
        SortDirection = direction,
    };

    private static AppSettings RadarrSettings() => new()
    {
        RadarrInstances = [new() { Id = "r", Name = "Radarr" }],
        RadarrSnapshots = [new() { InstanceId = "r", InstanceName = "Radarr", Movies = RadarrMovies().ToList() }],
    };

    private static IEnumerable<RadarrMovieSnapshot> RadarrMovies() =>
    [
        new()
        {
            MovieId = 1, Title = "Alpha", HasFile = false,
            AirDateUtc = DateTimeOffset.Parse("2023-01-01T00:00:00Z"), AddedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
        },
        new()
        {
            MovieId = 2, Title = "Bravo", HasFile = false,
            AirDateUtc = DateTimeOffset.Parse("2021-01-01T00:00:00Z"), AddedAt = DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
        },
        new()
        {
            MovieId = 3, Title = "Charlie", HasFile = false,
            AirDateUtc = DateTimeOffset.Parse("2022-01-01T00:00:00Z"), AddedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        },
        new() { MovieId = 4, Title = "NoDate", HasFile = false },
    ];

    private static AppSettings SonarrSettings() => new()
    {
        SonarrInstances = [new() { Id = "s", Name = "Sonarr" }],
        SonarrSnapshots = [new() { InstanceId = "s", InstanceName = "Sonarr", Series = SonarrSeries().ToList() }],
    };

    private static IEnumerable<SonarrSeriesSnapshot> SonarrSeries() =>
    [
        new()
        {
            SeriesId = 1, Title = "Alpha", AddedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
            Seasons = [new()
            {
                SeasonNumber = 1,
                // Unordered on purpose: numbering has to be restored regardless of the series order.
                Episodes =
                [
                    new() { EpisodeNumber = 3, AirDateUtc = DateTimeOffset.Parse("2024-01-01T00:00:00Z"), HasFile = false },
                    new() { EpisodeNumber = 1, AirDateUtc = DateTimeOffset.Parse("2020-01-01T00:00:00Z"), HasFile = false },
                ],
            }],
        },
        new()
        {
            SeriesId = 2, Title = "Bravo", AddedAt = DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            Seasons = [new() { SeasonNumber = 1, Episodes = [
                new() { EpisodeNumber = 1, AirDateUtc = DateTimeOffset.Parse("2022-01-01T00:00:00Z"), HasFile = false }] }],
        },
        new()
        {
            SeriesId = 3, Title = "Charlie", AddedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            Seasons = [new() { SeasonNumber = 1, Episodes = [
                new() { EpisodeNumber = 2, AirDateUtc = DateTimeOffset.Parse("2023-01-01T00:00:00Z"), HasFile = false },
                new() { EpisodeNumber = 1, AirDateUtc = DateTimeOffset.Parse("2021-01-01T00:00:00Z"), HasFile = false }] }],
        },
        new()
        {
            SeriesId = 4, Title = "NoDate",
            Seasons = [new() { SeasonNumber = 1, Episodes = [new() { EpisodeNumber = 1, HasFile = false }] }],
        },
    ];

    private sealed class StubSettingsService(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);

        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
    }
}
