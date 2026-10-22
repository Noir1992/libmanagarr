using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Endpoints;

public sealed class FixedLoggingSettingsEndpointTests
{
    [Fact]
    public async Task SaveLogging_ShouldIgnoreLegacyPathAndMigrationFlag()
    {
        LoggingSettingsUpdateRequest request = JsonSerializer.Deserialize<LoggingSettingsUpdateRequest>(
            """{"logging":{"path":"../outside","retentionDays":7,"minimumLevel":"Warning"},"moveExistingLogs":true}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            })!;
        SettingsStub service = new();
        var operation = (Task<Results<NoContent, ValidationProblem>>)typeof(SettingsEndpoints)
            .GetMethod("SaveLoggingSettingsAsync", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [request, service, CancellationToken.None])!;

        Assert.IsType<NoContent>((await operation).Result);
        Assert.Equal("./logs", service.Settings.Logging.Path);
        Assert.Equal(7, service.Settings.Logging.RetentionDays);
        Assert.Equal(LogLevel.Warning, service.Settings.Logging.MinimumLevel);
        Assert.Equal(AppLanguage.Fr, service.Settings.Language);
        Assert.Equal(["The"], service.Settings.TitleSorting.IgnoredLeadingWords);
    }

    private sealed class SettingsStub : ISettingsService
    {
        public AppSettings Settings { get; private set; } = new()
        {
            Language = AppLanguage.Fr,
            TitleSorting = new() { Enabled = true, IgnoredLeadingWords = ["The"] }
        };

        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(AppSettingsNormalizer.Normalize(Settings));

        public Task<AppSettings> SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            Settings = settings;
            return Task.FromResult(settings);
        }
    }
}
