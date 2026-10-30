using System.Net;
using System.Text;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Tests.Clients;

public sealed class SeerrClientRequestsTests
{
    [Fact]
    public async Task GetRequests_MapsPagedSeerrResponse()
    {
        const string json = """
            {
              "pageInfo": { "page": 1, "pageSize": 10, "results": 12, "pages": 2 },
              "results": [
                {
                  "id": 3,
                  "mediaId": 8,
                  "tmdbId": 603,
                  "mediaType": "movie",
                  "status": 2,
                  "is4k": true,
                  "createdAt": "2026-10-01T12:30:00.000Z",
                  "requestedBy": { "id": 1, "displayName": "alice" },
                  "media": {
                    "id": 8,
                    "tmdbId": 603,
                    "mediaType": "movie",
                    "title": "Sample Movie",
                    "coverPath": "/covers/local.jpg",
                    "posterPath": "/posters/sample.jpg",
                    "releaseDate": "2011-03-01"
                  }
                }
              ]
            }
            """;
        StubJsonHandler handler = new(json);
        SeerrClient client = new(new HttpClient(handler));

        SeerrRequestsResponse response = await client.GetRequestsAsync(
            CreateInstance(), 1, 10, "createdAt", "desc", "approved", CancellationToken.None);

        Assert.Equal(12, response.Total);
        Assert.Equal(1, response.Page);
        Assert.Equal(10, response.Limit);
        SeerrRequest request = Assert.Single(response.Requests);
        Assert.Equal("3", request.Id);
        Assert.Equal("approved", request.Status);
        Assert.True(request.Is4kRequest);
        Assert.Equal("movie", request.Type);
        Assert.Equal("alice", request.RequestedBy);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 30, 0, TimeSpan.Zero), request.RequestedAt);
        Assert.Equal("Sample Movie", request.Title);
        Assert.Equal("/posters/sample.jpg", request.PosterPath);
        Assert.Equal(2011, request.Year);
    }

    [Fact]
    public async Task GetRequests_HumanizesExternalServiceSlugWhenTitleIsMissing()
    {
        const string json = """
            {
              "results": [
                {
                  "id": 7,
                  "status": 2,
                  "type": "tv",
                  "media": { "mediaType": "tv", "externalServiceSlug": "a-wild-last-boss-appeared" }
                }
              ]
            }
            """;
        SeerrClient client = new(new HttpClient(new StubJsonHandler(json)));

        SeerrRequestsResponse response = await client.GetRequestsAsync(
            CreateInstance(), 1, 10, "createdAt", "desc", null, CancellationToken.None);

        SeerrRequest request = Assert.Single(response.Requests);
        Assert.Equal("A wild last boss appeared", request.Title);
        Assert.Equal("tv", request.Type);
    }

    [Fact]
    public async Task GetRequests_SlicesRequestedWindowFromFetchedHead()
    {
        // Seerr always answers with the first take entries; the page window is cut in memory.
        const string json = """
            {
              "pageInfo": { "page": 1, "pageSize": 6, "results": 6, "pages": 3 },
              "results": [
                { "id": 1, "status": 2 },
                { "id": 2, "status": 2 },
                { "id": 3, "status": 2 },
                { "id": 4, "status": 2 },
                { "id": 5, "status": 2 },
                { "id": 6, "status": 2 }
              ]
            }
            """;
        StubJsonHandler handler = new(json);
        SeerrClient client = new(new HttpClient(handler));

        SeerrRequestsResponse response = await client.GetRequestsAsync(
            CreateInstance(), 3, 2, "createdAt", "desc", null, CancellationToken.None);

        Assert.Contains("take=6", handler.CapturedRequest!.RequestUri!.Query);
        Assert.Equal(6, response.Total);
        Assert.Equal(3, response.Page);
        Assert.Equal(2, response.Limit);
        Assert.Equal(["5", "6"], response.Requests.Select((entry) => entry.Id));
    }

    [Fact]
    public async Task GetRequests_KeepsRequestsWhenMediaAndRequesterAreMissing()
    {
        const string json = """
            {
              "results": [
                { "id": 5, "status": 1, "is4k": false, "createdAt": "not-a-date" }
              ]
            }
            """;
        StubJsonHandler handler = new(json);
        SeerrClient client = new(new HttpClient(handler));

        SeerrRequestsResponse response = await client.GetRequestsAsync(
            CreateInstance(), 1, 15, "createdAt", "desc", null, CancellationToken.None);

        // Missing pageInfo falls back to the requested page/limit and the fetched result count.
        Assert.Equal(1, response.Total);
        Assert.Equal(1, response.Page);
        Assert.Equal(15, response.Limit);
        SeerrRequest request = Assert.Single(response.Requests);
        Assert.Equal("5", request.Id);
        Assert.Equal("pending", request.Status);
        Assert.Null(request.Title);
        Assert.Null(request.PosterPath);
        Assert.Null(request.Year);
        Assert.Null(request.RequestedBy);
        Assert.Null(request.RequestedAt);
        Assert.Null(request.Type);
    }

    [Theory]
    [InlineData("1", "pending")]
    [InlineData("2", "approved")]
    [InlineData("3", "declined")]
    [InlineData("4", "processing")]
    [InlineData("5", "available")]
    [InlineData("6", "failed")]
    [InlineData("\"Pending\"", "pending")]
    [InlineData("\"available\"", "available")]
    [InlineData("99", "unknown")]
    [InlineData("\"weird-status\"", "unknown")]
    public async Task GetRequests_MapsNumericAndTextStatusValues(string statusJson, string expectedStatus)
    {
        string json = $$"""{ "results": [ { "id": 1, "status": {{statusJson}} } ] }""";
        SeerrClient client = new(new HttpClient(new StubJsonHandler(json)));

        SeerrRequestsResponse response = await client.GetRequestsAsync(
            CreateInstance(), 1, 10, "createdAt", "desc", null, CancellationToken.None);

        Assert.Equal(expectedStatus, Assert.Single(response.Requests).Status);
    }

    [Fact]
    public async Task GetRequests_FallsBackToCoverPathWhenPosterPathIsMissing()
    {
        const string json = """
            {
              "results": [
                {
                  "id": 2,
                  "status": 2,
                  "mediaType": "tv",
                  "media": { "coverPath": "/covers/show.jpg", "firstAirDate": "2019-04-11" }
                }
              ]
            }
            """;
        SeerrClient client = new(new HttpClient(new StubJsonHandler(json)));

        SeerrRequestsResponse response = await client.GetRequestsAsync(
            CreateInstance(), 1, 10, "createdAt", "desc", null, CancellationToken.None);

        SeerrRequest request = Assert.Single(response.Requests);
        Assert.Equal("/covers/show.jpg", request.PosterPath);
        Assert.Equal("tv", request.Type);
        Assert.Equal(2019, request.Year);
    }

    [Fact]
    public async Task GetRequests_KeepsSeasonsNullWhenTheRequestCarriesNone()
    {
        const string json = """
            {
              "results": [
                {
                  "id": 3,
                  "status": 2,
                  "mediaType": "movie",
                  "seasons": [],
                  "media": { "mediaType": "movie", "tmdbId": 603 }
                },
                {
                  "id": 4,
                  "status": 2,
                  "type": "tv",
                  "seasons": [ { "id": 9, "status": 5 } ],
                  "media": { "mediaType": "tv" }
                }
              ]
            }
            """;
        SeerrClient client = new(new HttpClient(new StubJsonHandler(json)));

        SeerrRequestsResponse response = await client.GetRequestsAsync(
            CreateInstance(), 1, 10, "createdAt", "desc", null, CancellationToken.None);

        // Movies never carry seasons; tv entries without a usable seasonNumber behave alike.
        Assert.All(response.Requests, (request) => Assert.Null(request.Seasons));
    }

    [Fact]
    public async Task GetRequests_MapsSeasonNumbersSortedAndDeduplicated()
    {
        const string json = """
            {
              "results": [
                {
                  "id": 302,
                  "status": 5,
                  "type": "tv",
                  "seasons": [
                    { "id": 310, "seasonNumber": 2, "status": 5 },
                    { "id": 309, "seasonNumber": 1, "status": 5 },
                    { "id": 417, "seasonNumber": 0, "status": 1 },
                    { "id": 418, "seasonNumber": 2, "status": 5 }
                  ],
                  "media": { "mediaType": "tv" }
                }
              ]
            }
            """;
        SeerrClient client = new(new HttpClient(new StubJsonHandler(json)));

        SeerrRequestsResponse response = await client.GetRequestsAsync(
            CreateInstance(), 1, 10, "createdAt", "desc", null, CancellationToken.None);

        Assert.Equal([0, 1, 2], Assert.Single(response.Requests).Seasons);
    }

    [Fact]
    public async Task GetRequests_SendsSeerrWindowQueryAndApiKeyHeader()
    {
        StubJsonHandler handler = new("""{ "results": [] }""");
        SeerrClient client = new(new HttpClient(handler));

        await client.GetRequestsAsync(
            CreateInstance(), 2, 5, "createdAt", "asc", "Pending", CancellationToken.None);

        HttpRequestMessage captured = handler.CapturedRequest!;
        Assert.Equal("/api/v1/request", captured.RequestUri!.AbsolutePath);
        string query = captured.RequestUri!.Query;
        // Seerr has no page parameter; the window is fetched as the first page * limit entries.
        Assert.Contains("take=10", query);
        Assert.Contains("sort=added", query);
        Assert.Contains("sortDirection=asc", query);
        Assert.Contains("filter=pending", query);
        Assert.DoesNotContain("page=", query);
        Assert.DoesNotContain("sortField=", query);
        Assert.DoesNotContain("sortOrder=", query);
        Assert.Equal("secret", Assert.Single(captured.Headers.GetValues("X-Api-Key")));
    }

    [Fact]
    public async Task GetRequests_MapsUpdatedAtSortFieldToSeerrModifiedSort()
    {
        StubJsonHandler handler = new("""{ "results": [] }""");
        SeerrClient client = new(new HttpClient(handler));

        await client.GetRequestsAsync(
            CreateInstance(), 1, 20, "updatedAt", "desc", null, CancellationToken.None);

        string query = handler.CapturedRequest!.RequestUri!.Query;
        Assert.Contains("sort=modified", query);
        Assert.Contains("sortDirection=desc", query);
        // Without a status filter the request must not send the filter parameter at all.
        Assert.DoesNotContain("filter=", query);
    }

    [Fact]
    public async Task GetRequests_EnrichesMoviesFromTheMetadataProxy()
    {
        // The live list carries no movie title or poster; only the metadata proxy supplies them.
        const string listJson = """
            {
              "results": [
                {
                  "id": 1,
                  "status": 2,
                  "type": "movie",
                  "media": { "mediaType": "movie", "tmdbId": 383498, "externalServiceSlug": "383498" }
                }
              ]
            }
            """;
        const string metadataJson = """
            {
              "title": "Deadpool & Wolverine",
              "posterPath": "/mbUbTCc5oc5chSfVmJEAfcz19mn.jpg",
              "releaseDate": "2024-07-26"
            }
            """;
        StubMetadataHandler handler = new(listJson, metadataJson);
        SeerrClient client = new(new HttpClient(handler));

        SeerrRequestsResponse response = await client.GetRequestsAsync(
            CreateInstance(), 1, 10, "createdAt", "desc", null, CancellationToken.None);

        SeerrRequest request = Assert.Single(response.Requests);
        Assert.Equal("Deadpool & Wolverine", request.Title);
        Assert.Equal(2024, request.Year);
        // The poster renders through this app's cache endpoint instead of the foreign host.
        Assert.Equal("/api/requests/movie/poster/383498", request.PosterPath);
        Assert.Equal(["/api/v1/movie/383498"], handler.MetadataPaths);
    }

    [Fact]
    public async Task GetRequests_EnrichesSeriesFromTheTvMetadataRoute()
    {
        const string listJson = """
            {
              "results": [
                {
                  "id": 7,
                  "status": 2,
                  "type": "tv",
                  "media": { "mediaType": "tv", "tmdbId": 280042, "externalServiceSlug": "a-wild-last-boss-appeared" }
                }
              ]
            }
            """;
        const string metadataJson = """
            {
              "name": "A Wild Last Boss Appeared",
              "posterPath": "/uLkFJc0LZdNT6zNHpX0BpVjFg9e.jpg",
              "firstAirDate": "2025-10-05"
            }
            """;
        StubMetadataHandler handler = new(listJson, metadataJson);
        SeerrClient client = new(new HttpClient(handler));

        SeerrRequestsResponse response = await client.GetRequestsAsync(
            CreateInstance(), 1, 10, "createdAt", "desc", null, CancellationToken.None);

        SeerrRequest request = Assert.Single(response.Requests);
        Assert.Equal("A Wild Last Boss Appeared", request.Title);
        Assert.Equal(2025, request.Year);
        Assert.Equal("/api/requests/tv/poster/280042", request.PosterPath);
        Assert.Equal(["/api/v1/tv/280042"], handler.MetadataPaths);
    }

    [Fact]
    public async Task GetRequests_EnrichesOnlyTheDisplayedWindow()
    {
        // Page 2 of limit 2 fetches the first four entries; only the sliced window is enriched.
        const string listJson = """
            {
              "pageInfo": { "page": 1, "pageSize": 4, "results": 4, "pages": 2 },
              "results": [
                { "id": 1, "status": 2, "type": "movie", "media": { "mediaType": "movie", "tmdbId": 11 } },
                { "id": 2, "status": 2, "type": "movie", "media": { "mediaType": "movie", "tmdbId": 12 } },
                { "id": 3, "status": 2, "type": "movie", "media": { "mediaType": "movie", "tmdbId": 13 } },
                { "id": 4, "status": 2, "type": "movie", "media": { "mediaType": "movie", "tmdbId": 14 } }
              ]
            }
            """;
        StubMetadataHandler handler = new(listJson, """{ "title": "Ignored" }""");
        SeerrClient client = new(new HttpClient(handler));

        SeerrRequestsResponse response = await client.GetRequestsAsync(
            CreateInstance(), 2, 2, "createdAt", "desc", null, CancellationToken.None);

        Assert.Equal(["3", "4"], response.Requests.Select((entry) => entry.Id));
        Assert.Equal(["/api/v1/movie/13", "/api/v1/movie/14"], handler.MetadataPaths.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetRequests_KeepsListFallbacks_WhenTheMetadataRequestFails()
    {
        // The metadata proxy answers unknown ids with HTTP 500; the readable slug must survive.
        const string listJson = """
            {
              "results": [
                {
                  "id": 7,
                  "status": 2,
                  "type": "tv",
                  "media": { "mediaType": "tv", "tmdbId": 280042, "externalServiceSlug": "a-wild-last-boss-appeared" }
                }
              ]
            }
            """;
        StubMetadataHandler handler = new(listJson, """{ "message": "Unable to retrieve tv." }""", HttpStatusCode.InternalServerError);
        SeerrClient client = new(new HttpClient(handler));

        SeerrRequestsResponse response = await client.GetRequestsAsync(
            CreateInstance(), 1, 10, "createdAt", "desc", null, CancellationToken.None);

        SeerrRequest request = Assert.Single(response.Requests);
        Assert.Equal("A wild last boss appeared", request.Title);
        Assert.Null(request.PosterPath);
        Assert.Null(request.Year);
    }

    [Fact]
    public async Task GetRequests_HidesNumericExternalServiceSlug_WhenTheMetadataRequestFails()
    {
        // Movie slugs are the bare numeric external id and must never render as a title.
        const string listJson = """
            {
              "results": [
                {
                  "id": 1,
                  "status": 2,
                  "type": "movie",
                  "media": { "mediaType": "movie", "tmdbId": 383498, "externalServiceSlug": "383498" }
                }
              ]
            }
            """;
        StubMetadataHandler handler = new(listJson, """{ "message": "Unable to retrieve movie." }""", HttpStatusCode.InternalServerError);
        SeerrClient client = new(new HttpClient(handler));

        SeerrRequestsResponse response = await client.GetRequestsAsync(
            CreateInstance(), 1, 10, "createdAt", "desc", null, CancellationToken.None);

        SeerrRequest request = Assert.Single(response.Requests);
        Assert.Null(request.Title);
        Assert.Null(request.PosterPath);
    }

    [Fact]
    public async Task GetRequests_ThrowsHttpRequestException_WhenSeerrFails()
    {
        SeerrClient client = new(new HttpClient(new StubStatusHandler(HttpStatusCode.InternalServerError)));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetRequestsAsync(
            CreateInstance(), 1, 10, "createdAt", "desc", null, CancellationToken.None));
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

    // Answers the request list path and the metadata proxy paths independently and records the
    // metadata calls so enrichment can be asserted per media id.
    private sealed class StubMetadataHandler(
        string listJson,
        string metadataJson,
        HttpStatusCode metadataStatusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> MetadataPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            bool isListRequest = path == "/api/v1/request";
            if (!isListRequest)
            {
                MetadataPaths.Add(path);
            }

            HttpResponseMessage response = new(isListRequest ? HttpStatusCode.OK : metadataStatusCode)
            {
                Content = new StringContent(
                    isListRequest ? listJson : metadataJson,
                    Encoding.UTF8,
                    "application/json"),
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
