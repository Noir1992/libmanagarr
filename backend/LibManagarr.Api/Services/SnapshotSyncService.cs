using Microsoft.AspNetCore.Http;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public sealed class SnapshotSyncService(
    ISettingsService settingsService,
    IHistoryStore historyStore,
    SonarrClient sonarrClient,
    RadarrClient radarrClient,
    SymlinkMatchingRequests symlinkMatching,
    ILogger<SnapshotSyncService> logger)
    : ISnapshotSyncService
{
    private static readonly SemaphoreSlim SyncLock = new(1, 1);

    public async Task<SyncInstanceResult> SyncAsync(
        SyncInstanceRequest request,
        CancellationToken cancellationToken)
    {
        await SyncLock.WaitAsync(cancellationToken);
        try
        {
            AppSettings settings = await settingsService.GetAsync(cancellationToken);

            SyncInstanceResult result = request.Kind switch
            {
                ArrApplicationKind.Sonarr => await SyncSonarrAsync(request.InstanceId, settings, cancellationToken),
                ArrApplicationKind.Radarr => await SyncRadarrAsync(request.InstanceId, settings, cancellationToken),
                _ => throw new ApiProblemException(
                    StatusCodes.Status400BadRequest,
                    "Invalid application kind",
                    "Unsupported Arr application kind.")
            };

            return result;
        }
        finally
        {
            SyncLock.Release();
        }
    }

    private async Task<SyncInstanceResult> SyncSonarrAsync(
        string instanceId,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        ArrInstanceSettings? instance = settings.SonarrInstances.FirstOrDefault(
            currentInstance => currentInstance.Id == instanceId);

        if (instance is null)
        {
            logger.LogWarning("Sonarr instance {InstanceId} was not found for snapshot sync.", instanceId);
            throw new ApiProblemException(
                StatusCodes.Status404NotFound,
                "Sonarr instance not found",
                $"No Sonarr instance exists for id '{instanceId}'.");
        }

        logger.LogInformation("Starting Sonarr snapshot sync for instance {InstanceName}.", instance.Name);
        SonarrInstanceSnapshot? previousSnapshot = settings.SonarrSnapshots.FirstOrDefault(
            currentSnapshot => currentSnapshot.InstanceId == instanceId);
        bool hasSonarrLibraryHistory = await historyStore.HasLibraryHistoryAsync(
            ArrApplicationKind.Sonarr,
            instanceId,
            cancellationToken);
        SonarrInstanceSnapshot snapshot = await sonarrClient.FetchSnapshotAsync(instance, cancellationToken);
        DateTimeOffset historyStart = previousSnapshot?.SyncedAt ?? GetSonarrBootstrapDate(snapshot);
        if (!hasSonarrLibraryHistory)
        {
            historyStart = GetSonarrBootstrapDate(snapshot);
        }
        IReadOnlyList<SonarrHistoryResource> historyResources = await sonarrClient.FetchHistorySinceAsync(
            instance,
            historyStart,
            cancellationToken);
        IReadOnlyList<LibraryChangeHistoryEntry> historyEntries = LibraryChangeHistoryBuilder.BuildSonarrEntries(
            instance,
            snapshot,
            previousSnapshot,
            historyResources);
        settings.SonarrSnapshots = [
            .. settings.SonarrSnapshots.Where(currentSnapshot => currentSnapshot.InstanceId != instanceId),
            snapshot
        ];
        await historyStore.AppendSnapshotHistoryAsync(
            ArrApplicationKind.Sonarr,
            snapshot.InstanceId,
            snapshot.InstanceName,
            snapshot.SyncedAt,
            snapshot,
            cancellationToken);
        IReadOnlyList<LibraryChangeHistoryEntry> persistedSonarrHistory = await historyStore.GetLibraryHistoryAsync(
            ArrApplicationKind.Sonarr,
            cancellationToken);
        List<LibraryChangeHistoryEntry> combinedSonarrHistory = [.. persistedSonarrHistory, .. historyEntries];
        IReadOnlyList<LibraryChangeHistoryEntry> sonarrSeedEntries = LibraryHistorySeedBuilder.BuildSonarrSeeds(
            instance,
            snapshot,
            combinedSonarrHistory);
        await historyStore.ReplaceLibraryHistoryAsync(
            ArrApplicationKind.Sonarr,
            LibraryChangeHistoryNormalizer.Normalize(combinedSonarrHistory.Concat(sonarrSeedEntries)),
            cancellationToken);

        LibraryFilterValues sonarrFilterValues = LibraryFilterValuesBuilder.Build(instance.Id, snapshot);
        settings.LibraryFilterValues = [
            .. settings.LibraryFilterValues.Where(fv => fv.InstanceId != instanceId),
            sonarrFilterValues
        ];

        await settingsService.UpdateAsync(current =>
        {
            if (!current.SonarrInstances.Any(item => item.Id == instanceId)) return;
            current.SonarrSnapshots = [.. current.SonarrSnapshots.Where(item => item.InstanceId != instanceId), snapshot];
            current.LibraryFilterValues = [.. current.LibraryFilterValues.Where(item => item.InstanceId != instanceId), sonarrFilterValues];
        }, cancellationToken);

        logger.LogInformation(
            "Completed Sonarr snapshot sync for instance {InstanceName} with {ItemCount} items.",
            instance.Name,
            snapshot.Series.Count);

        // The specials of this instance may have moved, so the candidate search is asked for it — but
        // only asked, because the search must not run inside the sync lock.
        symlinkMatching.Request();

        return new SyncInstanceResult
        {
            Message = "Sonarr data synchronized.",
            SyncedAt = snapshot.SyncedAt,
            ItemCount = snapshot.Series.Count
        };
    }

    private async Task<SyncInstanceResult> SyncRadarrAsync(
        string instanceId,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        ArrInstanceSettings? instance = settings.RadarrInstances.FirstOrDefault(
            (currentInstance) => currentInstance.Id == instanceId);

        if (instance is null)
        {
            logger.LogWarning("Radarr instance {InstanceId} was not found for snapshot sync.", instanceId);
            throw new ApiProblemException(
                StatusCodes.Status404NotFound,
                "Radarr instance not found",
                $"No Radarr instance exists for id '{instanceId}'.");
        }

        logger.LogInformation("Starting Radarr snapshot sync for instance {InstanceName}.", instance.Name);
        RadarrInstanceSnapshot? previousSnapshot = settings.RadarrSnapshots.FirstOrDefault(
            (currentSnapshot) => currentSnapshot.InstanceId == instanceId);
        bool hasRadarrLibraryHistory = await historyStore.HasLibraryHistoryAsync(
            ArrApplicationKind.Radarr,
            instanceId,
            cancellationToken);
        RadarrInstanceSnapshot snapshot = await radarrClient.FetchSnapshotAsync(instance, cancellationToken);
        DateTimeOffset historyStart = previousSnapshot?.SyncedAt ?? GetRadarrBootstrapDate(snapshot);
        if (!hasRadarrLibraryHistory)
        {
            historyStart = GetRadarrBootstrapDate(snapshot);
        }
        IReadOnlyList<RadarrHistoryResource> historyResources = await radarrClient.FetchHistorySinceAsync(
            instance,
            historyStart,
            cancellationToken);
        IReadOnlyList<LibraryChangeHistoryEntry> historyEntries = LibraryChangeHistoryBuilder.BuildRadarrEntries(
            instance,
            snapshot,
            previousSnapshot,
            historyResources);
        settings.RadarrSnapshots = [
            .. settings.RadarrSnapshots.Where((currentSnapshot) => currentSnapshot.InstanceId != instanceId),
            snapshot
        ];
        await historyStore.AppendSnapshotHistoryAsync(
            ArrApplicationKind.Radarr,
            snapshot.InstanceId,
            snapshot.InstanceName,
            snapshot.SyncedAt,
            snapshot,
            cancellationToken);
        IReadOnlyList<LibraryChangeHistoryEntry> persistedRadarrHistory = await historyStore.GetLibraryHistoryAsync(
            ArrApplicationKind.Radarr,
            cancellationToken);
        List<LibraryChangeHistoryEntry> combinedRadarrHistory = [.. persistedRadarrHistory, .. historyEntries];
        IReadOnlyList<LibraryChangeHistoryEntry> radarrSeedEntries = LibraryHistorySeedBuilder.BuildRadarrSeeds(
            instance,
            snapshot,
            combinedRadarrHistory);
        await historyStore.ReplaceLibraryHistoryAsync(
            ArrApplicationKind.Radarr,
            LibraryChangeHistoryNormalizer.Normalize(combinedRadarrHistory.Concat(radarrSeedEntries)),
            cancellationToken);

        LibraryFilterValues radarrFilterValues = LibraryFilterValuesBuilder.Build(instance.Id, snapshot);
        settings.LibraryFilterValues = [
            .. settings.LibraryFilterValues.Where((fv) => fv.InstanceId != instanceId),
            radarrFilterValues
        ];

        await settingsService.UpdateAsync(current =>
        {
            if (!current.RadarrInstances.Any(item => item.Id == instanceId)) return;
            current.RadarrSnapshots = [.. current.RadarrSnapshots.Where(item => item.InstanceId != instanceId), snapshot];
            current.LibraryFilterValues = [.. current.LibraryFilterValues.Where(item => item.InstanceId != instanceId), radarrFilterValues];
        }, cancellationToken);

        logger.LogInformation(
            "Completed Radarr snapshot sync for instance {InstanceName} with {ItemCount} items.",
            instance.Name,
            snapshot.Movies.Count);

        // A movie that arrived or changed is exactly what the specials were waiting for.
        symlinkMatching.Request();

        return new SyncInstanceResult
        {
            Message = "Radarr data synchronized.",
            SyncedAt = snapshot.SyncedAt,
            ItemCount = snapshot.Movies.Count
        };
    }

    private static DateTimeOffset GetSonarrBootstrapDate(SonarrInstanceSnapshot snapshot)
    {
        DateTimeOffset? earliestCreatedAt = snapshot.Series
            .SelectMany((series) => series.Seasons)
            .SelectMany((season) => season.Episodes)
            .Select((episode) => episode.File.CreatedAt)
            .Where((createdAt) => createdAt.HasValue)
            .Select((createdAt) => createdAt!.Value)
            .OrderBy((createdAt) => createdAt)
            .FirstOrDefault();

        return earliestCreatedAt ?? snapshot.SyncedAt;
    }

    private static DateTimeOffset GetRadarrBootstrapDate(RadarrInstanceSnapshot snapshot)
    {
        DateTimeOffset? earliestCreatedAt = snapshot.Movies
            .Select((movie) => movie.File.CreatedAt)
            .Where((createdAt) => createdAt.HasValue)
            .Select((createdAt) => createdAt!.Value)
            .OrderBy((createdAt) => createdAt)
            .FirstOrDefault();

        return earliestCreatedAt ?? snapshot.SyncedAt;
    }
}
