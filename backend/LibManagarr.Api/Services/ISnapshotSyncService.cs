using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public interface ISnapshotSyncService
{
    Task<SyncInstanceResult> SyncAsync(SyncInstanceRequest request, CancellationToken cancellationToken);
}
