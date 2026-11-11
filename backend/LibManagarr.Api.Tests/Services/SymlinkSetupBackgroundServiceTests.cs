using System.Threading.Channels;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibManagarr.Api.Tests.Services;

public sealed class SymlinkSetupBackgroundServiceTests
{
    [Theory]
    [InlineData(true, SymlinkCapabilityStatus.StorageUnavailable)]
    [InlineData(false, SymlinkCapabilityStatus.StorageUnavailable)]
    [InlineData(true, SymlinkCapabilityStatus.Unsupported)]
    [InlineData(false, SymlinkCapabilityStatus.Unsupported)]
    [InlineData(true, SymlinkCapabilityStatus.PermissionDenied)]
    [InlineData(false, SymlinkCapabilityStatus.PermissionDenied)]
    [InlineData(false, SymlinkCapabilityStatus.Available)]
    public async Task StartupChecksOnceRegardlessOfActivationAndLaterRequestsDoNotRepeatTheProbe(
        bool enabled, SymlinkCapabilityStatus expectedStatus)
    {
        string movies = Path.Combine(Path.GetTempPath(), "symlink-test-movies");
        string series = Path.Combine(Path.GetTempPath(), "symlink-test-series");
        AppSettings snapshot = new()
        {
            Symlinks = new()
            {
                Enabled = enabled,
                Revision = 3,
                StorageAreas = [new() { Id = "main", MoviesRoot = movies, SeriesRoot = series }]
            }
        };
        SymlinkCapabilityServiceTests.FakeSymlinkFileSystem files = new(
            expectedStatus == SymlinkCapabilityStatus.StorageUnavailable ? [] : [movies, series])
        {
            CreateSymlinkThrows = expectedStatus switch
            {
                SymlinkCapabilityStatus.Unsupported => new PlatformNotSupportedException("No symlink support."),
                SymlinkCapabilityStatus.PermissionDenied => new UnauthorizedAccessException("No symlink privilege."),
                _ => null
            }
        };
        InMemorySymlinkStore store = new();
        Channel<byte> completedPasses = Channel.CreateUnbounded<byte>();
        ServiceCollection services = new();
        services.AddSingleton<ISymlinkStore>(store);
        services.AddSingleton<ISymlinkFileSystem>(files);
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddScoped<ISettingsService>(_ => new ObservedSettings(snapshot, completedPasses.Writer));
        services.AddScoped<SymlinkCapabilityService>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        SymlinkSetupRequests requests = new();
        using SymlinkSetupBackgroundService worker = new(requests,
            provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            NullLogger<SymlinkSetupBackgroundService>.Instance);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));

        await worker.StartAsync(deadline.Token);
        try
        {
            await completedPasses.Reader.ReadAsync(deadline.Token);
            SymlinkCapabilityCheck initial = Assert.Single(await store.GetCapabilityChecksAsync(deadline.Token));
            Assert.Equal(expectedStatus, initial.Status);
            Assert.Equal(3, initial.ConfigRevision);
            Assert.Equal(enabled && expectedStatus == SymlinkCapabilityStatus.Available, snapshot.Symlinks.Enabled);
            int created = files.Created.Count;

            for (int pass = 0; pass < 3; pass++)
            {
                requests.Request();
                await completedPasses.Reader.ReadAsync(deadline.Token);
                Assert.Same(initial, Assert.Single(await store.GetCapabilityChecksAsync(deadline.Token)));
                Assert.Equal(created, files.Created.Count);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private sealed class ObservedSettings(AppSettings snapshot, ChannelWriter<byte> completed) : ISettingsService, IDisposable
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
        public Task<AppSettings> SaveAsync(AppSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(settings);
        public void Dispose() => completed.TryWrite(0);
    }
}
