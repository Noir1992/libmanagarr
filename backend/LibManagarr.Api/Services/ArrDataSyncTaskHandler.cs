using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public sealed class ArrDataSyncTaskHandler(
    ISettingsService settingsService,
    ISnapshotSyncService snapshotSyncService,
    ILogger<ArrDataSyncTaskHandler> logger) : IScheduledTaskHandler
{
    public string TaskKey => "arrDataSync";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        await RunSyncCycleAsync(settings, cancellationToken);
    }

    private async Task RunSyncCycleAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        foreach (ArrInstanceSettings instance in settings.SonarrInstances)
        {
            await RunSyncSafelyAsync(ArrApplicationKind.Sonarr, instance, cancellationToken);
        }

        foreach (ArrInstanceSettings instance in settings.RadarrInstances)
        {
            await RunSyncSafelyAsync(ArrApplicationKind.Radarr, instance, cancellationToken);
        }
    }

    private async Task RunSyncSafelyAsync(
        ArrApplicationKind kind,
        ArrInstanceSettings instance,
        CancellationToken cancellationToken)
    {
        try
        {
            SyncInstanceResult result = await snapshotSyncService.SyncAsync(
                new SyncInstanceRequest
                {
                    Kind = kind,
                    InstanceId = instance.Id
                },
                cancellationToken);

            logger.LogInformation(
                "Scheduled sync completed for {Kind} instance {InstanceName} with {ItemCount} items.",
                kind,
                instance.Name,
                result.ItemCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Scheduled sync failed for {Kind} instance {InstanceName}.",
                kind,
                instance.Name);
        }
    }
}
