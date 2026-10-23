using System.Collections.Concurrent;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public sealed class ScheduledTaskExecutionService(
    IScheduledTaskStore store, ScheduledTaskService tasks, IServiceScopeFactory scopeFactory,
    TimeProvider clock, IHostApplicationLifetime lifetime, ILogger<ScheduledTaskExecutionService> logger)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.Ordinal);

    public async Task<ScheduledTask?> RunAsync(string key, bool manual, CancellationToken cancellationToken)
    {
        ScheduledTaskDefinition definition = tasks.RequireDefinition(key);
        if (manual && !definition.CanRunManually) throw new ScheduledTaskException(403, "Manual execution is not available for this task.");
        SemaphoreSlim gate = gates.GetOrAdd(key, _ => new(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken))
        {
            if (manual) throw new ScheduledTaskException(409, "This task is already running.");
            return null;
        }
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.ApplicationStopping);
        CancellationToken token = linked.Token;
        try
        {
            ScheduledTask current = await tasks.GetAsync(key, token);
            DateTimeOffset startedAt = clock.GetUtcNow();
            if (!manual && (!current.Enabled || current.NextRunAt is null || current.NextRunAt > startedAt)) return null;
            bool completed = false;
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                IScheduledTaskHandler handler = scope.ServiceProvider.GetServices<IScheduledTaskHandler>().Single(handler => handler.TaskKey == key);
                await handler.ExecuteAsync(token);
                token.ThrowIfCancellationRequested();
                completed = true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduled task {TaskKey} failed; persisting its next retry.", key);
            }

            // Only configuration changes increment revision. Retrying this narrow update retains
            // the newest schedule/deactivation even if it changes while the handler is running.
            while (true)
            {
                current = await tasks.GetAsync(key, token);
                DateTimeOffset? next = current.Enabled
                    ? ScheduleCalculator.GetFutureRunAt(current, clock.GetUtcNow(), clock.LocalTimeZone) : null;
                if (await store.UpdateRunAsync(key, current.Revision, completed ? startedAt : null, next, token)) break;
            }
            if (!completed && manual) throw new ScheduledTaskException(500, "Task execution failed. A future retry has been scheduled when enabled.");
            return await tasks.GetAsync(key, token);
        }
        finally { gate.Release(); }
    }
}
