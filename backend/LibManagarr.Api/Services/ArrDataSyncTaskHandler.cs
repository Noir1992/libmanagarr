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
        // An empty configuration is not a failing run: the task stays switched on, the next regular slot
        // stays its own, and once an instance is configured the following run simply picks it up. A
        // configured-but-unreachable instance never comes through here; it keeps its per-instance error path.
        if (settings.SonarrInstances.Count == 0 && settings.RadarrInstances.Count == 0)
        {
            logger.LogInformation("Skipping Arr data sync: no Sonarr or Radarr instances are configured.");
            return;
        }
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
            await snapshotSyncService.SyncAsync(
                new SyncInstanceRequest
                {
                    Kind = kind,
                    InstanceId = instance.Id
                },
                cancellationToken);
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
