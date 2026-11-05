using System.Reflection;
using System.Text.Json;
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

    [Fact]
    public async Task SaveGeneralSettings_KeepsStoredAuthProtection_WhenTheRequestOmitsIt()
    {
        AppSettings settings = new()
        {
            AuthProtection = new AuthProtectionSettings { RequestsPerMinute = 3, MaxFailedAttempts = 2 }
        };
        StubSettingsService service = new(settings);

        await SaveGeneral(new GeneralSettingsUpdateRequest { Language = AppLanguage.De, Theme = ThemeMode.Dark }, service);

        Assert.NotNull(service.Saved);
        Assert.Equal(3, service.Saved.AuthProtection.RequestsPerMinute);
        Assert.Equal(2, service.Saved.AuthProtection.MaxFailedAttempts);
    }

    [Fact]
    public async Task SaveGeneralSettings_AppliesAuthProtection_WhenTheRequestCarriesIt()
    {
        StubSettingsService service = new(new AppSettings());

        await SaveGeneral(
            new GeneralSettingsUpdateRequest
            {
                Language = AppLanguage.De,
                Theme = ThemeMode.Dark,
                AuthProtection = new AuthProtectionSettings { RequestsPerMinute = 42, MaxFailedAttempts = 7 }
            },
            service);

        Assert.NotNull(service.Saved);
        Assert.Equal(42, service.Saved.AuthProtection.RequestsPerMinute);
        Assert.Equal(7, service.Saved.AuthProtection.MaxFailedAttempts);
    }

    [Fact]
    public async Task FullPut_KeepsStoredAuthProtection_WhenTheBodyOmitsTheSection()
    {
        AppSettings settings = new()
        {
            AuthProtection = new AuthProtectionSettings { RequestsPerMinute = 3, MaxFailedAttempts = 2 }
        };
        StubSettingsService service = new(settings);

        Assert.IsType<NoContent>((await SaveFull("""{"language":"de","theme":"dark"}""", service)).Result);
        Assert.NotNull(service.Saved);
        Assert.Equal(3, service.Saved.AuthProtection.RequestsPerMinute);
        Assert.Equal(2, service.Saved.AuthProtection.MaxFailedAttempts);
    }

    [Fact]
    public async Task FullPut_RejectsAnOutOfRangeAuthProtectionLimit_WithoutSaving()
    {
        StubSettingsService service = new(new AppSettings());

        var result = await SaveFull(
            """{"language":"de","theme":"dark","authProtection":{"requestsPerMinute":0,"maxFailedAttempts":5}}""",
            service);

        Assert.IsType<ValidationProblem>(result.Result);
        Assert.Null(service.Saved);
    }

    private static Task<Results<NoContent, ValidationProblem>> SaveGeneral(
        GeneralSettingsUpdateRequest request,
        ISettingsService service) =>
        Invoke<Results<NoContent, ValidationProblem>>("SaveGeneralSettingsAsync", request, service, CancellationToken.None);

    private static Task<Results<NoContent, ValidationProblem>> SaveFull(string json, ISettingsService service) =>
        Invoke<Results<NoContent, ValidationProblem>>(
            "SaveSettingsAsync", JsonSerializer.Deserialize<JsonElement>(json), service, CancellationToken.None);

    private static Task<T> Invoke<T>(string method, params object[] arguments) =>
        (Task<T>)typeof(SettingsEndpoints).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments)!;

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
