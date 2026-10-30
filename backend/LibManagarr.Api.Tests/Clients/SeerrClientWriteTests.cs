using System.Net;
using System.Text;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Tests.Clients;

// Client tests for the write operations; the routes and body shapes follow the contract that
// was verified against the live Seerr instance (approve/decline suffixes, DELETE verb, PUT
// requiring mediaType).
public sealed class SeerrClientWriteTests
{
    [Fact]
    public async Task ApproveRequest_SendsPostToTheApproveRouteWithApiKey()
    {
        StubWriteHandler handler = new();
        SeerrClient client = new(new HttpClient(handler));

        await client.ApproveRequestAsync(CreateInstance(), 12, CancellationToken.None);

        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("/api/v1/request/12/approve", sent.Path);
        Assert.Equal("secret", sent.ApiKey);
        Assert.Equal("", sent.Body);
    }

    [Fact]
    public async Task DeclineRequest_SendsReasonInBody()
    {
        StubWriteHandler handler = new();
        SeerrClient client = new(new HttpClient(handler));

        await client.DeclineRequestAsync(CreateInstance(), 12, "  not now  ", CancellationToken.None);

        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/request/12/decline", sent.Path);
        Assert.Contains("\"reason\":\"not now\"", sent.Body);
    }

    [Fact]
    public async Task DeclineRequest_OmitsBody_WhenReasonIsMissing()
    {
        StubWriteHandler handler = new();
        SeerrClient client = new(new HttpClient(handler));

        await client.DeclineRequestAsync(CreateInstance(), 12, null, CancellationToken.None);

        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal("", sent.Body);
    }

    [Fact]
    public async Task DeleteRequest_SendsTheDeleteVerbToTheRequestResource()
    {
        StubWriteHandler handler = new();
        SeerrClient client = new(new HttpClient(handler));

        await client.DeleteRequestAsync(CreateInstance(), 42, CancellationToken.None);

        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, sent.Method);
        Assert.Equal("/api/v1/request/42", sent.Path);
    }

    [Fact]
    public async Task UpdateRequest_SendsCamelCaseSeerrBody()
    {
        StubWriteHandler handler = new();
        SeerrClient client = new(new HttpClient(handler));

        await client.UpdateRequestAsync(
            CreateInstance(),
            12,
            new SeerrRequestUpdateResource
            {
                MediaType = "tv",
                ServerId = 0,
                ProfileId = 18,
                RootFolder = "/media/series",
                LanguageProfileId = 3,
                Is4k = true,
                Seasons = [1, 2],
            },
            CancellationToken.None);

        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, sent.Method);
        Assert.Equal("/api/v1/request/12", sent.Path);
        Assert.Contains("\"mediaType\":\"tv\"", sent.Body);
        Assert.Contains("\"serverId\":0", sent.Body);
        Assert.Contains("\"profileId\":18", sent.Body);
        Assert.Contains("\"rootFolder\":\"/media/series\"", sent.Body);
        Assert.Contains("\"languageProfileId\":3", sent.Body);
        Assert.Contains("\"is4k\":true", sent.Body);
        // Live-verified wire format: seasons travel as a plain int array.
        Assert.Contains("\"seasons\":[1,2]", sent.Body);
    }

    [Fact]
    public async Task UpdateRequest_OmitsUnsetOptionalFields()
    {
        StubWriteHandler handler = new();
        SeerrClient client = new(new HttpClient(handler));

        await client.UpdateRequestAsync(
            CreateInstance(),
            12,
            new SeerrRequestUpdateResource { MediaType = "movie", ServerId = 1 },
            CancellationToken.None);

        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Contains("\"serverId\":1", sent.Body);
        Assert.DoesNotContain("profileId", sent.Body);
        Assert.DoesNotContain("rootFolder", sent.Body);
        Assert.DoesNotContain("languageProfileId", sent.Body);
        Assert.DoesNotContain("is4k", sent.Body);
        Assert.DoesNotContain("seasons", sent.Body);
    }

    [Fact]
    public async Task GetRequestDetail_MapsEditableFields()
    {
        const string json = """
            {
              "id": 302,
              "status": 2,
              "type": 2,
              "is4k": false,
              "serverId": 0,
              "profileId": 18,
              "rootFolder": "/media/videos/series (anime)",
              "languageProfileId": 1
            }
            """;
        StubWriteHandler handler = new(json);
        SeerrClient client = new(new HttpClient(handler));

        SeerrRequestResource? detail = await client.GetRequestDetailAsync(CreateInstance(), 302, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(0, detail.ServerId);
        Assert.Equal(18, detail.ProfileId);
        Assert.Equal("/media/videos/series (anime)", detail.RootFolder);
        Assert.Equal(1, detail.LanguageProfileId);
        Assert.Equal("/api/v1/request/302", Assert.Single(handler.Requests).Path);
    }

    [Fact]
    public async Task GetServers_MapsDisplayFieldsOfTheRequestedApplication()
    {
        const string json = """
            [
              { "id": 0, "name": "Radarr", "apiKey": "plain-secret", "activeDirectory": "/movies", "isDefault": true },
              { "id": 1, "name": "Radarr 4K", "apiKey": "plain-secret", "activeDirectory": "/movies-4k", "isDefault": false }
            ]
            """;
        StubWriteHandler handler = new(json);
        SeerrClient client = new(new HttpClient(handler));

        IReadOnlyList<SeerrServerResource> servers = await client.GetServersAsync(CreateInstance(), "radarr", CancellationToken.None);

        Assert.Equal(["Radarr", "Radarr 4K"], servers.Select((server) => server.Name));
        Assert.Equal([0, 1], servers.Select((server) => server.Id));
        Assert.Equal("/movies", servers[0].ActiveDirectory);
        Assert.True(servers[0].IsDefault);
        Assert.Equal("/api/v1/settings/radarr", Assert.Single(handler.Requests).Path);
    }

    [Fact]
    public async Task GetServiceInfo_MapsProfilesRootFoldersAndServerDefaults()
    {
        const string json = """
            {
              "server": { "id": 0, "name": "Sonarr", "activeDirectory": "/series", "activeProfileId": 18, "isDefault": true },
              "profiles": [ { "id": 15, "name": "Standard" }, { "id": 18, "name": "High Resolution" } ],
              "rootFolders": [ { "id": 9, "freeSpace": 1000, "path": "/series" }, { "id": 10, "path": "/series (anime)" } ]
            }
            """;
        StubWriteHandler handler = new(json);
        SeerrClient client = new(new HttpClient(handler));

        SeerrServiceResource? service = await client.GetServiceInfoAsync(CreateInstance(), "sonarr", 0, CancellationToken.None);

        Assert.NotNull(service);
        Assert.Equal(18, service.Server?.ActiveProfileId);
        Assert.Equal("/series", service.Server?.ActiveDirectory);
        Assert.Equal([15, 18], [.. service.Profiles!.Select((profile) => profile!.Id)]);
        Assert.Equal("High Resolution", service.Profiles![1]!.Name);
        Assert.Equal(["/series", "/series (anime)"], [.. service.RootFolders!.Select((folder) => folder!.Path!)]);
        Assert.Equal("/api/v1/service/sonarr/0", Assert.Single(handler.Requests).Path);
    }

    [Fact]
    public async Task WriteOperations_SurfaceUpstreamStatusCodeOnTheException()
    {
        // The endpoints map 404 to "Request not found.", so the status must survive the client.
        SeerrClient notFoundClient = new(new HttpClient(new StubWriteHandler("{}", HttpStatusCode.NotFound)));

        HttpRequestException notFound = await Assert.ThrowsAsync<HttpRequestException>(
            () => notFoundClient.ApproveRequestAsync(CreateInstance(), 999_999_999, CancellationToken.None));
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);

        SeerrClient failingClient = new(new HttpClient(new StubWriteHandler("{}", HttpStatusCode.InternalServerError)));

        HttpRequestException failing = await Assert.ThrowsAsync<HttpRequestException>(
            () => failingClient.UpdateRequestAsync(
                CreateInstance(),
                12,
                new SeerrRequestUpdateResource { MediaType = "movie" },
                CancellationToken.None));
        Assert.Equal(HttpStatusCode.InternalServerError, failing.StatusCode);
    }

    private static SeerrInstanceSettings CreateInstance()
    {
        return new SeerrInstanceSettings
        {
            Id = "seerr-1",
            BaseUrl = "http://seerr.local/",
            ApiKey = "secret"
        };
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string ApiKey, string Body);

    private sealed class StubWriteHandler(string json = "{}", HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            string apiKey = request.Headers.TryGetValues("X-Api-Key", out IEnumerable<string>? values)
                ? string.Join(',', values)
                : string.Empty;

            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath, apiKey, body));

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
        }
    }
}
