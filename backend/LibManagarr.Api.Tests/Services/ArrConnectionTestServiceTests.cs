using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class ArrConnectionTestServiceTests
{
    [Fact]
    public async Task TestAsync_ShouldReturnSuccess_ForSonarrConnection()
    {
        ArrConnectionTestService service = CreateService(
            new StubHttpMessageHandler(HttpStatusCode.OK),
            new StubHttpMessageHandler(HttpStatusCode.OK),
            new StubHttpMessageHandler(HttpStatusCode.OK));

        TestConnectionResult result = await service.TestAsync(
            CreateRequest(ArrApplicationKind.Sonarr),
            CancellationToken.None);

        Assert.Equal("Connection successful.", result.Message);
    }

    [Fact]
    public async Task TestAsync_ShouldReturnSuccess_ForSeerrConnection()
    {
        ArrConnectionTestService service = CreateService(
            new StubHttpMessageHandler(HttpStatusCode.OK),
            new StubHttpMessageHandler(HttpStatusCode.OK),
            new StubHttpMessageHandler(HttpStatusCode.OK));

        TestConnectionResult result = await service.TestAsync(
            CreateRequest(ArrApplicationKind.Seerr),
            CancellationToken.None);

        Assert.Equal("Connection successful.", result.Message);
    }

    [Fact]
    public async Task TestAsync_ShouldThrowBadGateway_ForSeerrAuthFailure()
    {
        ArrConnectionTestService service = CreateService(
            new StubHttpMessageHandler(HttpStatusCode.OK),
            new StubHttpMessageHandler(HttpStatusCode.OK),
            new StubHttpMessageHandler(HttpStatusCode.Unauthorized));

        ApiProblemException exception = await Assert.ThrowsAsync<ApiProblemException>(
            () => service.TestAsync(CreateRequest(ArrApplicationKind.Seerr), CancellationToken.None));

        Assert.Equal(StatusCodes.Status502BadGateway, exception.StatusCode);
        Assert.Equal("Connection failed", exception.Title);
    }

    [Fact]
    public async Task TestAsync_ShouldThrowBadGateway_ForHttpRequestError()
    {
        ArrConnectionTestService service = CreateService(
            new StubHttpMessageHandler(HttpStatusCode.BadGateway),
            new StubHttpMessageHandler(HttpStatusCode.OK),
            new StubHttpMessageHandler(HttpStatusCode.OK));

        ApiProblemException exception = await Assert.ThrowsAsync<ApiProblemException>(
            () => service.TestAsync(CreateRequest(ArrApplicationKind.Sonarr), CancellationToken.None));

        Assert.Equal(StatusCodes.Status502BadGateway, exception.StatusCode);
        Assert.Equal("Connection failed", exception.Title);
    }

    [Fact]
    public async Task TestAsync_ShouldThrowBadRequest_ForUnsupportedKind()
    {
        ArrConnectionTestService service = CreateService(
            new StubHttpMessageHandler(HttpStatusCode.OK),
            new StubHttpMessageHandler(HttpStatusCode.OK),
            new StubHttpMessageHandler(HttpStatusCode.OK));

        ApiProblemException exception = await Assert.ThrowsAsync<ApiProblemException>(
            () => service.TestAsync(CreateRequest((ArrApplicationKind)999), CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal("Invalid application kind", exception.Title);
    }

    private static ArrConnectionTestService CreateService(
        HttpMessageHandler sonarrHandler,
        HttpMessageHandler radarrHandler,
        HttpMessageHandler seerrHandler)
    {
        return new ArrConnectionTestService(
            new SonarrClient(new HttpClient(sonarrHandler)),
            new RadarrClient(new HttpClient(radarrHandler)),
            new SeerrClient(new HttpClient(seerrHandler)),
            NullLogger<ArrConnectionTestService>.Instance);
    }

    private static TestConnectionRequest CreateRequest(ArrApplicationKind kind)
    {
        return new TestConnectionRequest
        {
            Kind = kind,
            Instance = new ArrInstanceSettings
            {
                Id = "instance-1",
                Name = "Main",
                BaseUrl = "http://arr.local",
                ApiKey = "secret"
            }
        };
    }

    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(statusCode)
            {
                Content = JsonContent.Create(Array.Empty<object>()),
                RequestMessage = request
            };

            return Task.FromResult(response);
        }
    }
}
