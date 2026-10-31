namespace LibManagarr.Api.Services;

public interface IScheduledTaskHandler
{
    string TaskKey { get; }
    Task ExecuteAsync(CancellationToken cancellationToken);
}

public sealed class LogCleanupTaskHandler(
    LogFileService logs,
    ISettingsService settings,
    ILogger<LogCleanupTaskHandler> logger) : IScheduledTaskHandler
{
    public string TaskKey => "cleanup";
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logs.UpdateSettings((await settings.GetSnapshotAsync(cancellationToken)).Logging);
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Starting log cleanup.");
        int deletedFileCount = logs.CleanupExpiredLogs();
        logger.LogInformation("Completed log cleanup with {DeletedFileCount} deleted files.", deletedFileCount);
    }
}
