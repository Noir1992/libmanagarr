namespace LibManagarr.Api.Services;

/// <summary>
/// Resolves the storage directories once at startup. The config directory holds settings.json,
/// the SQLite database and the log files; it is taken from LIBMANAGARR_CONFIG_DIR when set (the
/// Docker image points it at /config so this state can be mounted on its own) and falls back to
/// the data folder below the content root otherwise. The data folder additionally holds the
/// poster cache and always stays below the content root.
/// </summary>
public sealed class StoragePaths
{
    public const string ConfigDirectoryEnvironmentVariable = "LIBMANAGARR_CONFIG_DIR";

    public StoragePaths(IHostEnvironment hostEnvironment)
        : this(hostEnvironment, Environment.GetEnvironmentVariable(ConfigDirectoryEnvironmentVariable))
    {
    }

    public StoragePaths(IHostEnvironment hostEnvironment, string? configDirectory)
    {
        DataDirectory = Path.Combine(hostEnvironment.ContentRootPath, "data");
        ConfigDirectory = string.IsNullOrWhiteSpace(configDirectory) ? DataDirectory : configDirectory;
    }

    public string ConfigDirectory { get; }

    public string DataDirectory { get; }

    public string DatabasePath => Path.Combine(ConfigDirectory, "libmanagarr.db");
}
