namespace LibManagarr.Api.Services;

public interface IScheduledTaskHandler
{
    string TaskKey { get; }
    Task ExecuteAsync(CancellationToken cancellationToken);
}

public sealed class LogCleanupTaskHandler(LogFileService logs, ISettingsService settings) : IScheduledTaskHandler
{
    public string TaskKey => "cleanup";
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logs.UpdateSettings((await settings.GetSnapshotAsync(cancellationToken)).Logging);
        cancellationToken.ThrowIfCancellationRequested();
        logs.CleanupExpiredLogs();
    }
}
