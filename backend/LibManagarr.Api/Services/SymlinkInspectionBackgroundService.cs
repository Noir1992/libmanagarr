using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>Writes and snapshot changes wake the inspector. Five-minute passes detect external filesystem changes.</summary>
public sealed class SymlinkInspectionBackgroundService(IServiceScopeFactory scopes, SymlinkChangeHub changes,
    SymlinkInspectionCache cache, ILogger<SymlinkInspectionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using SymlinkChangeHub.Subscription subscription = changes.Subscribe();
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(5));
        Task<bool> tick = timer.WaitForNextTickAsync(stoppingToken).AsTask();
        Task<SymlinkChange> next = subscription.Reader.ReadAsync(stoppingToken).AsTask();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Task completed = await Task.WhenAny(next, tick);
                bool inspect = completed == tick;
                if (completed == tick) { await tick; tick = timer.WaitForNextTickAsync(stoppingToken).AsTask(); }
                else { inspect = (await next).Areas.Contains("inspect"); next = subscription.Reader.ReadAsync(stoppingToken).AsTask(); }
                if (!inspect) continue;
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                AppSettings settings = await scope.ServiceProvider.GetRequiredService<ISettingsService>().GetSnapshotAsync(stoppingToken);
                IReadOnlyList<SymlinkSuggestion> rows = await scope.ServiceProvider.GetRequiredService<ISymlinkStore>().GetSuggestionsAsync(stoppingToken);
                cache.Prune(rows.Select(row => row.Id).ToHashSet());
                if (!settings.Symlinks.Enabled) continue;
                bool changed = false;
                foreach (SymlinkSuggestion row in rows)
                {
                    // A fresh scope gives each pass fresh Arr reads, with per-pass request coalescing.
                    ExistingEpisodeFileCheck? check = await scope.ServiceProvider.GetRequiredService<ExistingEpisodeFileInspector>()
                        .CheckAsync(settings, row, stoppingToken);
                    changed |= cache.Store(settings, row, check);
                }
                if (changed) changes.Publish("suggestions");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Symlink display inspection failed; next pass will retry."); }
        }
    }
}
