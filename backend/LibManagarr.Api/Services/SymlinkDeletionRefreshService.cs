using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>Refreshes confirmed deletions without holding the link operation gate during Sonarr reads.</summary>
public sealed class SymlinkDeletionRefreshService(
    ISymlinkStore store, ISnapshotSyncService snapshots, SymlinkSuggestionService matching,
    ILogger<SymlinkDeletionRefreshService> logger)
{
    public async Task<bool> RunAsync(CancellationToken token)
    {
        var pending = await store.GetDeletionRefreshesAsync(token);
        bool retry = false;
        foreach (var series in pending.GroupBy(r => (r.Pair.SonarrInstanceId, r.Pair.SonarrSeriesId)))
        {
            using IDisposable total = TemporarySymlinkDeleteTiming.Start(logger,
                $"links={string.Join(',', series.Select(r => r.LinkId))} sonarr={series.Key.SonarrInstanceId} series={series.Key.SonarrSeriesId}",
                "background-refresh-total");
            try
            {
                using (TemporarySymlinkDeleteTiming.Current(logger, "refresh-series-history-filters"))
                    await snapshots.SyncSonarrSeriesAfterDeletionAsync(series.Key.SonarrInstanceId, series.Key.SonarrSeriesId, token);
                foreach (var episode in series.GroupBy(r => r.Pair.SonarrEpisodeId))
                {
                    using IDisposable? timing = TemporarySymlinkDeleteTiming.Current(logger, $"refresh-suggestions episode={episode.Key}");
                    await matching.RefreshDeletedEpisodeAsync(episode.First().Pair, token);
                }
                foreach (SymlinkDeletionRefresh completed in series)
                {
                    using IDisposable? timing = TemporarySymlinkDeleteTiming.Current(logger, $"remove-cleanup-display link={completed.LinkId}");
                    await store.RemoveDeletionRefreshAsync(completed.LinkId, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                retry = true;
                logger.LogWarning(exception, "Refreshing deleted links in Sonarr series {SeriesId} is pending.", series.Key.SonarrSeriesId);
            }
        }
        return retry;
    }
}
