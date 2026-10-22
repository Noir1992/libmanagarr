using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Endpoints;

public sealed class TitleSortingSettingsEndpointTests
{
    [Fact]
    public async Task SaveTitleSorting_NormalizesAndPreservesOtherSettings()
    {
        StubSettingsService service = new();
        var result = await SaveTitle(service, """{"enabled":true,"ignoredLeadingWords":[" The ","the","A"]}""");
        TitleSortingSettings saved = Assert.IsType<Ok<TitleSortingSettings>>(result.Result).Value!;
        Assert.True(saved.Enabled);
        Assert.Equal(["The", "A"], saved.IgnoredLeadingWords);
        Assert.Equal(AppLanguage.De, service.Settings.Language);
        Assert.Equal(17, service.Settings.Logging.RetentionDays);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"enabled\":true}")]
    [InlineData("{\"enabled\":true,\"ignoredLeadingWords\":null}")]
    [InlineData("{\"enabled\":true,\"ignoredLeadingWords\":[null]}")]
    [InlineData("{\"enabled\":true,\"ignoredLeadingWords\":[\"two words\"]}")]
    public async Task SaveTitleSorting_InvalidBodyDoesNotSave(string json)
    {
        StubSettingsService service = new();
        Assert.IsType<ValidationProblem>((await SaveTitle(service, json)).Result);
        Assert.Equal(0, service.Saves);
    }

    [Fact]
    public async Task FullPut_OmittedFieldPreservesRulesAndExplicitEmptyListClears()
    {
        StubSettingsService service = new();
        service.Settings.TitleSorting = new() { Enabled = true, IgnoredLeadingWords = ["The"] };
        Assert.IsType<NoContent>((await SaveFull(service, "{}")).Result);
        Assert.Equal(["The"], service.Settings.TitleSorting.IgnoredLeadingWords);
        Assert.IsType<NoContent>((await SaveFull(service, """{"titleSorting":{"enabled":true,"ignoredLeadingWords":[]}}""")).Result);
        Assert.Empty(service.Settings.TitleSorting.IgnoredLeadingWords);
        Assert.IsType<ValidationProblem>((await SaveFull(service, """{"titleSorting":null}""")).Result);
        Assert.Equal(2, service.Saves);
    }

    [Fact]
    public async Task OverlappingGeneralAndTitleSavesWaitForSharedReadModifyWriteCoordination()
    {
        StubSettingsService service = new();
        ISettingsService settings = service;
        using IDisposable held = await settings.BeginUpdateAsync(CancellationToken.None);
        Task<Results<Ok<TitleSortingSettings>, ValidationProblem>> title = SaveTitle(service, """{"enabled":true,"ignoredLeadingWords":["The"]}""");
        Task<Results<NoContent, ValidationProblem>> general = Invoke<Results<NoContent, ValidationProblem>>("SaveGeneralSettingsAsync",
            new GeneralSettingsUpdateRequest { Language = AppLanguage.Fr, Theme = ThemeMode.Dark }, service, CancellationToken.None);
        Assert.False(title.IsCompleted);
        Assert.False(general.IsCompleted);
        held.Dispose();
        await Task.WhenAll(title, general);
        Assert.Equal(AppLanguage.Fr, service.Settings.Language);
        Assert.Equal(ThemeMode.Dark, service.Settings.Theme);
        Assert.True(service.Settings.TitleSorting.Enabled);
        Assert.Equal(["The"], service.Settings.TitleSorting.IgnoredLeadingWords);
    }

    private static Task<Results<Ok<TitleSortingSettings>, ValidationProblem>> SaveTitle(StubSettingsService service, string json) =>
        Invoke<Results<Ok<TitleSortingSettings>, ValidationProblem>>("SaveTitleSortingAsync", JsonSerializer.Deserialize<JsonElement>(json), service, CancellationToken.None);

    private static Task<Results<NoContent, ValidationProblem>> SaveFull(StubSettingsService service, string json) =>
        Invoke<Results<NoContent, ValidationProblem>>("SaveSettingsAsync", JsonSerializer.Deserialize<JsonElement>(json), service, CancellationToken.None);

    private static Task<T> Invoke<T>(string method, params object[] arguments) =>
        (Task<T>)typeof(SettingsEndpoints).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments)!;

    private sealed class StubSettingsService : ISettingsService
    {
        public AppSettings Settings { get; private set; } = new() { Language = AppLanguage.De, Logging = new() { RetentionDays = 17 } };
        public int Saves { get; private set; }
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(AppSettingsNormalizer.Normalize(Settings));
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken)
        {
            Settings = AppSettingsNormalizer.Normalize(value);
            Saves++;
            return Task.FromResult(Settings);
        }
    }
}
