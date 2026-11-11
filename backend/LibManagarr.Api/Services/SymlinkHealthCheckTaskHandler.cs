using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>
/// The scheduled half of the health check. Everything about when it runs — the schedule, the activation,
/// the manual run from the task list, the retry after a failure — belongs to the task infrastructure and is
/// reused as it is; this type only says which pass that is. What the pass found goes on the register rows,
/// which is where an operator reads it, and the summary of the pass is the service's own log line, so the
/// two do not have to be told apart here.
///
/// A scheduled run reads the readiness it can already know and never goes looking for a new one: it asks
/// the stored probe results, never the file system. Creating a test link per planned run would make the
/// night pass a second capability probe, and an environment that refused links in the morning has to keep
/// refusing them quietly until something actually changes. A skipped run is a completed one — no failure,
/// no retry, just the next regular slot and one info line naming the reason.
/// </summary>
public sealed class SymlinkHealthCheckTaskHandler(
    SymlinkHealthService health,
    ISettingsService settings,
    SymlinkCapabilityService capability,
    ILogger<SymlinkHealthCheckTaskHandler> logger) : IScheduledTaskHandler
{
    public string TaskKey => "symlinkHealthCheck";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SymlinkSettings configuration = (await settings.GetSnapshotAsync(cancellationToken)).Symlinks;
        if (!configuration.Enabled || !await capability.IsReadyAsync(configuration, cancellationToken))
        {
            (string reason, SymlinkCapabilityStatus status) = await DescribeSkipAsync(configuration, cancellationToken);
            logger.LogInformation("Skipping symlink health check: {Reason}. Capability status: {CapabilityStatus}.", reason, status);
            return;
        }
        logger.LogInformation("Starting the symlink health pass.");
        SymlinkHealthRun run = await health.CheckAllAsync(cancellationToken);
        if (run.Undetermined > 0) throw new IOException($"The state of {run.Undetermined} managed links could not be determined. See the link findings.");
        logger.LogInformation(
            "Symlink health pass finished: {LinksChecked} entries looked at, {Reported} with something to say, {FollowUpsQueued} waiting for their new source.",
            run.LinksChecked, run.Reported, run.FollowUpsQueued);
    }

    /// <summary>
    /// The one line the skip is allowed to leave behind, so it names what the stored results really say.
    /// A broken environment is reported even when the feature is switched off, because the automatic
    /// switch-off after a failed probe is exactly such a case: the state behind the switch is what an
    /// operator has to see, not the switch itself.
    /// </summary>
    private async Task<(string Reason, SymlinkCapabilityStatus Status)> DescribeSkipAsync(SymlinkSettings configuration, CancellationToken cancellationToken)
    {
        SymlinkCapabilityStatus status = SymlinkCapabilityStatus.NotTested;
        string? obstruction = null;
        if (configuration.StorageAreas.Count == 0) obstruction = "no storage area is configured";
        else
        {
            foreach (MediaStorageArea area in configuration.StorageAreas)
            {
                SymlinkCapabilityCheck check = await capability.GetStatusAsync(area.Id, cancellationToken);
                if (check.Status == SymlinkCapabilityStatus.Available && check.IsCurrent(configuration.Revision))
                {
                    status = SymlinkCapabilityStatus.Available;
                    continue;
                }
                status = check.Status;
                obstruction = check.Status != SymlinkCapabilityStatus.NotTested && !check.IsCurrent(configuration.Revision)
                    ? $"the stored result for storage area '{area.Name}' predates the current media configuration"
                    : check.Detail ?? "the stored result says this environment cannot carry the links";
                break;
            }
        }
        if (!configuration.Enabled)
        {
            return obstruction is null
                ? ("the symlink feature is switched off", status)
                : ($"the symlink feature is switched off and {obstruction}", status);
        }
        return (obstruction ?? "the readiness check came back without a current proven environment", status);
    }
}
