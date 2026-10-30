using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Endpoints;

public sealed class SeerrSettingsEndpointTests
{
    [Fact]
    public async Task GetSeerr_ReturnsNoContent_WhenNotConfigured()
    {
        StubSettingsService service = new();

        Results<Ok<SeerrInstanceSettings>, NoContent> result = await Invoke<Results<Ok<SeerrInstanceSettings>, NoContent>>(
            "GetSeerrInstanceAsync", service, CancellationToken.None);

        Assert.IsType<NoContent>(result.Result);
    }

    [Fact]
    public async Task SaveSeerr_NormalizesAndPersistsInstance()
    {
        StubSettingsService service = new();

        Results<NoContent, ValidationProblem> result = await SaveSeerr(service, new SeerrInstanceSettings
        {
            BaseUrl = "https://seerr.local/",
            ApiKey = " secret "
        });

        Assert.IsType<NoContent>(result.Result);
        Assert.Equal(1, service.Saves);
        SeerrInstanceSettings saved = service.Settings.Seerr!;
        Assert.Equal("https://seerr.local", saved.BaseUrl);
        Assert.Equal("secret", saved.ApiKey);
        Assert.False(string.IsNullOrWhiteSpace(saved.Id));
    }

    [Theory]
    [InlineData("not-an-absolute-url", "secret", "seerr.baseUrl")]
    [InlineData("https://seerr.local", "", "seerr.apiKey")]
    public async Task SaveSeerr_InvalidInstance_ReturnsValidationProblemAndDoesNotSave(
        string baseUrl,
        string apiKey,
        string expectedErrorKey)
    {
        StubSettingsService service = new();
        SeerrInstanceSettings instance = new()
        {
            BaseUrl = baseUrl,
            ApiKey = apiKey
        };

        Results<NoContent, ValidationProblem> result = await SaveSeerr(service, instance);

        Assert.IsType<ValidationProblem>(result.Result);
        Assert.Contains(expectedErrorKey, AppSettingsValidator.ValidateSeerr(instance).Keys);
        Assert.Equal(0, service.Saves);
        Assert.Null(service.Settings.Seerr);
    }

    [Fact]
    public async Task SaveSeerr_OverwritesPreviouslyConfiguredInstance()
    {
        StubSettingsService service = new();
        await SaveSeerr(service, CreateInstance());

        Results<NoContent, ValidationProblem> result = await SaveSeerr(service, new SeerrInstanceSettings
        {
            BaseUrl = "https://second.seerr.local",
            ApiKey = "secret"
        });

        Assert.IsType<NoContent>(result.Result);
        Assert.Equal(2, service.Saves);
        Assert.Equal("https://second.seerr.local", service.Settings.Seerr!.BaseUrl);
    }

    [Fact]
    public async Task GetSeerr_ReturnsSavedInstance()
    {
        StubSettingsService service = new();
        await SaveSeerr(service, CreateInstance());

        Results<Ok<SeerrInstanceSettings>, NoContent> result = await Invoke<Results<Ok<SeerrInstanceSettings>, NoContent>>(
            "GetSeerrInstanceAsync", service, CancellationToken.None);

        Assert.Equal("https://seerr.local", Assert.IsType<Ok<SeerrInstanceSettings>>(result.Result).Value!.BaseUrl);
    }

    [Fact]
    public async Task DeleteSeerr_ClearsConfigurationAndKeepsOtherSettings()
    {
        StubSettingsService service = new();
        await SaveSeerr(service, CreateInstance());

        await Invoke<NoContent>("DeleteSeerrInstanceAsync", service, CancellationToken.None);

        Assert.Null(service.Settings.Seerr);
        Assert.Equal(AppLanguage.De, service.Settings.Language);
        Assert.Equal(2, service.Saves);
    }

    private static Task<Results<NoContent, ValidationProblem>> SaveSeerr(StubSettingsService service, SeerrInstanceSettings instance) =>
        Invoke<Results<NoContent, ValidationProblem>>("SaveSeerrInstanceAsync", instance, service, CancellationToken.None);

    private static SeerrInstanceSettings CreateInstance()
    {
        return new SeerrInstanceSettings
        {
            BaseUrl = "https://seerr.local",
            ApiKey = "secret"
        };
    }

    private static Task<T> Invoke<T>(string method, params object?[] arguments) =>
        (Task<T>)typeof(SettingsEndpoints).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments)!;

    private sealed class StubSettingsService : ISettingsService
    {
        public AppSettings Settings { get; private set; } = new() { Language = AppLanguage.De };
        public int Saves { get; private set; }

        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(AppSettingsNormalizer.Normalize(Settings));

        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken)
        {
            Settings = AppSettingsNormalizer.Normalize(value);
            Saves += 1;
            return Task.FromResult(Settings);
        }
    }
}
