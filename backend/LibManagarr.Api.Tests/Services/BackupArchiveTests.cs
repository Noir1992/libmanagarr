using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using LibManagarr.Api.Services;
using LibManagarr.Api.Services.Backup;

namespace LibManagarr.Api.Tests.Services;

/// <summary>
/// Covers the archive path through the services the endpoints call: capturing one consistent
/// state, encrypting it, and checking an uploaded archive again inside its own staging tree.
/// </summary>
public sealed class BackupArchiveTests : IDisposable
{
    private const string BackupPassword = "a long enough backup password";

    private readonly TestStorageEnvironment environment = new();
    private readonly StoragePaths paths;
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero));
    private readonly BackupOperationCoordinator writes = new(TimeProvider.System);
    private readonly BackupStatusService status;
    private readonly BackupCaptureService capture;
    private readonly BackupImportRegistry registry;
    private readonly RestoreJournalStore journals;
    private readonly BackupExportService export;
    private readonly BackupImportService imports;
    private readonly string stagingRoot;

    public BackupArchiveTests()
    {
        paths = new StoragePaths(environment, configDirectory: null);
        status = new BackupStatusService(clock);
        capture = new BackupCaptureService(paths, NullLogger<BackupCaptureService>.Instance);
        registry = new BackupImportRegistry(clock);
        journals = new RestoreJournalStore(paths);
        export = new BackupExportService(paths, capture, writes, status, NullLogger<BackupExportService>.Instance);
        imports = new BackupImportService(paths, registry, journals, writes, status, clock,
            NullLogger<BackupImportService>.Instance);
        stagingRoot = Path.Combine(environment.ContentRootPath, "capture");
        SeedState();
    }

    [Fact]
    public async Task Capture_CopiesApplicationData_AndExcludesAccountAndKeys()
    {
        await capture.CaptureAsync(stagingRoot, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(stagingRoot, "settings.json")));
        Assert.True(File.Exists(Path.Combine(stagingRoot, "libmanagarr.db")));
        Assert.Equal(new[] { "libmanagarr.db", "settings.json" },
            Directory.GetFiles(stagingRoot).Select(Path.GetFileName).OrderBy(name => name));
        Assert.False(Directory.Exists(Path.Combine(stagingRoot, "data-protection-keys")));

        // The running installation keeps its own sessions, so exporting never logs anyone out.
        Assert.Equal(2, CountRows(paths.AuthDatabasePath, "sessions"));
        Assert.Equal(1, CountRows(paths.AuthDatabasePath, "two_factor_challenges"));
    }

    [Fact]
    public async Task BuildManifest_ListsEveryComponentWithItsSizeAndChecksum()
    {
        await capture.CaptureAsync(stagingRoot, CancellationToken.None);
        BackupManifest manifest = await capture.BuildManifestAsync(
            stagingRoot, new BackupSettingsSummary(2, 1, true), CancellationToken.None);

        Assert.Equal(BackupArchiveFormat.FormatVersion, manifest.FormatVersion);
        Assert.Equal(3, manifest.LibrarySchemaVersion);
        Assert.Equal(2, manifest.SonarrInstanceCount);
        Assert.Equal(1, manifest.RadarrInstanceCount);
        Assert.True(manifest.HasSeerrInstance);
        Assert.DoesNotContain("manifest.json", manifest.Entries.Select((entry) => entry.Path));
        Assert.Equal(
            new[] { "libmanagarr.db", "settings.json" },
            manifest.Entries.Select((entry) => entry.Path.Split('/')[0]).Distinct());
        Assert.All(manifest.Entries, (entry) =>
            Assert.Equal(new FileInfo(Resolve(stagingRoot, entry.Path)).Length, entry.SizeBytes));
        Assert.All(manifest.Entries, (entry) => Assert.Matches("^[0-9a-f]{64}$", entry.Sha256));
    }

    [Fact]
    public async Task Export_ThenValidate_OffersTheImportedStateWithoutTouchingTheLiveOne()
    {
        string settingsBefore = await File.ReadAllTextAsync(paths.SettingsFilePath);
        string upload = ExportToUploadFile();

        BackupImportPreview preview = await imports.ValidateAsync(upload, BackupPassword, 7, "session-hash", CancellationToken.None);

        Assert.Equal(2, preview.SonarrInstanceCount);
        Assert.Equal(1, preview.RadarrInstanceCount);
        Assert.True(preview.HasSeerrInstance);
        Assert.Equal(3, preview.LibrarySchemaVersion);
        Assert.True(preview.SameApplicationVersion);
        Assert.True(preview.ArchiveBytes > 0);
        Assert.Equal(clock.GetUtcNow() + BackupImportRegistry.ImportLifetime, preview.ExpiresAt);
        Assert.False(File.Exists(upload));
        Assert.Equal(settingsBefore, await File.ReadAllTextAsync(paths.SettingsFilePath));
        Assert.Equal(2, CountRows(paths.AuthDatabasePath, "sessions"));
    }

    [Fact]
    public async Task Export_ThenValidate_WhileTheLiveDatabasesUseWriteAheadLogging_OffersTheImportedState()
    {
        RunLiveDatabasesInWriteAheadLoggingMode();

        string upload = ExportToUploadFile();

        BackupImportPreview preview = await imports.ValidateAsync(upload, BackupPassword, 7, "session-hash", CancellationToken.None);

        Assert.Equal(3, preview.LibrarySchemaVersion);
    }

    [Fact]
    public async Task Validate_RefusesTheWrongPassword_AndLeavesNoStagingBehind()
    {
        string upload = ExportToUploadFile();

        BackupException failure = await Assert.ThrowsAsync<BackupException>(
            () => imports.ValidateAsync(upload, "an entirely wrong password", 7, "session-hash", CancellationToken.None));

        Assert.Equal(BackupErrorCodes.InvalidPassword, failure.Code);
        Assert.Empty(StagingAreas("import-"));
    }

    [Fact]
    public async Task Validate_RefusesAnAlteredArchive_BecauseEveryChunkIsAuthenticated()
    {
        string upload = ExportToUploadFile();
        byte[] bytes = await File.ReadAllBytesAsync(upload);
        bytes[BackupArchiveFormat.FixedHeaderLength + BackupArchiveFormat.SaltLength
            + BackupArchiveFormat.NoncePrefixLength + BackupArchiveFormat.HmacTagLength + 8] ^= 0xFF;
        await File.WriteAllBytesAsync(upload, bytes);

        BackupException failure = await Assert.ThrowsAsync<BackupException>(
            () => imports.ValidateAsync(upload, BackupPassword, 7, "session-hash", CancellationToken.None));

        Assert.Equal(BackupErrorCodes.InvalidPassword, failure.Code);
        Assert.Empty(StagingAreas("import-"));
    }

    [Fact]
    public async Task Export_RefusesAWeakBackupPassword_BeforeCapturingAnything()
    {
        BackupException failure = await Assert.ThrowsAsync<BackupException>(() => export.ExportAsync("too short", CancellationToken.None));

        Assert.Equal(BackupErrorCodes.WeakPassword, failure.Code);
        Assert.Equal(StatusCodes.Status400BadRequest, failure.StatusCode);
        Assert.Empty(StagingAreas("export-"));
        Assert.Empty(StagingAreas("download-"));
    }

    [Fact]
    public async Task ExportAndValidate_DoNotRequireAccountOrKeyFiles()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(paths.AuthDatabasePath);
        Directory.Move(paths.DataProtectionKeysDirectory, paths.DataProtectionKeysDirectory + "-unused");

        string upload = ExportToUploadFile();
        BackupImportPreview preview = await imports.ValidateAsync(upload, BackupPassword, 7, "session-hash", CancellationToken.None);

        Assert.Equal(3, preview.LibrarySchemaVersion);
        Assert.False(File.Exists(paths.AuthDatabasePath));
        Assert.False(Directory.Exists(paths.DataProtectionKeysDirectory));
    }

    [Fact]
    public async Task Validate_RejectsTheOldAccountInclusiveFormat()
    {
        string upload = ExportToUploadFile();
        byte[] bytes = await File.ReadAllBytesAsync(upload);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 1);
        await File.WriteAllBytesAsync(upload, bytes);

        BackupException failure = await Assert.ThrowsAsync<BackupException>(
            () => imports.ValidateAsync(upload, BackupPassword, 7, "session-hash", CancellationToken.None));

        Assert.Equal(BackupErrorCodes.UnsupportedFormat, failure.Code);
        Assert.Empty(StagingAreas("import-"));
        Assert.Equal(2, CountRows(paths.AuthDatabasePath, "sessions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_ReplacesApplicationData_AndPreservesTheTargetAccountAndKeys(bool failVerification)
    {
        string settingsAtExport = await File.ReadAllTextAsync(paths.SettingsFilePath);
        string upload = ExportToUploadFile();
        await File.WriteAllTextAsync(paths.SettingsFilePath, "{\"language\":\"Fr\"}");
        Execute(paths.AuthDatabasePath, 2,
            "UPDATE accounts SET totp_secret='target-secret', two_factor_enabled=0 WHERE id=1",
            "INSERT INTO sessions(token) VALUES('target-session')",
            "DELETE FROM recovery_codes");
        Execute(paths.DatabasePath, 3, "INSERT INTO snapshot_history(id) VALUES(99)");
        SqliteConnection.ClearAllPools();
        byte[] accountBefore = await File.ReadAllBytesAsync(paths.AuthDatabasePath);
        Dictionary<string, string> keysBefore = Directory.GetFiles(paths.DataProtectionKeysDirectory)
            .ToDictionary(file => Path.GetFileName(file), File.ReadAllText);
        BackupImportPreview preview = await imports.ValidateAsync(upload, BackupPassword, 7, "session-hash", CancellationToken.None);
        RestoreJournal journal = await imports.PrepareRestoreAsync(preview.ImportId, 7, "session-hash", "restore-account-test", CancellationToken.None);
        if (failVerification)
        {
            // A database missing a required table forces the bootstrap's verification and rollback.
            Execute(Path.Combine(journal.StagingDirectory, "libmanagarr.db"), 3, "DROP TABLE snapshot_history");
            SqliteConnection.ClearAllPools();
        }

        RestoreBootstrapper bootstrap = new(paths, journals, NullLogger<RestoreBootstrapper>.Instance);
        RestoreBootstrapOutcome outcome = bootstrap.Run(CancellationToken.None);

        Assert.Equal(failVerification ? RestoreBootstrapOutcome.RolledBack : RestoreBootstrapOutcome.Applied, outcome);
        Assert.Equal(failVerification ? "{\"language\":\"Fr\"}" : settingsAtExport,
            await File.ReadAllTextAsync(paths.SettingsFilePath));
        Assert.Equal(failVerification ? 1 : 0, CountRows(paths.DatabasePath, "snapshot_history"));
        Assert.Equal(accountBefore, await File.ReadAllBytesAsync(paths.AuthDatabasePath));
        Assert.Equal(3, CountRows(paths.AuthDatabasePath, "sessions"));
        Assert.Equal(0, CountRows(paths.AuthDatabasePath, "recovery_codes"));
        Assert.Equal(keysBefore.Count, Directory.GetFiles(paths.DataProtectionKeysDirectory).Length);
        foreach ((string file, string content) in keysBefore)
        {
            Assert.Equal(content, await File.ReadAllTextAsync(Path.Combine(paths.DataProtectionKeysDirectory, file)));
        }
        Assert.DoesNotContain(Directory.GetFiles(journal.RollbackDirectory),
            file => Path.GetFileName(file) == "auth.db");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bootstrap_RejectsOldAccountOrKeyRestoreOrders(bool keyDirectory)
    {
        byte[] accountBefore = File.ReadAllBytes(paths.AuthDatabasePath);
        journals.Write(new RestoreJournal
        {
            OperationId = "old-order", State = RestoreJournalState.Prepared, PreparedAt = clock.GetUtcNow(),
            ArchiveSha256 = "sha", StagingDirectory = stagingRoot,
            RollbackDirectory = Path.Combine(paths.BackupsDirectory, "rollback-old"),
            Components = [new RestoreComponent
            {
                SourcePath = Path.Combine(stagingRoot, keyDirectory ? "data-protection-keys" : "auth.db"),
                TargetPath = keyDirectory ? paths.DataProtectionKeysDirectory : paths.AuthDatabasePath,
                IsDirectory = keyDirectory
            }]
        });

        RestoreBootstrapper bootstrap = new(paths, journals, NullLogger<RestoreBootstrapper>.Instance);

        Assert.Equal(RestoreBootstrapOutcome.Failed, bootstrap.Run(CancellationToken.None));
        Assert.Equal(accountBefore, File.ReadAllBytes(paths.AuthDatabasePath));
        Assert.NotNull(journals.Read());
    }

    [Fact]
    public async Task PrepareRestore_KeepsWritersOutUntilTheRestart_AndCanRunOnlyOnce()
    {
        string upload = ExportToUploadFile();
        BackupImportPreview preview = await imports.ValidateAsync(upload, BackupPassword, 7, "session-hash", CancellationToken.None);

        RestoreJournal journal = await imports.PrepareRestoreAsync(preview.ImportId, 7, "session-hash", "op-1", CancellationToken.None);

        Assert.Equal(RestoreJournalState.Prepared, journal.State);
        Assert.Equal("op-1", journals.Read()!.OperationId);
        Assert.Equal(BackupOperationPhase.RestartPending, status.Phase);
        Assert.True(writes.IsInMaintenanceMode);
        Assert.Equal(2, journal.Components.Count);
        Assert.DoesNotContain(journal.Components, component => component.TargetPath == paths.AuthDatabasePath
            || component.TargetPath == paths.DataProtectionKeysDirectory);
        Assert.Contains(journal.Components, (component) => !component.IsDirectory && component.TargetPath == paths.SettingsFilePath);
        Assert.Contains(journal.Components, (component) => !component.IsDirectory && component.TargetPath == paths.DatabasePath);

        BackupException blocked = await Assert.ThrowsAsync<BackupException>(() => writes.EnterWriteAsync(CancellationToken.None));
        Assert.Equal(BackupErrorCodes.RestorePending, blocked.Code);

        BackupException reused = await Assert.ThrowsAsync<BackupException>(
            () => imports.PrepareRestoreAsync(preview.ImportId, 7, "session-hash", "op-2", CancellationToken.None));
        Assert.Equal(BackupErrorCodes.ImportConflict, reused.Code);
    }

    [Fact]
    public async Task DiscardTakesTheStagedFiles_AndADiscardedTicketIsGoneForGood()
    {
        string upload = ExportToUploadFile();
        BackupImportPreview preview = await imports.ValidateAsync(upload, BackupPassword, 7, "session-hash", CancellationToken.None);
        string staged = Assert.Single(StagingAreas("import-"));

        imports.Discard(preview.ImportId, 7, "session-hash");
        Assert.False(Directory.Exists(staged));

        BackupException unknown = Assert.Throws<BackupException>(() => imports.Discard(preview.ImportId, 7, "session-hash"));
        Assert.Equal(BackupErrorCodes.ImportUnknown, unknown.Code);
    }

    [Fact]
    public void ExpiredStagingIsHandedToTheCallerForDeletion()
    {
        registry.Add("stale-import", "stale-state", "sha", 10, Manifest(), 7, "session-hash");
        clock.Advance(BackupImportRegistry.ImportLifetime);

        Assert.Equal(new[] { "stale-import" }, imports.TakeExpiredStaging());
        Assert.Empty(imports.TakeExpiredStaging());
    }

    private string ExportToUploadFile()
    {
        using BackupExportResult archive = export.ExportAsync(BackupPassword, CancellationToken.None).GetAwaiter().GetResult();
        Assert.StartsWith("libmanagarr-backup-", archive.FileName);
        Assert.EndsWith(BackupArchiveFormat.FileExtension, archive.FileName);
        Assert.True(archive.SizeBytes > 0);
        Assert.Equal(new[] { "libmanagarr.db", "settings.json" }, archive.Manifest.Entries.Select(entry => entry.Path));

        string upload = Path.Combine(environment.ContentRootPath, "incoming" + BackupArchiveFormat.FileExtension);
        File.Copy(archive.ArchivePath, upload, overwrite: true);
        return upload;
    }

    private IReadOnlyList<string> StagingAreas(string prefix) =>
        Directory.Exists(paths.BackupsDirectory)
            ? [.. Directory.EnumerateDirectories(paths.BackupsDirectory)
                .Where((directory) => Path.GetFileName(directory).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))]
            : [];

    private void SeedState()
    {
        Directory.CreateDirectory(paths.ConfigDirectory);
        File.WriteAllText(paths.SettingsFilePath,
            """{"language":"De","sonarrInstances":[{"id":"a"},{"id":"b"}],"radarrInstances":[{"id":"c"}],"seerr":{"baseUrl":"http://seerr","apiKey":"k"}}""");

        Execute(paths.DatabasePath, 3,
            "CREATE TABLE snapshot_history(id INTEGER PRIMARY KEY)",
            "CREATE TABLE library_history(id INTEGER PRIMARY KEY)",
            "CREATE TABLE instance_snapshots(id INTEGER PRIMARY KEY)",
            "CREATE TABLE library_filter_values(id INTEGER PRIMARY KEY)");

        string protectedSecret = ProtectWithRing(paths.DataProtectionKeysDirectory, "a stored two factor secret");
        Execute(paths.AuthDatabasePath, 2,
            "CREATE TABLE accounts(id INTEGER PRIMARY KEY, two_factor_enabled INTEGER NOT NULL DEFAULT 0, totp_secret TEXT)",
            "CREATE TABLE sessions(id INTEGER PRIMARY KEY, token TEXT)",
            "CREATE TABLE recovery_codes(id INTEGER PRIMARY KEY, code TEXT)",
            "CREATE TABLE two_factor_challenges(id INTEGER PRIMARY KEY, account_id INTEGER)",
            "CREATE TABLE two_factor_setups(id INTEGER PRIMARY KEY, account_id INTEGER)",
            "INSERT INTO accounts(id,two_factor_enabled) VALUES(1,1)",
            "INSERT INTO sessions(token) VALUES('one'),('two')",
            "INSERT INTO recovery_codes(code) VALUES('recovery')",
            "INSERT INTO two_factor_challenges(account_id) VALUES(1)",
            "INSERT INTO two_factor_setups(account_id) VALUES(1)");

        using SqliteConnection account = new(new SqliteConnectionStringBuilder { DataSource = paths.AuthDatabasePath }.ToString());
        account.Open();
        using SqliteCommand secret = account.CreateCommand();
        secret.CommandText = "UPDATE accounts SET totp_secret=$secret WHERE id=1";
        secret.Parameters.AddWithValue("$secret", protectedSecret);
        secret.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// Switches both live databases to the write-ahead logging mode the running application uses,
    /// because that mode is stored in the file header and the online backup carries it into the
    /// staged copies. Without it this class only ever exports rollback-journal databases, which is
    /// a milder input than any real installation hands to the export.
    /// </summary>
    private void RunLiveDatabasesInWriteAheadLoggingMode()
    {
        Execute(paths.DatabasePath, 3, "PRAGMA journal_mode=WAL");
        Execute(paths.AuthDatabasePath, 2, "PRAGMA journal_mode=WAL");
        SqliteConnection.ClearAllPools();
    }

    private static string ProtectWithRing(string keysDirectory, string value)
    {
        Directory.CreateDirectory(keysDirectory);
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDataProtection().SetApplicationName("LibManagarr");
        services.Configure<KeyManagementOptions>((options) => options.XmlRepository = new FileSystemXmlRepository(
            new DirectoryInfo(keysDirectory), NullLoggerFactory.Instance));
        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("LibManagarr.TwoFactorSecret.v1")
            .Protect(value);
    }

    private static void Execute(string databasePath, int schemaVersion, params string[] statements)
    {
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        connection.Open();
        using (SqliteCommand version = connection.CreateCommand())
        {
            version.CommandText = $"PRAGMA user_version={schemaVersion}";
            version.ExecuteNonQuery();
        }

        foreach (string statement in statements)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
    }

    private static int CountRows(string databasePath, string table)
    {
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static string Resolve(string stagingRoot, string archivePath) =>
        Path.Combine(stagingRoot, archivePath.Replace('/', Path.DirectorySeparatorChar));

    private static BackupManifest Manifest() => new()
    {
        FormatVersion = BackupArchiveFormat.FormatVersion,
        ApplicationVersion = "1.2.3",
        CreatedAt = new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero),
        LibrarySchemaVersion = 3,
        Entries = [new BackupManifestEntry { Path = "libmanagarr.db", SizeBytes = 4096, Sha256 = "aa" }]
    };

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        environment.Dispose();
    }
}
