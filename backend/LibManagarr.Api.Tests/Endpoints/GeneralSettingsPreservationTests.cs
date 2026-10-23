using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Endpoints;

public sealed class GeneralSettingsPreservationTests
{
    [Fact]
    public async Task SaveGeneralSettings_ShouldPreserveSnapshotsAndFilterValues()
    {
        AppSettings settings = new()
        {
            SonarrSnapshots = [new() { InstanceId = "s" }], RadarrSnapshots = [new() { InstanceId = "r" }],
            LibraryFilterValues = [new() { VideoCodecs = ["HEVC"] }]
        };
        StubSettingsService service = new(settings);
        MethodInfo handler = typeof(SettingsEndpoints).GetMethod("SaveGeneralSettingsAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task<Results<NoContent, ValidationProblem>>)handler.Invoke(null,
            [new GeneralSettingsUpdateRequest { Language = AppLanguage.De, Theme = ThemeMode.Dark }, service, CancellationToken.None])!;

        Assert.IsType<NoContent>((await task).Result);
        Assert.NotNull(service.Saved);
        Assert.Equal(AppLanguage.De, service.Saved.Language);
        Assert.Equal(ThemeMode.Dark, service.Saved.Theme);
        Assert.Same(settings.SonarrSnapshots, service.Saved.SonarrSnapshots);
        Assert.Same(settings.RadarrSnapshots, service.Saved.RadarrSnapshots);
        Assert.Same(settings.LibraryFilterValues, service.Saved.LibraryFilterValues);
    }

    private sealed class StubSettingsService(AppSettings settings) : ISettingsService
    {
        public AppSettings? Saved { get; private set; }
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken)
        {
            Saved = value;
            return Task.FromResult(value);
        }
    }
}
