using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public interface ISnapshotSyncService
{
    Task<SyncInstanceResult> SyncSonarrSeriesAsync(string instanceId, int seriesId, CancellationToken cancellationToken);

    Task<SyncInstanceResult> SyncAsync(SyncInstanceRequest request, CancellationToken cancellationToken);
}
