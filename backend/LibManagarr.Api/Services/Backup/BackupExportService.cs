namespace LibManagarr.Api.Services.Backup;

/// <summary>
/// Runs a manual export: pauses writers, captures one consistent snapshot, encrypts it and hands
/// back a ready to read archive. The archive lives in its own directory below the config folder,
/// never inside the staging tree, because the staging tree is what gets zipped and the caller
/// deleting the download must not reach into the captured files.
/// </summary>
public sealed class BackupExportService(
    StoragePaths paths,
    BackupCaptureService capture,
    BackupOperationCoordinator coordinator,
    BackupStatusService status,
    ILogger<BackupExportService> logger)
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(90);

    public async Task<BackupExportResult> ExportAsync(string backupPassword, CancellationToken cancellationToken)
    {
        if (backupPassword.Length < BackupArchiveFormat.MinBackupPasswordLength)
        {
            throw new BackupException(BackupErrorCodes.WeakPassword,
                $"The backup password needs at least {BackupArchiveFormat.MinBackupPasswordLength} characters.")
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }

        status.Update(BackupOperationPhase.Exporting);
        using IOperationLease lease = await coordinator.PauseWritersAsync(DrainTimeout, keepPausedAfterRelease: false, cancellationToken);

        string stagingRoot = BackupStaging.Create(paths.BackupsDirectory, "export");
        string outputDirectory = BackupStaging.Create(paths.BackupsDirectory, "download");
        try
        {
            await capture.CaptureAsync(stagingRoot, cancellationToken);
            BackupManifest manifest = await capture.BuildManifestAsync(
                stagingRoot, BackupContentReader.ReadSummary(stagingRoot), cancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(stagingRoot, BackupManifestSerializer.EntryName), BackupManifestSerializer.Serialize(manifest), cancellationToken);

            string archivePath = Path.Combine(outputDirectory, BackupArchiveFormat.BuildDownloadFileName(manifest.CreatedAt));
            await BackupArchiveCodec.WriteAsync(archivePath, backupPassword, stagingRoot, cancellationToken);
            logger.LogInformation("Created backup archive with {EntryCount} components.", manifest.Entries.Count);
            status.Update(BackupOperationPhase.Idle);
            return new BackupExportResult(archivePath, outputDirectory, manifest);
        }
        catch
        {
            BackupStaging.TryDelete(outputDirectory);
            status.Update(BackupOperationPhase.Idle, BackupErrorCodes.CorruptArchive);
            throw;
        }
        finally
        {
            BackupStaging.TryDelete(stagingRoot);
        }
    }
}

/// <summary>
/// The archive on disk plus the directory that holds it. The caller streams the file to the
/// browser and then disposes this result, which removes the directory again.
/// </summary>
public sealed class BackupExportResult(string archivePath, string directory, BackupManifest manifest) : IDisposable
{
    public string ArchivePath { get; } = archivePath;

    public string FileName { get; } = Path.GetFileName(archivePath);

    public BackupManifest Manifest { get; } = manifest;

    public long SizeBytes => new FileInfo(ArchivePath).Length;

    public FileStream OpenRead() => new(ArchivePath, FileMode.Open, FileAccess.Read, FileShare.None);

    public void Dispose() => BackupStaging.TryDelete(directory);
}
