using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace LibManagarr.Api.Tests.Services;

internal sealed class TestStorageEnvironment : IHostEnvironment, IDisposable
{
    public string EnvironmentName { get; set; } = "Testing";
    public string ApplicationName { get; set; } = "LibManagarr.Tests";
    public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), $"libmanagarr-tests-{Guid.NewGuid():N}");
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

    public TestStorageEnvironment() => Directory.CreateDirectory(ContentRootPath);

    public void Dispose()
    {
        if (Directory.Exists(ContentRootPath)) Directory.Delete(ContentRootPath, recursive: true);
    }
}
