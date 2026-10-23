using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class LibraryAddedFilterTests
{
    [Theory]
    [InlineData("2026-01-10T00:00:00Z", "2026-01-10", "2026-01-10", false, true)]
    [InlineData("2026-01-10T23:59:59.9999999Z", "2026-01-10", "2026-01-10", false, true)]
    [InlineData("2026-01-11T00:30:00+02:00", "2026-01-10", "2026-01-10", false, true)]
    [InlineData("2026-01-09T23:59:59Z", "2026-01-10", "2026-01-10", false, false)]
    [InlineData("2026-01-11T00:00:00Z", "2026-01-10", "2026-01-10", false, false)]
    [InlineData("2026-01-11T12:00:00Z", "2026-01-10", null, false, true)]
    [InlineData("2026-01-09T12:00:00Z", null, "2026-01-10", false, true)]
    [InlineData("2026-01-10T12:00:00Z", "2026-01-11", "2026-01-09", false, false)]
    [InlineData(null, "2026-01-10", null, false, false)]
    [InlineData(null, null, "2026-01-10", false, false)]
    [InlineData(null, null, null, false, true)]
    [InlineData(null, "invalid", "invalid", false, true)]
    [InlineData("2026-01-10T12:00:00Z", "2026-01-10", "2026-01-10", true, false)]
    [InlineData("2026-01-09T12:00:00Z", "2026-01-10", "2026-01-10", true, true)]
    [InlineData(null, "2026-01-10", "2026-01-10", true, true)]
    [InlineData(null, null, null, true, true)]
    public void AddedRange_ShouldUseEntityDatesForMoviesAndSeries(
        string? added, string? from, string? to, bool exclude, bool expected)
    {
        DateTimeOffset? addedAt = added is null ? null : DateTimeOffset.Parse(added);
        LibraryFilterMatcher matcher = new(new()
        {
            AddedFrom = from, AddedTo = to, ExcludedFilters = exclude ? ["added-range"] : []
        });

        Assert.Equal(expected, matcher.MatchesMovie(new() { AddedAt = addedAt, HasFile = false }));
        Assert.Equal(expected, matcher.MatchesSeries(new() { AddedAt = addedAt }, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void AddedRange_ShouldCombineWithOtherFiltersAndNeverUseFileImportOrAirDates()
    {
        DateTimeOffset added = DateTimeOffset.Parse("2026-01-10T12:00:00Z");
        DateTimeOffset older = added.AddYears(-1);
        LibraryFilterMatcher matcher = new(new()
        {
            AddedFrom = "2026-01-10", AddedTo = "2026-01-10", Statuses = ["released"]
        });

        Assert.True(matcher.MatchesMovie(new() { AddedAt = added, Status = "released", AirDateUtc = older, File = new() { CreatedAt = older } }));
        Assert.False(matcher.MatchesMovie(new() { AddedAt = older, Status = "released", AirDateUtc = added, File = new() { CreatedAt = added } }));
        Assert.False(matcher.MatchesMovie(new() { AddedAt = added, Status = "announced" }));
        Assert.False(matcher.MatchesMovie(new() { Status = "released", File = new() { CreatedAt = added } }));
        Assert.True(matcher.MatchesEpisode(new() { AirDateUtc = older, HasFile = false }, added));
    }
}
