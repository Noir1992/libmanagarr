using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class LibraryAvailabilityTests
{
    [Theory]
    [InlineData(false, true, 1, "upcoming", false)]
    [InlineData(false, true, null, "upcoming", false)]
    [InlineData(false, true, null, "continuing", false)]
    [InlineData(false, false, -1, "continuing", false)]
    [InlineData(false, true, -1, "continuing", false)]
    [InlineData(true, true, 1, "upcoming", true)]
    [InlineData(true, true, -1, "continuing", true)]
    public void Availability_ShouldRequireAFileRegardlessOfAirStatus(
        bool hasFile, bool monitored, int? airDays, string status, bool available)
    {
        DateTimeOffset now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        SonarrSeriesSnapshot series = new()
        {
            Status = status,
            Seasons = [new() { Episodes = [new()
            {
                HasFile = hasFile,
                Monitored = monitored,
                AirDateUtc = airDays.HasValue ? now.AddDays(airDays.Value) : null
            }] }]
        };

        Assert.Equal(available, LibraryAvailability.IsAvailable(series, now));
    }

    [Theory]
    [InlineData(false, -1, true)]
    [InlineData(true, 1, true)]
    [InlineData(true, -1, false)]
    [InlineData(true, 0, false)]
    public void Availability_ShouldIgnoreOnlyFutureAndUnmonitoredMissingEpisodes(bool monitored, int airDays, bool available)
    {
        DateTimeOffset now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        SonarrSeriesSnapshot series = new()
        {
            Seasons = [new() { Episodes = [
                new() { HasFile = true, Monitored = true, AirDateUtc = now.AddDays(-2) },
                new() { HasFile = false, Monitored = monitored, AirDateUtc = now.AddDays(airDays) }
            ] }]
        };

        Assert.Equal(available, LibraryAvailability.IsAvailable(series, now));
    }
}
