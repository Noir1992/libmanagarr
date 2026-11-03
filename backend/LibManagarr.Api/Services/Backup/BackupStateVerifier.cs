using Microsoft.Data.Sqlite;

namespace LibManagarr.Api.Services.Backup;

/// <summary>Checks library integrity without reading or modifying the installation's account.</summary>
public static class BackupStateVerifier
{
    public static string? VerifyAfterTakeover(StoragePaths paths) =>
        VerifyDatabase(paths.DatabasePath, ExpectedTables);

    public static string? VerifyStaged(string stagingRoot) =>
        VerifyDatabase(Path.Combine(stagingRoot, BackupComponentNames.LibraryDatabase), ExpectedTables);

    /// <summary>
    /// Every connection here stays out of the pool. A pooled connection keeps its file handle, and
    /// while one lingers on a staged copy that copy can neither be packed nor deleted afterwards.
    /// </summary>
    public static int ReadSchemaVersion(string databasePath)
    {
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

    private static string? VerifyDatabase(string path, IReadOnlyList<string> expectedTables)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();

        using (SqliteCommand check = connection.CreateCommand())
        {
            check.CommandText = "PRAGMA quick_check";
            string? result = check.ExecuteScalar() as string;
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return $"The database {Path.GetFileName(path)} failed its integrity check.";
            }
        }

        foreach (string table in expectedTables)
        {
            using SqliteCommand exists = connection.CreateCommand();
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";
            exists.Parameters.AddWithValue("$table", table);
            if (Convert.ToInt64(exists.ExecuteScalar()) == 0)
            {
                return $"The database {Path.GetFileName(path)} is missing the expected table {table}.";
            }
        }

        return null;
    }

    private static readonly string[] ExpectedTables =
        ["snapshot_history", "library_history", "instance_snapshots", "library_filter_values"];
}
