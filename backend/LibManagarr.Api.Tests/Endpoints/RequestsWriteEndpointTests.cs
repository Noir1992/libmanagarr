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

// Endpoint tests for the write operations and the edit-options route. The private handlers are
// invoked through reflection, mirroring the paged list tests.
public sealed class RequestsWriteEndpointTests
{
    [Fact]
    public async Task Approve_ReturnsNoContent_ForwardsApproveToSeerr()
    {
        StubRecordingHandler handler = new("{}");

        IResult result = await InvokeAsync(
            "ApproveRequestAsync",
            [12, new StubSettingsService(Configured()), new SeerrClient(new HttpClient(handler)), CancellationToken.None]);

        Assert.IsType<NoContent>(result);
        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("/api/v1/request/12/approve", sent.Path);
    }

    [Fact]
    public async Task Approve_ReturnsServiceProblem_WhenSeerrIsNotConfigured()
    {
        StubRecordingHandler handler = new("{}");

        IResult result = await InvokeAsync(
            "ApproveRequestAsync",
            [12, new StubSettingsService(new AppSettings()), new SeerrClient(new HttpClient(handler)), CancellationToken.None]);

        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.ProblemDetails.Status);
        Assert.Equal(RequestEndpoints.NotConfiguredProblemCode, problem.ProblemDetails.Extensions["code"]);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Approve_ReturnsNotFound_WhenSeerrAnswersNotFound()
    {
        StubRecordingHandler handler = new("", HttpStatusCode.NotFound);

        IResult result = await InvokeAsync(
            "ApproveRequestAsync",
            [12, new StubSettingsService(Configured()), new SeerrClient(new HttpClient(handler)), CancellationToken.None]);

        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.ProblemDetails.Status);
    }

    [Fact]
    public async Task Decline_ForwardsReasonAsSeerrBody()
    {
        StubRecordingHandler handler = new("{}");

        IResult result = await InvokeAsync(
            "DeclineRequestAsync",
            [12,
                new SeerrRequestDeclineBody { Reason = "not now" },
                new StubSettingsService(Configured()),
                new SeerrClient(new HttpClient(handler)),
                CancellationToken.None]);

        Assert.IsType<NoContent>(result);
        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/request/12/decline", sent.Path);
        Assert.Contains("\"reason\":\"not now\"", sent.Body);
    }

    [Fact]
    public async Task Decline_OmitsBody_WhenNoReasonGiven()
    {
        StubRecordingHandler handler = new("{}");

        IResult result = await InvokeAsync(
            "DeclineRequestAsync",
            [12, null, new StubSettingsService(Configured()), new SeerrClient(new HttpClient(handler)), CancellationToken.None]);

        Assert.IsType<NoContent>(result);
        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal("", sent.Body);
    }

    [Fact]
    public async Task Delete_UsesTheDeleteVerbOnTheRequestResource()
    {
        StubRecordingHandler handler = new("{}");

        IResult result = await InvokeAsync(
            "DeleteRequestAsync",
            [12, new StubSettingsService(Configured()), new SeerrClient(new HttpClient(handler)), CancellationToken.None]);

        Assert.IsType<NoContent>(result);
        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, sent.Method);
        Assert.Equal("/api/v1/request/12", sent.Path);
    }

    [Fact]
    public async Task Update_ReturnsValidationProblem_WhenNoFieldsAreProvided()
    {
        StubRecordingHandler handler = new("{}");

        IResult result = await InvokeAsync(
            "UpdateRequestAsync",
            [12,
                new SeerrRequestUpdateBody(),
                new StubSettingsService(Configured()),
                new SeerrClient(new HttpClient(handler)),
                CancellationToken.None]);

        ValidationProblem validation = Assert.IsType<ValidationProblem>(result);
        Assert.Contains("body", validation.ProblemDetails.Errors.Select((pair) => pair.Key));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Update_EchosDetailFields_AndAppliesEditedProfile()
    {
        StubRecordingHandler handler = new("""
            { "id": 12, "type": 1, "serverId": 3, "profileId": 5, "rootFolder": "/movies", "languageProfileId": 2 }
            """);

        IResult result = await InvokeAsync(
            "UpdateRequestAsync",
            [12,
                new SeerrRequestUpdateBody { ProfileId = 7 },
                new StubSettingsService(Configured()),
                new SeerrClient(new HttpClient(handler)),
                CancellationToken.None]);

        Assert.IsType<NoContent>(result);
        Assert.Equal(2, handler.Requests.Count);

        RecordedRequest detail = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, detail.Method);
        Assert.Equal("/api/v1/request/12", detail.Path);

        RecordedRequest update = handler.Requests[1];
        Assert.Equal(HttpMethod.Put, update.Method);
        Assert.Contains("\"mediaType\":\"movie\"", update.Body);
        Assert.Contains("\"serverId\":3", update.Body);
        Assert.Contains("\"profileId\":7", update.Body);
        Assert.Contains("\"rootFolder\":\"/movies\"", update.Body);
        Assert.Contains("\"languageProfileId\":2", update.Body);
        // Movies carry no seasons, so the field stays out of the body entirely.
        Assert.DoesNotContain("seasons", update.Body);
    }

    [Fact]
    public async Task Update_AppliesEditedSeasons_ForTv()
    {
        StubRecordingHandler handler = new("""
            { "id": 12, "type": 2, "serverId": 0, "profileId": 18, "rootFolder": "/series", "seasons": [ { "seasonNumber": 1 }, { "seasonNumber": 2 } ] }
            """);

        IResult result = await InvokeAsync(
            "UpdateRequestAsync",
            [12,
                new SeerrRequestUpdateBody { Seasons = [3] },
                new StubSettingsService(Configured()),
                new SeerrClient(new HttpClient(handler)),
                CancellationToken.None]);

        Assert.IsType<NoContent>(result);
        RecordedRequest update = handler.Requests[1];
        Assert.Equal(HttpMethod.Put, update.Method);
        Assert.Contains("\"mediaType\":\"tv\"", update.Body);
        Assert.Contains("\"seasons\":[3]", update.Body);
    }

    [Fact]
    public async Task Update_EchosDetailSeasons_WhenUnedited()
    {
        StubRecordingHandler handler = new("""
            { "id": 12, "type": 2, "serverId": 0, "profileId": 18, "rootFolder": "/series", "seasons": [ { "seasonNumber": 1 }, { "seasonNumber": 2 } ] }
            """);

        IResult result = await InvokeAsync(
            "UpdateRequestAsync",
            [12,
                new SeerrRequestUpdateBody { ProfileId = 7 },
                new StubSettingsService(Configured()),
                new SeerrClient(new HttpClient(handler)),
                CancellationToken.None]);

        Assert.IsType<NoContent>(result);
        RecordedRequest update = handler.Requests[1];
        Assert.Contains("\"profileId\":7", update.Body);
        Assert.Contains("\"seasons\":[1,2]", update.Body);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenTheRequestDetailIsMissing()
    {
        StubRecordingHandler handler = new("{\"message\":\"Request not found.\"}", HttpStatusCode.NotFound);

        IResult result = await InvokeAsync(
            "UpdateRequestAsync",
            [12,
                new SeerrRequestUpdateBody { ProfileId = 7 },
                new StubSettingsService(Configured()),
                new SeerrClient(new HttpClient(handler)),
                CancellationToken.None]);

        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.ProblemDetails.Status);
    }

    [Fact]
    public async Task Update_ReturnsBadGateway_WhenTheUpdateFailsUpstream()
    {
        // The detail lookup succeeds but the PUT itself fails with a server error.
        StubStatusSequenceHandler handler = new(
            ("""{ "id": 12, "type": 1, "serverId": 3, "profileId": 5, "rootFolder": "/movies" }""", HttpStatusCode.OK),
            ("{}", HttpStatusCode.InternalServerError));

        IResult result = await InvokeAsync(
            "UpdateRequestAsync",
            [12,
                new SeerrRequestUpdateBody { ProfileId = 7 },
                new StubSettingsService(Configured()),
                new SeerrClient(new HttpClient(handler)),
                CancellationToken.None]);

        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, problem.ProblemDetails.Status);
    }

    [Fact]
    public async Task EditOptions_ResolvesProfilesFoldersAndOwnPreselection_ForMovies()
    {
        StubRoutingHandler handler = new(new Dictionary<string, string>
        {
            ["/api/v1/request/12"] = """
                { "id": 12, "type": 1, "serverId": 0, "profileId": 5, "rootFolder": "/movies-4k" }
                """,
            ["/api/v1/service/radarr/0"] = """
                {
                  "server": { "id": 0, "name": "Radarr", "activeDirectory": "/movies", "activeProfileId": 15, "isDefault": true },
                  "profiles": [ { "id": 15, "name": "Standard" }, { "id": 16, "name": "High Resolution" } ],
                  "rootFolders": [ { "id": 9, "path": "/movies" }, { "id": 10, "path": "/movies (anime)" } ]
                }
                """,
        });

        IResult result = await InvokeAsync(
            "GetEditOptionsAsync",
            [12, new StubSettingsService(Configured()), new SeerrClient(new HttpClient(handler)), CancellationToken.None]);

        SeerrRequestEditOptions options = Assert.IsType<Ok<SeerrRequestEditOptions>>(result).Value!;
        Assert.Equal([15, 16], [.. options.QualityProfiles.Select((profile) => profile.Id)]);
        Assert.Equal(["/movies", "/movies (anime)"], [.. options.RootFolders]);
        // The request's own values win over the server defaults.
        Assert.Equal(5, options.ProfileId);
        Assert.Equal("/movies-4k", options.RootFolder);
        Assert.Empty(options.Seasons);
        // With a serverId on the detail the settings list stays unqueried.
        Assert.DoesNotContain("/api/v1/settings/radarr", handler.Paths);
    }

    [Fact]
    public async Task EditOptions_FallsBackToDefaultServer_AndOffersTvSeasons()
    {
        StubRoutingHandler handler = new(new Dictionary<string, string>
        {
            ["/api/v1/request/303"] = """
                { "id": 303, "type": 2, "media": { "tmdbId": 60572 }, "seasons": [ { "seasonNumber": 2 } ] }
                """,
            ["/api/v1/settings/sonarr"] = """
                [ { "id": 0, "name": "Sonarr", "apiKey": "secret", "activeDirectory": "/series", "isDefault": true } ]
                """,
            ["/api/v1/service/sonarr/0"] = """
                {
                  "server": { "id": 0, "name": "Sonarr", "activeDirectory": "/series", "activeProfileId": 18, "isDefault": true },
                  "profiles": [ { "id": 18, "name": "Standard" } ],
                  "rootFolders": [ { "id": 9, "path": "/series" }, { "id": 10, "path": "/series (anime)" } ]
                }
                """,
            ["/api/v1/tv/60572"] = """
                {
                  "name": "Sample Show",
                  "seasons": [
                    { "seasonNumber": 2, "episodeCount": 8 },
                    { "seasonNumber": 0, "episodeCount": 4 },
                    { "seasonNumber": 1, "episodeCount": 12 }
                  ]
                }
                """,
        });

        IResult result = await InvokeAsync(
            "GetEditOptionsAsync",
            [303, new StubSettingsService(Configured()), new SeerrClient(new HttpClient(handler)), CancellationToken.None]);

        SeerrRequestEditOptions options = Assert.IsType<Ok<SeerrRequestEditOptions>>(result).Value!;
        // The pending request carries nothing yet, so the server defaults preselect.
        Assert.Equal(18, options.ProfileId);
        Assert.Equal("/series", options.RootFolder);
        Assert.Equal([18], [.. options.QualityProfiles.Select((profile) => profile.Id)]);
        Assert.Equal(["/series", "/series (anime)"], [.. options.RootFolders]);
        // Metadata seasons arrive unsorted upstream and come back ascending with their episode
        // counts; only the season this request covers is marked as pending.
        Assert.Equal(
            [
                new SeerrSeasonOption(0, 4, "none"),
                new SeerrSeasonOption(1, 12, "none"),
                new SeerrSeasonOption(2, 8, "pending"),
            ],
            options.Seasons);
        Assert.Contains("/api/v1/settings/sonarr", handler.Paths);
    }

    [Fact]
    public async Task EditOptions_MarksSeasonStatesFromTheMediaDownloadStatuses_WhenTheMetadataStaysSilent()
    {
        // The metadata proxy answers with an upstream error, so the media object's seasons keep
        // the table alive: 5 is on disk, 3 is still downloading and 1 was never requested. The
        // season this request covers stays pending even though it also downloads.
        StubRoutingHandler handler = new(new Dictionary<string, string>
        {
            ["/api/v1/request/302"] = """
                {
                  "id": 302, "type": 2, "serverId": 0,
                  "media": {
                    "tmdbId": 60572,
                    "seasons": [
                      { "seasonNumber": 1, "status": 3 },
                      { "seasonNumber": 2, "status": 5 },
                      { "seasonNumber": 3, "status": 1 }
                    ]
                  },
                  "seasons": [ { "seasonNumber": 1 } ]
                }
                """,
            ["/api/v1/service/sonarr/0"] = """
                {
                  "server": { "id": 0, "name": "Sonarr", "activeDirectory": "/series", "activeProfileId": 18, "isDefault": true },
                  "profiles": [ { "id": 18, "name": "Standard" } ],
                  "rootFolders": [ { "id": 9, "path": "/series" } ]
                }
                """,
        });

        IResult result = await InvokeAsync(
            "GetEditOptionsAsync",
            [302, new StubSettingsService(Configured()), new SeerrClient(new HttpClient(handler)), CancellationToken.None]);

        SeerrSeasonOption[] seasons = [.. Assert.IsType<Ok<SeerrRequestEditOptions>>(result).Value!.Seasons];
        Assert.Equal(
            [
                new SeerrSeasonOption(1, null, "pending"),
                new SeerrSeasonOption(2, null, "available"),
                new SeerrSeasonOption(3, null, "none"),
            ],
            seasons);
    }

    [Fact]
    public async Task EditOptions_PreselectsAnimeDefaults_ForKeywordAnimeSeries()
    {
        StubRoutingHandler handler = new(new Dictionary<string, string>
        {
            ["/api/v1/request/304"] = """
                { "id": 304, "type": 2, "serverId": 0, "media": { "tmdbId": 30984 } }
                """,
            ["/api/v1/service/sonarr/0"] = """
                {
                  "server": {
                    "id": 0, "name": "Sonarr",
                    "activeDirectory": "/series", "activeProfileId": 18,
                    "activeAnimeDirectory": "/series (anime)", "activeAnimeProfileId": 21,
                    "isDefault": true
                  },
                  "profiles": [ { "id": 18, "name": "Standard" }, { "id": 21, "name": "Anime" } ],
                  "rootFolders": [ { "id": 9, "path": "/series" }, { "id": 10, "path": "/series (anime)" } ]
                }
                """,
            ["/api/v1/tv/30984"] = """
                { "name": "Bleach", "keywords": [ { "id": 5, "name": "based on manga" }, { "id": 6, "name": "anime" } ] }
                """,
        });

        IResult result = await InvokeAsync(
            "GetEditOptionsAsync",
            [304, new StubSettingsService(Configured()), new SeerrClient(new HttpClient(handler)), CancellationToken.None]);

        SeerrRequestEditOptions options = Assert.IsType<Ok<SeerrRequestEditOptions>>(result).Value!;
        // The exact keyword anime selects the server anime defaults, like in Seerr's dialog.
        Assert.Equal(21, options.ProfileId);
        Assert.Equal("/series (anime)", options.RootFolder);
    }

    [Fact]
    public async Task EditOptions_KeepsPlainDefaults_ForAnimeInspiredSeries()
    {
        StubRoutingHandler handler = new(new Dictionary<string, string>
        {
            ["/api/v1/request/305"] = """
                { "id": 305, "type": 2, "serverId": 0, "media": { "tmdbId": 246 } }
                """,
            ["/api/v1/service/sonarr/0"] = """
                {
                  "server": {
                    "id": 0, "name": "Sonarr",
                    "activeDirectory": "/series", "activeProfileId": 18,
                    "activeAnimeDirectory": "/series (anime)", "activeAnimeProfileId": 21,
                    "isDefault": true
                  },
                  "profiles": [ { "id": 18, "name": "Standard" }, { "id": 21, "name": "Anime" } ],
                  "rootFolders": [ { "id": 9, "path": "/series" }, { "id": 10, "path": "/series (anime)" } ]
                }
                """,
            ["/api/v1/tv/246"] = """
                { "name": "Sample Show", "keywords": [ { "id": 7, "name": "anime inspired" } ] }
                """,
        });

        IResult result = await InvokeAsync(
            "GetEditOptionsAsync",
            [305, new StubSettingsService(Configured()), new SeerrClient(new HttpClient(handler)), CancellationToken.None]);

        SeerrRequestEditOptions options = Assert.IsType<Ok<SeerrRequestEditOptions>>(result).Value!;
        // Close keyword variants do not qualify, so the plain defaults stay preselected.
        Assert.Equal(18, options.ProfileId);
        Assert.Equal("/series", options.RootFolder);
    }

    [Fact]
    public async Task EditOptions_ReturnsNotFound_WhenTheRequestDetailIsMissing()
    {
        // The routing stub answers the unknown detail path with 404 "Request not found.".
        StubRoutingHandler handler = new(new Dictionary<string, string>());

        IResult result = await InvokeAsync(
            "GetEditOptionsAsync",
            [999, new StubSettingsService(Configured()), new SeerrClient(new HttpClient(handler)), CancellationToken.None]);

        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.ProblemDetails.Status);
    }

    [Fact]
    public async Task EditOptions_ReturnsServiceProblem_WhenSeerrIsNotConfigured()
    {
        StubRoutingHandler handler = new(new Dictionary<string, string>());

        IResult result = await InvokeAsync(
            "GetEditOptionsAsync",
            [12, new StubSettingsService(new AppSettings()), new SeerrClient(new HttpClient(handler)), CancellationToken.None]);

        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.ProblemDetails.Status);
        Assert.Equal(RequestEndpoints.NotConfiguredProblemCode, problem.ProblemDetails.Extensions["code"]);
        Assert.Empty(handler.Paths);
    }

    private static async Task<IResult> InvokeAsync(string handlerName, object?[] args)
    {
        Task<IResult> task = (Task<IResult>)typeof(RequestEndpoints)
            .GetMethod(handlerName, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, args)!;

        return await task;
    }

    private static AppSettings Configured()
    {
        return new AppSettings
        {
            Seerr = new SeerrInstanceSettings
            {
                Id = "seerr-1",
                BaseUrl = "http://seerr.local",
                ApiKey = "secret"
            }
        };
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string Body);

    private sealed class StubSettingsService(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);

        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) => Task.FromResult(value);
    }

    private sealed class StubRecordingHandler(string json, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath, body));

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
        }
    }

    private sealed class StubStatusSequenceHandler(params (string Json, HttpStatusCode StatusCode)[] responses) : HttpMessageHandler
    {
        private int index;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            (string json, HttpStatusCode statusCode) = responses[Math.Min(index, responses.Length - 1)];
            index++;

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }

    // Routes by exact path so one stub can answer the several lookups of one endpoint call.
    private sealed class StubRoutingHandler(IReadOnlyDictionary<string, string> jsonByPath) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);

            bool known = jsonByPath.TryGetValue(path, out string? json);

            return Task.FromResult(new HttpResponseMessage(known ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(json ?? "{\"message\":\"not found\"}", Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }
}
