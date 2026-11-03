using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using LibManagarr.Api.Clients;

namespace LibManagarr.Api.Tests.Endpoints;

// Uses the "auth-host" collection for the HTTP authentication integration tests.
[Collection("auth-host")]
public sealed class ReleaseEndpointsTests
{
    [Fact]
    public async Task ReleaseRoutes_RequireAuthentication()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/releases")).StatusCode);
    }

    [Fact]
    public async Task ReleaseRoutes_RequireCsrfTokenForTheMutatingRefresh()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAsync(client);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/releases/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task GetReleases_AnswersWithAnEmptyOverview_WhenGitHubCannotBeReached()
    {
        using AuthTestAppFactory factory = new();
        using WebApplicationFactory<Program> host = factory.WithWebHostBuilder((builder) => builder.ConfigureTestServices((services) =>
            services.AddHttpClient<GitHubReleasesClient>().ConfigurePrimaryHttpMessageHandler(
                () => new FailingHandler(new HttpRequestException("no route to host")))));
        using HttpClient client = host.CreateClient();
        await SetupAsync(client);

        using HttpResponseMessage response = await client.GetAsync("/api/releases");
        JsonObject overview = (await response.Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The failure is reported in the body instead of an error status, so the navigation stays cheap.
        Assert.Equal("Unreachable", overview["status"]!.GetValue<string>());
        Assert.False(overview["updateAvailable"]!.GetValue<bool>());
        Assert.Empty(overview["versions"]!.AsArray());
        Assert.True(overview["currentVersion"]!.GetValue<string>().Length > 0);
        // The antiforgery-free GET still carries the shared no-store header for /api responses.
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task RefreshReleases_ServesTheOverviewFromTheStubbedUpstream_WithCsrfToken()
    {
        using AuthTestAppFactory factory = new();
        using WebApplicationFactory<Program> host = factory.WithWebHostBuilder((builder) => builder.ConfigureTestServices((services) =>
            services.AddHttpClient<GitHubReleasesClient>().ConfigurePrimaryHttpMessageHandler(
                () => new StubHandler("""
                    [ { "tag_name": "v9.9.9", "name": "Future", "body": "notes", "html_url": "https://example.test/v9.9.9",
                        "published_at": "2026-10-28T09:12:00Z", "prerelease": false, "draft": false } ]
                    """))));
        using HttpClient client = host.CreateClient();
        await SetupAsync(client);

        using HttpResponseMessage response = await client.PostAsync("/api/releases/refresh", null);
        JsonObject overview = (await response.Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ok", overview["status"]!.GetValue<string>());
        Assert.Equal("v9.9.9", overview["versions"]!.AsArray()[0]!["tag"]!.GetValue<string>());
    }

    [Fact]
    public async Task UnknownApiPaths_StillFallIntoTheNotFoundCatchAll_BelowTheReleaseRoutes()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAsync(client);

        using HttpResponseMessage response = await client.GetAsync("/api/releases/not-a-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task SetupAsync(HttpClient client)
    {
        await SetCsrfAsync(client);
        using HttpResponseMessage setup = await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "testuser", password = "correct-horse-battery-staple", confirmPassword = "correct-horse-battery-staple"
        });
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);
        await SetCsrfAsync(client);
    }

    private static async Task SetCsrfAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/auth/csrf");
        string cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", cookie.Split(';')[0]["XSRF-TOKEN=".Length..]);
    }

    private sealed class FailingHandler(Exception failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw failure;
        }
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }
}
