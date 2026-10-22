using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace LibManagarr.Api.Tests;

[Collection("auth-host")]
public sealed class CsrfCacheHeadersTests
{
    [Fact]
    public async Task IssueCsrfToken_ShouldPreventCachingWithoutHeaderOverrideWarnings()
    {
        using AuthTestAppFactory factory = new();
        using AntiforgeryLogProvider logs = new();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using HttpClient client = host.CreateClient();

        for (int request = 0; request < 2; request++)
        {
            using HttpResponseMessage response = await client.GetAsync("/api/auth/csrf");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoCache);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Contains(response.Headers.Pragma, header => header.Name == "no-cache");
            Assert.Contains(response.Headers.GetValues("Set-Cookie"),
                cookie => cookie.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        }

        Assert.DoesNotContain(logs.Events, entry => entry.Level >= LogLevel.Warning && entry.EventId.Id == 8);
    }

    private sealed class AntiforgeryLogProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, EventId EventId)> Events { get; } = new();

        public ILogger CreateLogger(string categoryName) => new AntiforgeryLogger(
            Events, categoryName == "Microsoft.AspNetCore.Antiforgery.DefaultAntiforgery");

        public void Dispose() { }

        private sealed class AntiforgeryLogger(
            ConcurrentQueue<(LogLevel Level, EventId EventId)> events,
            bool enabled) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => enabled;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (enabled) events.Enqueue((logLevel, eventId));
            }
        }
    }
}
