using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class LibraryAvailabilityTests
{
    [Theory]
    [InlineData(false, true, 1, LibraryStatus.Missing)]
    [InlineData(false, true, null, LibraryStatus.Missing)]
    [InlineData(false, false, -1, LibraryStatus.Missing)]
    [InlineData(true, true, 1, LibraryStatus.Present)]
    [InlineData(true, true, -1, LibraryStatus.Present)]
    public void Status_ShouldBeMissingWithoutAnyFileAndPresentWhenEveryMonitoredEpisodeHasOne(
        bool hasFile, bool monitored, int? airDays, LibraryStatus expected)
    {
        DateTimeOffset now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        SonarrSeriesSnapshot series = new()
        {
            Seasons = [new() { Episodes = [new()
            {
                HasFile = hasFile,
                Monitored = monitored,
                AirDateUtc = airDays.HasValue ? now.AddDays(airDays.Value) : null
            }] }]
        };

        Assert.Equal(expected, LibraryAvailability.GetStatus(series));
    }

    [Theory]
    [InlineData(true, -1, LibraryStatus.Partial)]
    [InlineData(true, 1, LibraryStatus.Partial)]
    [InlineData(false, -1, LibraryStatus.Present)]
    [InlineData(false, 1, LibraryStatus.Present)]
    public void Status_ShouldBePartialOnlyWhenMonitoredEpisodesLackAFile(bool monitored, int airDays, LibraryStatus expected)
    {
        DateTimeOffset now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        SonarrSeriesSnapshot series = new()
        {
            Seasons = [new() { Episodes = [
                new() { HasFile = true, Monitored = true, AirDateUtc = now.AddDays(-2) },
                new() { HasFile = false, Monitored = monitored, AirDateUtc = now.AddDays(airDays) }
            ] }]
        };

        Assert.Equal(expected, LibraryAvailability.GetStatus(series));
    }

    [Theory]
    [InlineData(LibraryStatus.Missing, "missing")]
    [InlineData(LibraryStatus.Partial, "partial")]
    [InlineData(LibraryStatus.Present, "present")]
    public void WireValue_ShouldMatchTheFileStatusFilterValues(LibraryStatus status, string wireValue) =>
        Assert.Equal(wireValue, status.ToWireValue());
}
