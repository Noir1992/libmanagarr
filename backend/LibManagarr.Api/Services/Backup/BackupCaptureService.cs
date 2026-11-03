using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace LibManagarr.Api.Services.Backup;

/// <summary>
/// Creates restricted-access staging directories below the config directory and removes them
/// again. Every export, import attempt and safety copy lives here first, so no half processed
/// archive ever sits next to the live settings or databases.
/// </summary>
internal static class BackupStaging
{
    /// <summary>
    /// Staging trees only a running process needs. A rollback area is deliberately absent: its
    /// contents are the safety copy a restore can be undone with, and it stays until the documented
    /// retention period ends.
    /// </summary>
    private static readonly string[] ProcessStagingPrefixes = ["export-", "download-", "import-", "upload-"];

    /// <summary>Prefix of the directory a restore moves the previous files into.</summary>
    private const string RollbackPrefix = "rollback-";

    public static string Create(string backupsDirectory, string prefix)
    {
        Directory.CreateDirectory(backupsDirectory);
        string path = Path.Combine(backupsDirectory, $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    public static void ProtectFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>
    /// Removes staging trees a previous process never finished cleaning, for instance after a crash
    /// in the middle of an export. The staging area of a restore that still waits to be taken over is
    /// kept, because the next start has to find exactly those files.
    /// </summary>
    public static void CleanAbandonedProcessStaging(string backupsDirectory, string? keepStagingDirectory)
    {
        if (!Directory.Exists(backupsDirectory))
        {
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(backupsDirectory))
        {
            string name = Path.GetFileName(directory);
            if (!ProcessStagingPrefixes.Any((prefix) => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (keepStagingDirectory is not null && HoldsStagingArea(directory, keepStagingDirectory))
            {
                continue;
            }

            TryDelete(directory);
        }
    }

    /// <summary>
    /// Keeps the newest safety copies and discards older ones, so the rollback area cannot grow
    /// without a bound. The retention is a number of restores rather than a duration, because one
    /// restore per installation is what an operator actually produces, and it is the period the
    /// deployment documentation points a manual rollback at.
    /// </summary>
    public static void TrimRollbackCopies(string backupsDirectory, int keepNewest = 3)
    {
        if (!Directory.Exists(backupsDirectory) || keepNewest < 1)
        {
            return;
        }

        IOrderedEnumerable<string> byAge = Directory.EnumerateDirectories(backupsDirectory)
            .Where((directory) => Path.GetFileName(directory)
                .StartsWith(RollbackPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending((directory) => Directory.GetCreationTimeUtc(directory));

        foreach (string directory in byAge.Skip(keepNewest))
        {
            TryDelete(directory);
        }
    }

    private static bool HoldsStagingArea(string candidate, string path)
    {
        string candidateRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return string.Equals(fullPath, candidateRoot, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(candidateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A staging leftover is never worth failing an export over; the next start ignores it.
        }
        catch (UnauthorizedAccessException)
        {
            // Same here: the directory belongs to this service and is retried on the next run.
        }
    }

    public static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using (FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        await using (FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        ProtectFile(destination);
    }

    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// Captures settings and library data through the SQLite online backup API, including committed
/// WAL changes. Account data and data-protection keys stay on the current installation.
/// </summary>
public sealed class BackupCaptureService(StoragePaths paths, ILogger<BackupCaptureService> logger)
{
    public async Task CaptureAsync(string stagingRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stagingRoot);

        if (File.Exists(paths.SettingsFilePath))
        {
            await BackupStaging.CopyFileAsync(paths.SettingsFilePath, Path.Combine(stagingRoot, BackupComponentNames.Settings), cancellationToken);
        }

        await CopyDatabaseAsync(paths.DatabasePath, Path.Combine(stagingRoot, BackupComponentNames.LibraryDatabase), cancellationToken);
        logger.LogInformation("Captured backup settings and library data.");
    }

    public async Task<BackupManifest> BuildManifestAsync(string stagingRoot, BackupSettingsSummary settings, CancellationToken cancellationToken)
    {
        List<BackupManifestEntry> entries = [];
        foreach (string file in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories))
        {
            string archivePath = Path.GetRelativePath(stagingRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            if (archivePath == BackupManifestSerializer.EntryName)
            {
                continue;
            }

            entries.Add(new BackupManifestEntry
            {
                Path = archivePath,
                SizeBytes = new FileInfo(file).Length,
                Sha256 = await BackupStaging.Sha256Async(file, cancellationToken)
            });
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));

        return new BackupManifest
        {
            FormatVersion = BackupArchiveFormat.FormatVersion,
            ApplicationVersion = typeof(BackupCaptureService).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
            LibrarySchemaVersion = ReadSchemaVersion(Path.Combine(stagingRoot, BackupComponentNames.LibraryDatabase)),
            Entries = entries,
            SonarrInstanceCount = settings.SonarrInstanceCount,
            RadarrInstanceCount = settings.RadarrInstanceCount,
            HasSeerrInstance = settings.HasSeerrInstance
        };
    }

    /// <summary>
    /// Copies one database through the supported online backup API, so the committed WAL content
    /// belongs to the snapshot and no reader or writer ever sees a file being copied underneath it.
    /// Both connections stay out of the pool: a pooled connection keeps its file handle, and the
    /// handle a copy leaves behind asks for read and write access, which packing the staging tree
    /// (reading only) and deleting it afterwards both refuse on Windows.
    /// </summary>
    private async Task CopyDatabaseAsync(string source, string destination, CancellationToken cancellationToken)
    {
        if (!File.Exists(source))
        {
            return;
        }

        await using SqliteConnection sourceConnection = new(new SqliteConnectionStringBuilder
        {
            DataSource = source,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await sourceConnection.OpenAsync(cancellationToken);
        await using SqliteConnection destinationConnection = new(new SqliteConnectionStringBuilder
        {
            DataSource = destination,
            Pooling = false
        }.ToString());
        await destinationConnection.OpenAsync(cancellationToken);
        sourceConnection.BackupDatabase(destinationConnection);
        await destinationConnection.CloseAsync();
        await LeaveWriteAheadLoggingMode(destination, cancellationToken);
        BackupStaging.ProtectFile(destination);
    }

    /// <summary>
    /// Turns the copy into a single self contained database file again. The online backup transfers
    /// page one unchanged, so the copy carries the write-ahead marker of its original. Opening it
    /// to read the schema version would make SQLite place .db-wal and .db-shm files next to it.
    /// Those two are runtime bookkeeping
    /// rather than content, the archive format has no place for them and the import refuses them,
    /// so the marker is taken back here instead. Everything committed belongs to the copy already.
    /// </summary>
    private static async Task LeaveWriteAheadLoggingMode(string databasePath, CancellationToken cancellationToken)
    {
        // Reopened on purpose: the mode of the copy below is only visible to a connection that
        // reads the transferred header, which the backup connection had no reason to do again.
        await using SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using (SqliteCommand journalMode = connection.CreateCommand())
        {
            journalMode.CommandText = "PRAGMA journal_mode = DELETE";
            await journalMode.ExecuteScalarAsync(cancellationToken);
        }

        await connection.CloseAsync();
    }

    private static int ReadSchemaVersion(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return 0;
        }

        using SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
