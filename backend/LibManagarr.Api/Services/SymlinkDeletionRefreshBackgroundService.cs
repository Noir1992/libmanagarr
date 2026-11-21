namespace LibManagarr.Api.Services;

public sealed class SymlinkDeletionRefreshBackgroundService(
    SymlinkDeletionRefreshRequests requests, IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<SymlinkDeletionRefreshBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        requests.Request();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await requests.WaitAsync(stoppingToken);
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                bool retry = await scope.ServiceProvider.GetRequiredService<SymlinkDeletionRefreshService>().RunAsync(stoppingToken);
                if (!retry) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Deletion library refresh will be retried."); }

            try
            {
                await requests.WaitOrDelayAsync(TimeSpan.FromSeconds(10), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            requests.Request();
        }
    }
}
