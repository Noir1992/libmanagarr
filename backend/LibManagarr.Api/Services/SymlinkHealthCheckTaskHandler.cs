namespace LibManagarr.Api.Services;

/// <summary>
/// The scheduled half of the health check. Everything about when it runs — the schedule, the activation,
/// the manual run from the task list, the retry after a failure — belongs to the task infrastructure and is
/// reused as it is; this type only says which pass that is. What the pass found goes on the register rows,
/// which is where an operator reads it, and the summary of the pass is the service's own log line, so the
/// two do not have to be told apart here.
/// </summary>
public sealed class SymlinkHealthCheckTaskHandler(
    SymlinkHealthService health,
    ILogger<SymlinkHealthCheckTaskHandler> logger) : IScheduledTaskHandler
{
    public string TaskKey => "symlinkHealthCheck";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Starting the symlink health pass.");
        SymlinkHealthRun run = await health.CheckAllAsync(cancellationToken);
        logger.LogInformation(
            "Symlink health pass finished: {LinksChecked} entries looked at, {Reported} with something to say, {FollowUpsQueued} waiting for their new source.",
            run.LinksChecked, run.Reported, run.FollowUpsQueued);
    }
}
