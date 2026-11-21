using System.Diagnostics;

namespace LibManagarr.Api.Services;

/// <summary>TEMP: remove after diagnosing deletion latency. Uses monotonic elapsed times and
/// carries the link/request context through async HTTP and persistence calls.</summary>
internal sealed class TemporarySymlinkDeleteTiming : IDisposable
{
    private static readonly AsyncLocal<string?> Context = new();
    private readonly ILogger logger;
    private readonly string context;
    private readonly string phase;
    private readonly string? previous;
    private readonly long started;
    private int disposed;

    public static string? CurrentContext => Context.Value;

    private TemporarySymlinkDeleteTiming(ILogger logger, string context, string phase)
    {
        this.logger = logger;
        this.context = context;
        this.phase = phase;
        previous = Context.Value;
        Context.Value = context;
        started = Stopwatch.GetTimestamp();
        logger.LogInformation("[TEMP symlink-delete] {Context} phase={Phase} started", context, phase);
    }

    public static IDisposable Start(ILogger logger, string context, string phase) => new TemporarySymlinkDeleteTiming(logger, context, phase);

    public static IDisposable? Current(ILogger? logger, string phase) => logger is not null && Context.Value is { } context
        ? Start(logger, context, phase) : null;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        logger.LogInformation("[TEMP symlink-delete] {Context} phase={Phase} ended elapsedMs={ElapsedMs}",
            context, phase, Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1));
        Context.Value = previous;
    }
}
