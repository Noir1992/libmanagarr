using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Endpoints;

public sealed class LibraryAddedEndpointTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PostLibrary_ShouldFilterByAddedAndReturnDatesAndCounts(bool sonarr, bool exclude)
    {
        DateTimeOffset added = DateTimeOffset.Parse("2026-01-10T12:00:00Z");
        DateTimeOffset?[] dates = [added, added.AddYears(-1), null];
        AppSettings settings = new()
        {
            SonarrInstances = [new() { Id = "s", Name = "Sonarr" }],
            RadarrInstances = [new() { Id = "r", Name = "Radarr" }],
            SonarrSnapshots = [new()
            {
                InstanceId = "s", InstanceName = "Sonarr",
                Series = dates.Select((date, index) => new SonarrSeriesSnapshot
                {
                    SeriesId = index + 1, Title = $"Series {index + 1}", AddedAt = date,
                    Seasons = [new() { SeasonNumber = 1, Episodes = [
                        new() { EpisodeNumber = 1, AirDateUtc = added.AddYears(-2), HasFile = false },
                        new() { EpisodeNumber = 2, AirDateUtc = added.AddYears(-1), HasFile = false }
                    ] }]
                }).ToList()
            }],
            RadarrSnapshots = [new()
            {
                InstanceId = "r", InstanceName = "Radarr",
                Movies = dates.Select((date, index) => new RadarrMovieSnapshot
                {
                    MovieId = index + 1, Title = $"Movie {index + 1}", AddedAt = date, HasFile = false
                }).ToList()
            }]
        };
        LibraryRequestFilter filter = new()
        {
            SonarrInstanceFilter = ["__all__"], RadarrInstanceFilter = ["__all__"],
            AddedFrom = "2026-01-10", AddedTo = "2026-01-10", ExcludedFilters = exclude ? ["added-range"] : []
        };
        int[] expectedIds = exclude ? [2, 3] : [1];
        StubSettingsService service = new(settings);
        MethodInfo handler = (sonarr ? typeof(SonarrEndpoints) : typeof(RadarrEndpoints))
            .GetMethod("PostLibraryAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        if (sonarr)
        {
            var task = (Task<Results<Ok<SonarrLibraryBatchResponse>, NotFound<ProblemDetails>>>)handler.Invoke(null, [filter, service, CancellationToken.None])!;
            SonarrLibraryBatchResponse result = Assert.IsType<Ok<SonarrLibraryBatchResponse>>((await task).Result).Value!;
            IReadOnlyList<SonarrLibraryEntryResponse> rows = Assert.Single(result.Instances).Rows;
            Assert.Equal(expectedIds, rows.Select(row => row.SeriesId));
            Assert.Equal(expectedIds.Select(id => dates[id - 1]), rows.Select(row => row.AddedAt));
            Assert.All(rows, row => Assert.Equal(2, row.TotalEpisodes));
            Assert.Equal(expectedIds.Length, result.Counts["sonarr-instance-filter"]["__all__"]);
            Assert.Equal(expectedIds.Length * 2, result.Counts["episode-type-filter"]["regular"]);
        }
        else
        {
            var task = (Task<Results<Ok<RadarrLibraryBatchResponse>, NotFound<ProblemDetails>>>)handler.Invoke(null, [filter, service, CancellationToken.None])!;
            RadarrLibraryBatchResponse result = Assert.IsType<Ok<RadarrLibraryBatchResponse>>((await task).Result).Value!;
            IReadOnlyList<RadarrLibraryEntryResponse> rows = Assert.Single(result.Instances).Rows;
            Assert.Equal(expectedIds, rows.Select(row => row.MovieId));
            Assert.Equal(expectedIds.Select(id => dates[id - 1]), rows.Select(row => row.AddedAt));
            Assert.Equal(expectedIds.Length, result.Counts["radarr-instance-filter"]["__all__"]);
            Assert.Equal(expectedIds.Length, result.Counts["file-status-filter"]["missing"]);
        }
    }

    private sealed class StubSettingsService(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
