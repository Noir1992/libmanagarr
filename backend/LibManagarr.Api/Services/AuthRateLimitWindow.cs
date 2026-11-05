using System.Threading.RateLimiting;

namespace LibManagarr.Api.Services;

/// <summary>
/// Permit window for the protected auth, account and backup routes, one instance per source address.
/// The permit count is read from the settings with every request, so changing it there acts without a
/// restart: raising it frees the extra permits inside the open window right away, lowering it applies
/// from the next window on because permits that were already handed out stay handed out. A rejected
/// request never moves the window, so waiting cannot extend the wait.
/// </summary>
public sealed class AuthRateLimitWindow(ISettingsService settingsService, TimeProvider timeProvider) : RateLimiter
{
    public static readonly TimeSpan WindowLength = TimeSpan.FromMinutes(1);

    private readonly object sync = new();
    private DateTimeOffset? windowEndsAt;
    private int usedPermits;

    // The middleware asks the synchronous path first and parks on the asynchronous one when it is
    // turned down. Nothing here waits for a permit, so both answer the same way at once.
    protected override RateLimitLease AttemptAcquireCore(int permitCount) => Acquire(permitCount);

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<RateLimitLease>(Acquire(permitCount));
    }

    // Once the window ran through without a new request, all its permits are available again. From that
    // moment on the framework retires the entries of addresses that stopped sending requests, which
    // keeps the number of live windows bounded.
    public override TimeSpan? IdleDuration
    {
        get
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            lock (sync)
            {
                if (windowEndsAt is not { } endsAt)
                {
                    return null;
                }

                return now < endsAt ? null : now - endsAt;
            }
        }
    }

    // The window answers with its live permit count only; nothing in this API reports limiter
    // statistics, so it keeps no lease counters for that purpose.
    public override RateLimiterStatistics? GetStatistics() => null;

    private RateLimitLease Acquire(int permitCount)
    {
        int permitLimit = ReadPermitLimit();
        DateTimeOffset now = timeProvider.GetUtcNow();
        lock (sync)
        {
            if (windowEndsAt is null || now >= windowEndsAt)
            {
                // The first request of a period opens its window; every later one shares it.
                windowEndsAt = now + WindowLength;
                usedPermits = 0;
            }

            if (usedPermits + permitCount > permitLimit)
            {
                // Turning the request down leaves the window where it is and names the time it really
                // has left, which is what the rejection handler reports as Retry-After.
                return new WindowLease(isAcquired: false, retryAfter: windowEndsAt.Value - now);
            }

            usedPermits += permitCount;
            return new WindowLease(isAcquired: true, retryAfter: TimeSpan.Zero);
        }
    }

    // The rate limiter takes the synchronous path first, so the settings have to be readable without
    // waiting. The settings service serves its snapshot from memory, which keeps this read off the disk.
    private int ReadPermitLimit() =>
        settingsService.GetSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult().AuthProtection.RequestsPerMinute;

    private sealed class WindowLease(bool isAcquired, TimeSpan retryAfter) : RateLimitLease
    {
        public override bool IsAcquired { get; } = isAcquired;

        public override IEnumerable<string> MetadataNames => IsAcquired ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (!IsAcquired && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = retryAfter;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}
