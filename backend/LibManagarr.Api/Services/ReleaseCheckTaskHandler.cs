namespace LibManagarr.Api.Services;

// The task is the only entry point that asks GitHub on a schedule. The overview endpoint only
// reads the stored snapshot, so the twelve-hour rhythm and the run at every start are the
// moments a check happens; failures stay visible through the task's own run state.
public sealed class ReleaseCheckTaskHandler(ReleaseOverviewService releases) : IScheduledTaskHandler
{
    public string TaskKey => "releaseCheck";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        releases.RefreshAsync(cancellationToken);
}
