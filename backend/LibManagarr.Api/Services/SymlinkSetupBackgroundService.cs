using Microsoft.Extensions.DependencyInjection;

namespace LibManagarr.Api.Services;

/// <summary>
/// Runs the owed managed-link work outside a request. The first pass happens on every start, which is
/// what finishes an interrupted operation by itself; after that a pass runs whenever something asked for
/// one and comes back on its own while a row still waits for an instance to answer. Rows that wait for a
/// decision are deliberately left alone, because a timer cannot make that decision.
/// </summary>
public sealed class SymlinkSetupBackgroundService(
    SymlinkSetupRequests requests,
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<SymlinkSetupBackgroundService> logger) : BackgroundService
{
    /// <summary>
    /// How long a row that waits on a queued command is left alone before the same pass looks again. It is
    /// short because the wait is for Sonarr's queue, not for a person.
    /// </summary>
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        requests.Request();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await requests.WaitForRequestAsync(stoppingToken);
                int waiting;
                await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
                {
                    waiting = await scope.ServiceProvider
                        .GetRequiredService<SymlinkSetupRunner>()
                        .RunDueSetupsAsync(stoppingToken);
                }

                if (waiting > 0)
                {
                    await Task.Delay(PollDelay, clock, stoppingToken);
                    requests.Request();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The managed link setup pass failed; it is tried again in {RetryDelay}.", RetryDelay);
                try
                {
                    await Task.Delay(RetryDelay, clock, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    /* The host is stopping, and an unfinished pass stays owed. */
                }

                requests.Request();
            }
        }
    }
}
