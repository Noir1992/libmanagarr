using Microsoft.Extensions.DependencyInjection;

namespace LibManagarr.Api.Services;

/// <summary>
/// Runs the candidate search outside the sync lock, which is where the plan wants it: the sync only
/// leaves a note that something moved, and the search over the whole library happens here. The note is
/// also what survives a crash — the pass stores its signature only when it finished, so a run that died
/// halfway is asked for again the next time this service starts.
/// </summary>
public sealed class SymlinkMatchingBackgroundService(
    SymlinkMatchingRequests requests,
    SymlinkSetupRequests setup,
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<SymlinkMatchingBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        requests.Request();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await requests.WaitForRequestAsync(stoppingToken);
                // The same note is what the register's follow-ups are owed to: a synchronized library is the
                // only way it learns that a movie has another file now, and the snapshots for that comparison
                // are already written when this service is woken. Asking before the search starts keeps a
                // search that stops from holding the register back.
                setup.Request();
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                SymlinkSuggestionService matching = scope.ServiceProvider.GetRequiredService<SymlinkSuggestionService>();
                await matching.RefreshAsync(force: false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Symlink matching failed; it is tried again in {RetryDelay}.", RetryDelay);
                try
                {
                    await Task.Delay(RetryDelay, clock, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    /* The host is stopping, and an unfinished pass stays due. */
                }

                requests.Request();
            }
        }
    }
}
