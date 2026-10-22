namespace LibManagarr.Api.Services;

public sealed class ScheduledTaskBackgroundService(
    ScheduledTaskService tasks, ScheduledTaskExecutionService execution, TimeProvider clock,
    ILogger<ScheduledTaskBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        HashSet<Task> running = [];
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromMinutes(1), clock);
            do
            {
                running.RemoveWhere(task => task.IsCompleted);
                foreach (var task in await tasks.GetAllAsync(stoppingToken))
                {
                    if (task.Enabled && task.NextRunAt <= clock.GetUtcNow())
                        running.Add(RunSafelyAsync(task.TaskKey, stoppingToken));
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { /* Normal host shutdown. */ }
        finally { await Task.WhenAll(running); }
    }

    private async Task RunSafelyAsync(string key, CancellationToken token)
    {
        try { await execution.RunAsync(key, false, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { /* An interrupted run remains due. */ }
        catch (Exception exception) { logger.LogError(exception, "Could not execute scheduled task {TaskKey}.", key); }
    }
}
