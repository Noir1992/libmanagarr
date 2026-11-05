using System.Threading.RateLimiting;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

/// <summary>
/// The permit window follows the settings while it runs: a raise frees the extra permits inside the
/// open window, a lower limit leaves the permits that were already handed out alone, and turning a
/// request down neither consumes a permit nor moves the window the caller has to wait out.
/// </summary>
public sealed class AuthRateLimitWindowTests
{
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly AppSettings settings = new() { AuthProtection = new AuthProtectionSettings { RequestsPerMinute = 3 } };

    [Fact]
    public void TurnsDownRequestsAboveTheLimit_AndNamesTheTimeTheWindowHasLeft()
    {
        AuthRateLimitWindow window = CreateWindow();

        Assert.True(Acquired(window));
        Assert.True(Acquired(window));
        Assert.True(Acquired(window));

        clock.Advance(TimeSpan.FromSeconds(20));

        Assert.False(Acquired(window));
        Assert.Equal(TimeSpan.FromSeconds(40), RemainingWindow(window));
    }

    [Fact]
    public void RaisingTheLimitFreesTheExtraPermitsInsideTheOpenWindow()
    {
        AuthRateLimitWindow window = CreateWindow();
        Assert.True(Acquired(window));
        Assert.True(Acquired(window));
        Assert.True(Acquired(window));
        Assert.False(Acquired(window));

        settings.AuthProtection.RequestsPerMinute = 5;

        Assert.True(Acquired(window));
        Assert.True(Acquired(window));
        Assert.False(Acquired(window));
        Assert.Equal(TimeSpan.FromSeconds(60), RemainingWindow(window));
    }

    [Fact]
    public void LoweringTheLimitLeavesThePermitsThatWereAlreadyGrantedAlone()
    {
        AuthRateLimitWindow window = CreateWindow();
        Assert.True(Acquired(window));
        Assert.True(Acquired(window));

        settings.AuthProtection.RequestsPerMinute = 1;

        // The permits handed out while the higher limit was in force are not taken back, so the open
        // window stays where it is and nothing about it moves to a later time.
        Assert.False(Acquired(window));
        Assert.Equal(TimeSpan.FromSeconds(60), RemainingWindow(window));

        clock.Advance(TimeSpan.FromMinutes(1));

        // The next window counts against the lowered limit from its first request on.
        Assert.True(Acquired(window));
        Assert.False(Acquired(window));
    }

    [Fact]
    public void TurningRequestsDownDoesNotMoveTheWindow()
    {
        settings.AuthProtection.RequestsPerMinute = 1;
        AuthRateLimitWindow window = CreateWindow();
        Assert.True(Acquired(window));

        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(30), RemainingWindow(window));

        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(TimeSpan.FromSeconds(1), RemainingWindow(window));

        // The rejections in between left the window where it was, so it runs out on its original time.
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(Acquired(window));
    }

    [Fact]
    public void TheWindowStartsWithTheFirstRequestOfTheNewPeriod()
    {
        settings.AuthProtection.RequestsPerMinute = 1;
        AuthRateLimitWindow window = CreateWindow();
        Assert.True(Acquired(window));

        clock.Advance(TimeSpan.FromSeconds(61));

        Assert.True(Acquired(window));
        Assert.Equal(TimeSpan.FromSeconds(60), RemainingWindow(window));
    }

    [Fact]
    public void TheWindowIsInUseWhileItIsOpenAndIdleAfterItRanOut()
    {
        AuthRateLimitWindow window = CreateWindow();
        Assert.Null(window.IdleDuration);

        Assert.True(Acquired(window));
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Null(window.IdleDuration);

        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(TimeSpan.FromSeconds(30), window.IdleDuration);
    }

    private AuthRateLimitWindow CreateWindow() => new(new StubSettingsService(settings), clock);

    private static bool Acquired(AuthRateLimitWindow window)
    {
        using RateLimitLease lease = window.AttemptAcquire();
        return lease.IsAcquired;
    }

    private static TimeSpan RemainingWindow(AuthRateLimitWindow window)
    {
        using RateLimitLease lease = window.AttemptAcquire();
        Assert.False(lease.IsAcquired);
        Assert.True(lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter));
        return retryAfter;
    }

    private sealed class StubSettingsService(AppSettings value) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(value);

        public Task<AppSettings> SaveAsync(AppSettings settings, CancellationToken cancellationToken) => Task.FromResult(settings);
    }
}
