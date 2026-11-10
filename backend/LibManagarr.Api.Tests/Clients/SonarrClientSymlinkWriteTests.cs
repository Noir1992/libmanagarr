using System.Net;
using System.Text;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Tests.Clients;

// The writes a managed link performs on Sonarr. Two rules carry these tests: a command is addressed to
// exactly the resource the link owns — never the whole series — and a write sends Sonarr's own document
// back with one field changed, because a partial object would let the instance blank every field this
// client does not model.
public sealed class SonarrClientSymlinkWriteTests
{
    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Delete_RemovesOnlyTheSelectedFileAndAcceptsAnAlreadyMissingRecord(HttpStatusCode status)
    {
        StubArrHandler handler = new([], status);
        SonarrClient client = new(new HttpClient(handler));
        await client.DeleteEpisodeFileAsync(CreateSonarr(), 44, CancellationToken.None);
        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, sent.Method);
        Assert.Equal("/api/v3/episodefile/44", sent.Path);
        Assert.Equal("secret", sent.ApiKey);
        Assert.Empty(sent.Body);
    }

    [Fact]
    public async Task Rescan_QueuesOneCommandForTheSeriesAlone()
    {
        StubArrHandler handler = new(new Dictionary<string, string>
        {
            ["POST /api/v3/command"] = """
                { "id": 91, "name": "RescanSeries", "status": "queued", "body": { "seriesId": 12 } }
                """
        });
        SonarrClient client = new(new HttpClient(handler));

        SonarrCommandResource? command = await client.StartRescanSeriesAsync(CreateSonarr(), 12, CancellationToken.None);

        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("/api/v3/command", sent.Path);
        Assert.Equal("secret", sent.ApiKey);
        Assert.Contains("\"name\":\"RescanSeries\"", sent.Body);
        Assert.Contains("\"seriesId\":12", sent.Body);
        // A rescan that carries a file list is a rename, and a rename would touch other episodes.
        Assert.DoesNotContain("fileIds", sent.Body);

        Assert.NotNull(command);
        Assert.Equal(91, command.Id);
        Assert.Equal("queued", command.Status);
        Assert.False(command.IsFinished);
        // The body is what recognises a recorded command later, so it has to survive the read.
        Assert.Equal(12, command.Body!["seriesId"].GetInt32());
    }

    [Fact]
    public async Task Rename_QueuesExactlyTheOneFileTheLinkOwns()
    {
        StubArrHandler handler = new(new Dictionary<string, string>
        {
            ["POST /api/v3/command"] = """{ "id": 92, "name": "RenameFiles", "status": "queued" }"""
        });
        SonarrClient client = new(new HttpClient(handler));

        await client.StartRenameFilesAsync(CreateSonarr(), 12, [44], CancellationToken.None);

        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal("/api/v3/command", sent.Path);
        Assert.Contains("\"name\":\"RenameFiles\"", sent.Body);
        Assert.Contains("\"seriesId\":12", sent.Body);
        Assert.Contains("\"files\":[44]", sent.Body);
    }

    [Fact]
    public async Task Command_ReadsTheQueuedCommandBackFromItsOwnRoute()
    {
        StubArrHandler handler = new(new Dictionary<string, string>
        {
            ["GET /api/v3/command/91"] = """{ "id": 91, "name": "RescanSeries", "status": "completed" }"""
        });
        SonarrClient client = new(new HttpClient(handler));

        SonarrCommandResource? command = await client.FetchCommandAsync(CreateSonarr(), 91, CancellationToken.None);

        RecordedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal("/api/v3/command/91", sent.Path);
        Assert.NotNull(command);
        Assert.True(command.IsFinished);
        Assert.True(command.Completed);
    }

    [Theory]
    [InlineData("queued", false, false)]
    [InlineData("started", false, false)]
    [InlineData("cancelling", false, false)]
    [InlineData("completed", true, true)]
    [InlineData("failed", true, false)]
    public void CommandState_SeparatesWaitingFromFinishedAndFinishedFromFailed(
        string status,
        bool isFinished,
        bool completed)
    {
        // Waiting and finished are different waits, and a failed scan must not be reported as done just
        // because the instance stopped working on it.
        SonarrCommandResource command = new() { Status = status };

        Assert.Equal(isFinished, command.IsFinished);
        Assert.Equal(completed, command.Completed);
    }

    [Fact]
    public async Task Monitoring_ChangesOneFieldOnSonarrsOwnEpisodeDocument()
    {
        StubArrHandler handler = new(new Dictionary<string, string>
        {
            ["GET /api/v3/episode/5"] = """
                {
                  "id": 5,
                  "seriesId": 12,
                  "seasonNumber": 0,
                  "episodeNumber": 1,
                  "title": "The Battle of Unato",
                  "monitored": true,
                  "titleSlug": "kabaneri-of-the-iron-fortress",
                  "runtime": 132
                }
                """,
            ["PUT /api/v3/episode"] = """
                { "id": 5, "seriesId": 12, "seasonNumber": 0, "episodeNumber": 1, "monitored": false }
                """
        });
        SonarrClient client = new(new HttpClient(handler));

        SonarrEpisodeResource? episode = await client.SetEpisodeMonitoredAsync(
            CreateSonarr(), 5, false, CancellationToken.None);

        Assert.Collection(
            handler.Requests,
            read =>
            {
                Assert.Equal(HttpMethod.Get, read.Method);
                Assert.Equal("/api/v3/episode/5", read.Path);
                Assert.Equal("", read.Body);
            },
            write =>
            {
                Assert.Equal(HttpMethod.Put, write.Method);
                Assert.Equal("/api/v3/episode", write.Path);
                Assert.Equal("secret", write.ApiKey);
                Assert.Contains("\"monitored\":false", write.Body);
                Assert.DoesNotContain("\"monitored\":true", write.Body);
                // Unmodelled and untouched fields go back exactly as Sonarr reported them.
                Assert.Contains("\"titleSlug\":\"kabaneri-of-the-iron-fortress\"", write.Body);
                Assert.Contains("\"runtime\":132", write.Body);
                Assert.Contains("\"seasonNumber\":0", write.Body);
            });

        Assert.False(episode?.Monitored);
    }

    [Fact]
    public async Task EpisodeFileFields_SetTheReleaseGroup_WithoutTouchingQualityOrPath()
    {
        StubArrHandler handler = new(new Dictionary<string, string>
        {
            ["GET /api/v3/episodefile/44"] = """
                {
                  "id": 44,
                  "seriesId": 12,
                  "releaseGroup": null,
                  "quality": { "quality": { "id": 3, "name": "WEBDL-1080p", "source": "web", "resolution": 1080 } },
                  "size": 5368709120,
                  "path": "/data/series/Kabaneri/S00E05.mkv",
                  "originalFileName": "kabaneri.s00e05.mkv"
                }
                """,
            ["PUT /api/v3/episodefile"] = """
                { "id": 44, "releaseGroup": "WFFF", "path": "/data/series/Kabaneri/S00E05.mkv" }
                """
        });
        SonarrClient client = new(new HttpClient(handler));

        SonarrEpisodeFileResource? episodeFile = await client.SetEpisodeFileFieldsAsync(
            CreateSonarr(),
            44,
            new Dictionary<string, object?> { ["releaseGroup"] = "WFFF" },
            CancellationToken.None);

        Assert.Collection(
            handler.Requests,
            read => Assert.Equal("/api/v3/episodefile/44", read.Path),
            write =>
            {
                Assert.Equal(HttpMethod.Put, write.Method);
                Assert.Equal("/api/v3/episodefile", write.Path);
                Assert.Contains("\"releaseGroup\":\"WFFF\"", write.Body);
                Assert.Contains("\"name\":\"WEBDL-1080p\"", write.Body);
                Assert.Contains("\"size\":5368709120", write.Body);
                Assert.Contains("\"originalFileName\":\"kabaneri.s00e05.mkv\"", write.Body);
                // Nothing beyond the named field is invented on the way out.
                Assert.DoesNotContain("sceneName", write.Body);
                Assert.DoesNotContain("languages", write.Body);
            });

        Assert.Equal("/data/series/Kabaneri/S00E05.mkv", episodeFile?.Path);
    }

    [Fact]
    public async Task Monitoring_StopsWithTheStatusSonarrAnswered_WhenTheEpisodeIsNotThere()
    {
        // The endpoint maps this to a conflict the user can read; a silent "done" would hide that the
        // episode id was wrong.
        SonarrClient client = new(new HttpClient(new StubArrHandler(new Dictionary<string, string>(), HttpStatusCode.NotFound)));

        HttpRequestException failure = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SetEpisodeMonitoredAsync(CreateSonarr(), 5, false, CancellationToken.None));

        Assert.Equal(HttpStatusCode.NotFound, failure.StatusCode);
    }

    [Fact]
    public async Task Monitoring_Fails_WhenSonarrAnswersWithoutADocument()
    {
        StubArrHandler handler = new(new Dictionary<string, string> { ["GET /api/v3/episode/5"] = string.Empty });
        SonarrClient client = new(new HttpClient(handler));

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SetEpisodeMonitoredAsync(CreateSonarr(), 5, false, CancellationToken.None));

        Assert.Contains("5", failure.Message);
        // Without a document there is nothing to write back, so no PUT may have happened.
        RecordedRequest read = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, read.Method);
    }

    [Fact]
    public async Task Metadata_MapsNamesToDestinationIdsAndUsesBulkWithoutBlankingOtherFields()
    {
        StubArrHandler handler = new(new Dictionary<string, string>
        {
            ["GET /api/v3/episodefile/44"] = """{"id":44,"quality":{"quality":{"id":1,"name":"HDTV-720p"},"revision":{"version":2}},"languages":[{"id":1,"name":"English"}],"customFormatScore":123}""",
            ["GET /api/v3/qualitydefinition"] = """[{"quality":{"id":941,"name":"WEBDL-1080p","source":"web","resolution":1080}}]""",
            ["GET /api/v3/language"] = """[{"id":47,"name":"German"},{"id":48,"name":"Japanese"}]""",
            ["PUT /api/v3/episodefile/bulk"] = "[]"
        });
        await new SonarrClient(new HttpClient(handler)).SetMappedEpisodeFileMetadataAsync(CreateSonarr(), 44,
            new SymlinkFileMetadata { Quality = "WEBDL-1080p", Languages = ["German", "Japanese"], ReleaseGroup = "NEW" }, CancellationToken.None);
        RecordedRequest sent = Assert.Single(handler.Requests, value => value.Method == HttpMethod.Put);
        Assert.Equal("/api/v3/episodefile/bulk", sent.Path);
        using System.Text.Json.JsonDocument body = System.Text.Json.JsonDocument.Parse(sent.Body);
        System.Text.Json.JsonElement file = body.RootElement[0];
        Assert.Equal(941, file.GetProperty("quality").GetProperty("quality").GetProperty("id").GetInt32());
        Assert.Equal(2, file.GetProperty("quality").GetProperty("revision").GetProperty("version").GetInt32());
        Assert.Equal([47, 48], file.GetProperty("languages").EnumerateArray().Select(value => value.GetProperty("id").GetInt32()));
        Assert.Equal(123, file.GetProperty("customFormatScore").GetInt32());
        Assert.Equal("NEW", file.GetProperty("releaseGroup").GetString());
    }

    [Fact]
    public async Task Metadata_UnknownDestinationQualityDoesNotWrite()
    {
        StubArrHandler handler = new(new Dictionary<string, string>
        {
            ["GET /api/v3/episodefile/44"] = """{"id":44}""",
            ["GET /api/v3/qualitydefinition"] = "[]"
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SonarrClient(new HttpClient(handler))
            .SetMappedEpisodeFileMetadataAsync(CreateSonarr(), 44, new SymlinkFileMetadata { Quality = "Unmapped" }, CancellationToken.None));
        Assert.DoesNotContain(handler.Requests, value => value.Method == HttpMethod.Put);
    }

    private static ArrInstanceSettings CreateSonarr()
    {
        return new ArrInstanceSettings
        {
            Id = "sonarr-1",
            Name = "Sonarr",
            BaseUrl = "http://sonarr.local/",
            ApiKey = "secret"
        };
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string ApiKey, string Body);

    private sealed class StubArrHandler(
        Dictionary<string, string> jsonByRoute,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
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
            string path = request.RequestUri!.AbsolutePath;
            Requests.Add(new RecordedRequest(request.Method, path, request.RequestUri.Query, apiKey, body));

            string json = jsonByRoute.TryGetValue($"{request.Method} {path}", out string? found)
                ? found
                : "{}";

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
        }
    }
}
