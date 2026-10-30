using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Endpoints;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Endpoints;

public sealed class SeerrRequestsEndpointTests
{
    [Fact]
    public async Task GetRequests_ReturnsProblem_WhenSeerrIsNotConfigured()
    {
        StubSettingsService service = new(new AppSettings());

        Results<Ok<SeerrRequestsResponse>, ValidationProblem, ProblemHttpResult> result = await InvokeAsync(
            service, CreateClient("""{ "results": [] }"""));

        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.ProblemDetails.Status);
        Assert.Equal(RequestEndpoints.NotConfiguredProblemCode, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task GetRequests_ReturnsOk_WhenSeerrIsConfigured()
    {
        StubSettingsService service = new(new AppSettings { Seerr = CreateSeerrInstance() });
        StubJsonHandler handler = new("""
            {
              "pageInfo": { "page": 1, "pageSize": 20, "results": 1, "totalPages": 1 },
              "results": [ { "id": 1, "status": "pending", "media": { "title": "Sample" } } ]
            }
            """);

        Results<Ok<SeerrRequestsResponse>, ValidationProblem, ProblemHttpResult> result = await InvokeAsync(
            service, new SeerrClient(new HttpClient(handler)), status: "approved");

        SeerrRequestsResponse response = Assert.IsType<Ok<SeerrRequestsResponse>>(result.Result).Value!;
        Assert.Equal(1, response.Total);
        Assert.Equal("Sample", Assert.Single(response.Requests).Title);
        // The endpoint forwards the validated status filter to the Seerr client.
        Assert.Contains("filter=approved", handler.CapturedRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task GetRequests_ReturnsValidationProblem_ForUnknownStatus()
    {
        StubSettingsService service = new(new AppSettings { Seerr = CreateSeerrInstance() });

        Results<Ok<SeerrRequestsResponse>, ValidationProblem, ProblemHttpResult> result = await InvokeAsync(
            service, CreateClient("""{ "results": [] }"""), status: "not-a-status");

        ValidationProblem validation = Assert.IsType<ValidationProblem>(result.Result);
        Assert.Contains("status", validation.ProblemDetails.Errors.Select((pair) => pair.Key));
    }

    [Fact]
    public async Task GetRequests_ReturnsValidationProblem_ForDeclinedStatus()
    {
        // Seerr's filter parameter has no declined value, so the endpoint must reject it.
        StubSettingsService service = new(new AppSettings { Seerr = CreateSeerrInstance() });

        Results<Ok<SeerrRequestsResponse>, ValidationProblem, ProblemHttpResult> result = await InvokeAsync(
            service, CreateClient("""{ "results": [] }"""), status: "declined");

        ValidationProblem validation = Assert.IsType<ValidationProblem>(result.Result);
        Assert.Contains("status", validation.ProblemDetails.Errors.Select((pair) => pair.Key));
    }

    [Fact]
    public async Task GetRequests_ReturnsValidationProblem_WhenPageWindowExceedsFetchLimit()
    {
        StubSettingsService service = new(new AppSettings { Seerr = CreateSeerrInstance() });

        Results<Ok<SeerrRequestsResponse>, ValidationProblem, ProblemHttpResult> result = await InvokeAsync(
            service, CreateClient("""{ "results": [] }"""), page: 51, limit: 20);

        ValidationProblem validation = Assert.IsType<ValidationProblem>(result.Result);
        Assert.Contains("page", validation.ProblemDetails.Errors.Select((pair) => pair.Key));
    }

    [Fact]
    public async Task GetRequests_ReturnsValidationProblem_ForInvalidPageAndLimit()
    {
        StubSettingsService service = new(new AppSettings { Seerr = CreateSeerrInstance() });

        Results<Ok<SeerrRequestsResponse>, ValidationProblem, ProblemHttpResult> result = await InvokeAsync(
            service, CreateClient("""{ "results": [] }"""), page: 0, limit: 500);

        ValidationProblem validation = Assert.IsType<ValidationProblem>(result.Result);
        string[] errorKeys = [.. validation.ProblemDetails.Errors.Select((pair) => pair.Key)];
        Assert.Contains("page", errorKeys);
        Assert.Contains("limit", errorKeys);
    }

    [Fact]
    public async Task GetRequests_ReturnsBadGateway_WhenSeerrIsUnreachable()
    {
        StubSettingsService service = new(new AppSettings { Seerr = CreateSeerrInstance() });
        SeerrClient client = new(new HttpClient(new StubStatusHandler(HttpStatusCode.InternalServerError)));

        Results<Ok<SeerrRequestsResponse>, ValidationProblem, ProblemHttpResult> result = await InvokeAsync(
            service, client);

        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, problem.ProblemDetails.Status);
    }

    private static Task<Results<Ok<SeerrRequestsResponse>, ValidationProblem, ProblemHttpResult>> InvokeAsync(
        StubSettingsService settingsService,
        SeerrClient seerrClient,
        int page = 1,
        int limit = 20,
        string? status = null,
        string? sortField = null,
        string? sortOrder = null)
    {
        return (Task<Results<Ok<SeerrRequestsResponse>, ValidationProblem, ProblemHttpResult>>)typeof(RequestEndpoints)
            .GetMethod("GetRequestsAsync", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [page, limit, status, sortField, sortOrder, settingsService, seerrClient, CancellationToken.None])!;
    }

    private static SeerrClient CreateClient(string json) => new(new HttpClient(new StubJsonHandler(json)));

    private static SeerrInstanceSettings CreateSeerrInstance()
    {
        return new SeerrInstanceSettings
        {
            Id = "seerr-1",
            BaseUrl = "http://seerr.local",
            ApiKey = "secret"
        };
    }

    private sealed class StubSettingsService(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);

        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) => Task.FromResult(value);
    }

    private sealed class StubJsonHandler(string json) : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CapturedRequest = request;

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };

            return Task.FromResult(response);
        }
    }

    private sealed class StubStatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(statusCode)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };

            return Task.FromResult(response);
        }
    }
}
