using System.IO.Compression;
using System.Security.Cryptography;

namespace LibManagarr.Api.Services.Backup;

/// <summary>
/// Short lived record of one archive that passed the full import check. It binds the staged files
/// to the account and session that produced them, to the hashes that were verified, and to a
/// deadline, so the confirmed takeover provably works on the files the user was shown and on
/// nothing else. The record lives in memory only: after a process restart an import has to be
/// validated again, which is the same guarantee the plan asks for and cheaper than a table.
/// </summary>
public sealed class BackupImportRegistry(TimeProvider clock)
{
    public static readonly TimeSpan ImportLifetime = TimeSpan.FromMinutes(15);

    private readonly Dictionary<string, ImportEntry> entries = [];
    private readonly object sync = new();

    public ImportEntry Add(
        string stagingDirectory,
        string stateDirectory,
        string archiveSha256,
        long archiveBytes,
        BackupManifest manifest,
        int accountId,
        string sessionTokenHash)
    {
        string importId = Guid.NewGuid().ToString("N");
        ImportEntry entry = new(
            importId,
            stagingDirectory,
            stateDirectory,
            archiveSha256,
            archiveBytes,
            manifest,
            accountId,
            sessionTokenHash,
            clock.GetUtcNow() + ImportLifetime);

        lock (sync)
        {
            entries.Add(importId, entry);
        }

        return entry;
    }

    /// <summary>
    /// Reads a ticket and reports why it cannot be used any more. Expiry and reuse are separate
    /// codes because the UI tells the user to repeat the check in one case and to start over from
    /// the beginning in the other.
    /// </summary>
    public ImportEntry RequireUsable(string importId, int accountId, string sessionTokenHash)
    {
        ImportEntry? entry;
        lock (sync)
        {
            entries.TryGetValue(importId, out entry);
        }

        if (entry is null)
        {
            throw new BackupException(BackupErrorCodes.ImportUnknown, "This import is no longer known. Check the backup file again.")
            {
                StatusCode = StatusCodes.Status404NotFound
            };
        }

        if (entry.AccountId != accountId || !string.Equals(entry.SessionTokenHash, sessionTokenHash, StringComparison.Ordinal))
        {
            throw new BackupException(BackupErrorCodes.ImportConflict, "The import was prepared in a different session.")
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        if (clock.GetUtcNow() >= entry.ExpiresAt)
        {
            Remove(importId);
            throw new BackupException(BackupErrorCodes.ImportExpired, "The prepared import expired. Check the backup file again.")
            {
                StatusCode = StatusCodes.Status410Gone
            };
        }

        return entry;
    }

    /// <summary>
    /// Marks a ticket as used. Doing this before any file is replaced is what stops a second
    /// confirm click, or a replayed request, from queueing a second restore of the same archive.
    /// </summary>
    public ImportEntry ClaimForApply(string importId, int accountId, string sessionTokenHash)
    {
        ImportEntry entry = RequireUsable(importId, accountId, sessionTokenHash);
        if (!entry.TryClaim())
        {
            throw new BackupException(BackupErrorCodes.ImportConflict, "This import was already used. Check the backup file again.")
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }

        return entry;
    }

    public void Remove(string importId)
    {
        lock (sync)
        {
            entries.Remove(importId);
        }
    }

    /// <summary>Staged directories of tickets that ran out of time, for the caller to delete.</summary>
    public IReadOnlyList<string> TakeExpired()
    {
        DateTimeOffset now = clock.GetUtcNow();
        List<string> stale = [];
        lock (sync)
        {
            foreach (ImportEntry entry in entries.Values.Where((entry) => now >= entry.ExpiresAt).ToList())
            {
                stale.Add(entry.StagingDirectory);
                entries.Remove(entry.ImportId);
            }
        }

        return stale;
    }

    public sealed record ImportEntry(
        string ImportId,
        string StagingDirectory,
        string StateDirectory,
        string ArchiveSha256,
        long ArchiveBytes,
        BackupManifest Manifest,
        int AccountId,
        string SessionTokenHash,
        DateTimeOffset ExpiresAt)
    {
        private int claimed;

        public bool TryClaim() => Interlocked.Exchange(ref claimed, 1) == 0;
    }
}

/// <summary>
/// Accepts an uploaded archive and turns it into a reviewed import offer. Everything happens in a
/// protected staging tree below the config directory: the file is decrypted, unpacked under strict
/// path and size limits, compared against its manifest and finally checked as a runnable state.
/// Nothing here writes to the live settings, databases or keys, so a failed check leaves the
/// installation exactly as it was.
/// </summary>
public sealed class BackupImportService(
    StoragePaths paths,
    BackupImportRegistry registry,
    RestoreJournalStore journals,
    BackupOperationCoordinator coordinator,
    BackupStatusService status,
    TimeProvider clock,
    ILogger<BackupImportService> logger)
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Checks an uploaded archive end to end and records it for the session that uploaded it. The
    /// caller hands over the file already inside a protected staging file, because that is where the
    /// request stream has to put it anyway; it is moved from there, so the bytes of an archive never
    /// exist twice on disk during the check. The session binding comes from whoever read the request,
    /// since only that side ever sees the session token, and the service works on its hash.
    /// </summary>
    public async Task<BackupImportPreview> ValidateAsync(
        string archivePath, string backupPassword, int accountId, string sessionTokenHash, CancellationToken cancellationToken)
    {
        status.Update(BackupOperationPhase.Validating);
        string stagingRoot = BackupStaging.Create(paths.BackupsDirectory, "import");
        string stateRoot = Path.Combine(stagingRoot, "state");
        try
        {
            string stagedArchive = Path.Combine(stagingRoot, "upload" + BackupArchiveFormat.FileExtension);
            File.Move(archivePath, stagedArchive, overwrite: true);
            long archiveBytes = new FileInfo(stagedArchive).Length;
            string archiveSha256 = await BackupStaging.Sha256Async(stagedArchive, cancellationToken);

            string payloadPath = Path.Combine(stagingRoot, "payload.zip");
            await BackupArchiveCodec.ReadAsync(stagedArchive, backupPassword, payloadPath, cancellationToken);
            long expandedBytes = await ExtractSafelyAsync(payloadPath, stateRoot, cancellationToken);

            BackupManifest manifest = BackupContentReader.ReadManifest(stateRoot);
            await VerifyAgainstManifestAsync(manifest, stateRoot, cancellationToken);
            string? contentFailure = BackupContentReader.ValidateSettings(stateRoot);
            if (contentFailure is not null)
            {
                throw new BackupException(BackupErrorCodes.IntegrityCheckFailed, contentFailure);
            }

            VerifySchemaVersions(manifest, stateRoot);
            EnsureFreeSpace(stateRoot, archiveBytes, expandedBytes);

            string? stateFailure = BackupStateVerifier.VerifyStaged(stateRoot);
            if (stateFailure is not null)
            {
                throw new BackupException(BackupErrorCodes.IntegrityCheckFailed, stateFailure);
            }

            BackupImportRegistry.ImportEntry entry = registry.Add(
                stagingRoot, stateRoot, archiveSha256, archiveBytes, manifest, accountId, sessionTokenHash);
            logger.LogInformation(
                "Validated import {ImportId} from version {ApplicationVersion} created at {CreatedAt}.",
                entry.ImportId, manifest.ApplicationVersion, manifest.CreatedAt);

            status.Update(BackupOperationPhase.Idle);
            return BuildPreview(entry, manifest);
        }
        catch
        {
            BackupStaging.TryDelete(stagingRoot);
            status.Update(BackupOperationPhase.Idle, BackupErrorCodes.CorruptArchive);
            throw;
        }
    }

    public void Discard(string importId, int accountId, string sessionTokenHash)
    {
        BackupImportRegistry.ImportEntry entry = registry.RequireUsable(importId, accountId, sessionTokenHash);
        registry.Remove(importId);
        BackupStaging.TryDelete(entry.StagingDirectory);
    }

    /// <summary>
    /// Turns a reviewed import into a durable restore order and leaves the application in
    /// maintenance mode. The lease is deliberately never released: writers stay blocked until this
    /// host is disposed, because the files the order replaces belong to the next host.
    /// </summary>
    public async Task<RestoreJournal> PrepareRestoreAsync(
        string importId, int accountId, string sessionTokenHash, string operationId, CancellationToken cancellationToken)
    {
        BackupImportRegistry.ImportEntry entry = registry.ClaimForApply(importId, accountId, sessionTokenHash);
        status.Update(BackupOperationPhase.PreparingRestore, operationId: operationId);
        long expandedBytes = entry.Manifest.Entries.Sum((item) => item.SizeBytes);
        EnsureFreeSpace(entry.StateDirectory, entry.ArchiveBytes, expandedBytes);
        await VerifyAgainstManifestAsync(entry.Manifest, entry.StateDirectory, cancellationToken);

        IOperationLease lease;
        try
        {
            lease = await coordinator.PauseWritersAsync(DrainTimeout, keepPausedAfterRelease: true, cancellationToken);
        }
        catch
        {
            status.Update(BackupOperationPhase.Idle, BackupErrorCodes.WritersNotPaused);
            throw;
        }

        try
        {
            RestoreJournal journal = WriteJournal(entry, operationId);
            status.Update(BackupOperationPhase.RestartPending, operationId: operationId);
            logger.LogWarning(
                "Restore {OperationId} is prepared and the application is in maintenance mode. Import {ImportId} will be taken over by the next start.",
                operationId, importId);
            return journal;
        }
        catch
        {
            lease.Dispose();
            status.Update(BackupOperationPhase.Idle, BackupErrorCodes.CorruptArchive);
            throw;
        }
    }

    public IReadOnlyList<string> TakeExpiredStaging() => registry.TakeExpired();

    private RestoreJournal WriteJournal(BackupImportRegistry.ImportEntry entry, string operationId)
    {
        string rollbackDirectory = Path.Combine(paths.BackupsDirectory, $"rollback-{operationId}");
        List<RestoreComponent> components = [];

        string settings = Path.Combine(entry.StateDirectory, BackupComponentNames.Settings);
        if (File.Exists(settings))
        {
            components.Add(new RestoreComponent { SourcePath = settings, TargetPath = paths.SettingsFilePath });
        }

        string library = Path.Combine(entry.StateDirectory, BackupComponentNames.LibraryDatabase);
        if (File.Exists(library))
        {
            components.Add(new RestoreComponent { SourcePath = library, TargetPath = paths.DatabasePath });
        }

        RestoreJournal journal = new()
        {
            OperationId = operationId,
            State = RestoreJournalState.Prepared,
            PreparedAt = clock.GetUtcNow(),
            ArchiveSha256 = entry.ArchiveSha256,
            StagingDirectory = entry.StateDirectory,
            RollbackDirectory = rollbackDirectory,
            Components = components
        };
        journals.Write(journal);
        return journal;
    }

    private static BackupImportPreview BuildPreview(BackupImportRegistry.ImportEntry entry, BackupManifest manifest)
    {
        string currentVersion = typeof(BackupImportService).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        return new BackupImportPreview(
            entry.ImportId,
            entry.ExpiresAt,
            manifest.ApplicationVersion,
            manifest.CreatedAt,
            manifest.LibrarySchemaVersion,
            manifest.SonarrInstanceCount,
            manifest.RadarrInstanceCount,
            manifest.HasSeerrInstance,
            entry.ArchiveBytes,
            entry.Manifest.Entries.Sum((item) => item.SizeBytes),
            string.Equals(manifest.ApplicationVersion, currentVersion, StringComparison.Ordinal));
    }

    private static async Task<long> ExtractSafelyAsync(string payloadPath, string stateRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stateRoot);
        await using ZipArchive archive = await ZipFile.OpenAsync(payloadPath, ZipArchiveMode.Read, cancellationToken);
        if (archive.Entries.Count > BackupArchiveFormat.MaxArchiveEntries)
        {
            throw new BackupException(BackupErrorCodes.CorruptArchive,
                $"The backup carries more than {BackupArchiveFormat.MaxArchiveEntries} files.");
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        long total = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            if (!BackupComponentNames.TryResolve(entry.FullName, stateRoot, out string destination))
            {
                throw new BackupException(BackupErrorCodes.UnsafePath, $"The backup contains the unsupported path {entry.FullName}.");
            }

            if (!seen.Add(entry.FullName))
            {
                throw new BackupException(BackupErrorCodes.CorruptArchive, $"The backup lists {entry.FullName} more than once.");
            }

            if (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > BackupArchiveFormat.MaxCompressionRatio)
            {
                throw new BackupException(BackupErrorCodes.SizeLimitExceeded, $"{entry.FullName} expands far beyond its stored size.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (Stream input = await entry.OpenAsync(cancellationToken))
            await using (FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                total += await CopyCountedAsync(input, output, total, cancellationToken);
            }

            BackupStaging.ProtectFile(destination);
        }

        return total;
    }

    private static async Task<long> CopyCountedAsync(Stream input, Stream output, long alreadyExtracted, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            written += read;
            if (alreadyExtracted + written > BackupArchiveFormat.MaxUncompressedBytes)
            {
                throw new BackupException(BackupErrorCodes.SizeLimitExceeded, "The backup expands beyond the allowed size.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return written;
    }

    /// <summary>
    /// Compares the unpacked tree with the manifest in both directions: every listed component has
    /// to be present with the recorded size and hash, and nothing that the manifest does not list
    /// may sit in the tree. A backup that silently lost a file is therefore rejected here instead
    /// of starting as an installation with missing data.
    /// </summary>
    private static async Task VerifyAgainstManifestAsync(BackupManifest manifest, string stateRoot, CancellationToken cancellationToken)
    {
        HashSet<string> listed = [];
        foreach (BackupManifestEntry entry in manifest.Entries)
        {
            if (!BackupComponentNames.TryResolve(entry.Path, stateRoot, out string path))
            {
                throw new BackupException(BackupErrorCodes.UnsafePath, $"The manifest lists the unsupported path {entry.Path}.");
            }

            if (!File.Exists(path))
            {
                throw new BackupException(BackupErrorCodes.MissingComponent, $"The backup is missing {entry.Path}.");
            }

            FileInfo info = new(path);
            if (info.Length != entry.SizeBytes)
            {
                throw new BackupException(BackupErrorCodes.ChecksumMismatch, $"{entry.Path} has a different size than the manifest declares.");
            }

            string hash = await BackupStaging.Sha256Async(path, cancellationToken);
            if (!string.Equals(hash, entry.Sha256, StringComparison.Ordinal))
            {
                throw new BackupException(BackupErrorCodes.ChecksumMismatch, $"{entry.Path} does not match its recorded checksum.");
            }

            listed.Add(entry.Path);
        }

        foreach (string file in Directory.EnumerateFiles(stateRoot, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(stateRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            if (relative != BackupManifestSerializer.EntryName && !listed.Contains(relative))
            {
                throw new BackupException(BackupErrorCodes.UnsafePath, $"The backup carries the unexpected file {relative}.");
            }
        }
    }

    /// <summary>
    /// Rejects a library database that does not match the schema version in the manifest.
    /// Comparing the stored version with the declared one also catches an
    /// archive whose files were swapped after it was written.
    /// </summary>
    private static void VerifySchemaVersions(BackupManifest manifest, string stateRoot)
    {
        string library = Path.Combine(stateRoot, BackupComponentNames.LibraryDatabase);

        if (File.Exists(library) && BackupStateVerifier.ReadSchemaVersion(library) != manifest.LibrarySchemaVersion)
        {
            throw new BackupException(BackupErrorCodes.UnsupportedSchema, "The library database does not match the schema version in the manifest.");
        }
    }

    /// <summary>
    /// Staging, the payload, the safety copy and the imported files all need room below the config
    /// directory. Asking before the work starts keeps a disk that is full from failing halfway
    /// through a takeover, when a database is already replaced.
    /// </summary>
    private static void EnsureFreeSpace(string path, long archiveBytes, long expandedBytes)
    {
        long needed = archiveBytes * 2 + expandedBytes * 2;
        DriveInfo drive = new(Path.GetPathRoot(Path.GetFullPath(path))!);
        if (drive.IsReady && drive.AvailableFreeSpace < needed)
        {
            throw new BackupException(BackupErrorCodes.InsufficientDiskSpace,
                $"At least {needed / (1024 * 1024)} MB of free space below the config directory is needed.")
            {
                StatusCode = StatusCodes.Status507InsufficientStorage
            };
        }
    }
}

/// <summary>
/// What the user is shown before confirming an import. Only counts, versions and dates, so the
/// preview can not leak an API key, a password hash or a stored secret.
/// </summary>
public sealed record BackupImportPreview(
    string ImportId,
    DateTimeOffset ExpiresAt,
    string ApplicationVersion,
    DateTimeOffset CreatedAt,
    int LibrarySchemaVersion,
    int SonarrInstanceCount,
    int RadarrInstanceCount,
    bool HasSeerrInstance,
    long ArchiveBytes,
    long UncompressedBytes,
    bool SameApplicationVersion);
