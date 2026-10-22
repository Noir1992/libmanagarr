using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class StoragePathsTests
{
    [Fact]
    public void Ctor_ShouldDefaultConfigDirectoryToDataDirectory()
    {
        using TestStorageEnvironment environment = new();

        StoragePaths paths = new(environment, configDirectory: null);

        Assert.Equal(Path.Combine(environment.ContentRootPath, "data"), paths.ConfigDirectory);
        Assert.Equal(paths.ConfigDirectory, paths.DataDirectory);
    }

    [Fact]
    public void Ctor_ShouldFallBackToDataDirectory_WhenConfigDirectoryIsBlank()
    {
        using TestStorageEnvironment environment = new();

        StoragePaths paths = new(environment, "   ");

        Assert.Equal(Path.Combine(environment.ContentRootPath, "data"), paths.ConfigDirectory);
    }

    [Fact]
    public void Ctor_ShouldUseExplicitConfigDirectory_AndKeepDataDirectoryInContentRoot()
    {
        using TestStorageEnvironment environment = new();
        string configDirectory = Path.Combine(Path.GetTempPath(), $"libmanagarr-config-{Guid.NewGuid():N}");

        StoragePaths paths = new(environment, configDirectory);

        Assert.Equal(configDirectory, paths.ConfigDirectory);
        Assert.Equal(Path.Combine(environment.ContentRootPath, "data"), paths.DataDirectory);
    }

    [Fact]
    public void Ctor_ShouldReadConfigDirectoryFromEnvironmentVariable()
    {
        using TestStorageEnvironment environment = new();
        string configDirectory = Path.Combine(Path.GetTempPath(), $"libmanagarr-config-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable(StoragePaths.ConfigDirectoryEnvironmentVariable, configDirectory);
        try
        {
            StoragePaths paths = new(environment);

            Assert.Equal(configDirectory, paths.ConfigDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(StoragePaths.ConfigDirectoryEnvironmentVariable, null);
        }
    }
}
