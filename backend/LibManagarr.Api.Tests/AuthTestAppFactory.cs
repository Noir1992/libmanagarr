using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests;

/// <summary>
/// Test host for the full auth pipeline: a fresh temporary config directory per factory, the
/// background sync jobs removed and the DI TimeProvider swapped for a controllable fake so
/// session expiry can be exercised without real waiting. The fake clock starts at the real
/// current time because the cookie handler validates cookie expiry with the system clock.
/// </summary>
internal sealed class AuthTestAppFactory : WebApplicationFactory<Program>
{
    public FakeTimeProvider FakeTime { get; } = new(DateTimeOffset.UtcNow);

    public string ConfigDirectory { get; } = Path.Combine(Path.GetTempPath(), "libmanagarr-auth-tests-" + Guid.NewGuid().ToString("N"));

    public AuthTestAppFactory()
    {
        Directory.CreateDirectory(ConfigDirectory);

    }

    public HttpClient CreateCookielessClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { [StoragePaths.ConfigDirectoryEnvironmentVariable] = ConfigDirectory }));
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices((services) =>
        {
            foreach (ServiceDescriptor descriptor in services.Where((descriptor) => descriptor.ServiceType == typeof(IHostedService)).ToArray())
            {
                services.Remove(descriptor);
            }

            ServiceDescriptor? timeDescriptor = services.SingleOrDefault((descriptor) => descriptor.ServiceType == typeof(TimeProvider));
            if (timeDescriptor is not null)
            {
                services.Remove(timeDescriptor);
            }

            services.AddSingleton<TimeProvider>(FakeTime);
            services.Configure<CookieAuthenticationOptions>("Cookies", options => options.TimeProvider = FakeTime);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(ConfigDirectory, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset current = start;

    public DateTimeOffset Current => current;

    public override DateTimeOffset GetUtcNow() => current;

    public void Advance(TimeSpan by) => current += by;
}
