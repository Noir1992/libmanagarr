using System.Net;
using System.Text;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibManagarr.Api.Tests.Services;

// The releaseCheck task is the single path that asks GitHub; these tests pin that it stores what
// it finds and that a failed check leaves the last good snapshot readable.
public sealed class ReleaseCheckTaskHandlerTests
{
    private const string ReleaseJson = """
        [
          {
            "tag_name": "v1.4.0",
            "name": "Version 1.4.0",
            "body": "- notes",
            "html_url": "https://github.com/Noir1992/libmanagarr/releases/tag/v1.4.0",
            "published_at": "2026-10-28T09:12:00Z",
            "prerelease": false,
            "draft": false
          }
        ]
        """;

    [Fact]
    public async Task Execute_StoresWhatUpstreamPublishedSoPageReadsServeIt()
    {
        FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 2, 14, 3, 0, TimeSpan.Zero));
        StubHandler upstream = new(ReleaseJson);
        ReleaseOverviewService service = CreateService(time, upstream);
        ReleaseCheckTaskHandler handler = new(service);

        await handler.ExecuteAsync(CancellationToken.None);

        ReleaseOverview read = await service.GetOverviewAsync(CancellationToken.None);
        Assert.Equal(1, upstream.RequestCount);
        Assert.Equal(ReleaseOverviewStatus.Ok, read.Status);
        Assert.Equal("1.4.0", Assert.Single(read.Versions).Version);
    }

    [Fact]
    public async Task Execute_KeepsTheLastGoodSnapshotReadableWhenUpstreamFails()
    {
        FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 2, 14, 3, 0, TimeSpan.Zero));
        StubHandler upstream = new(ReleaseJson);
        ReleaseOverviewService service = CreateService(time, upstream);
        ReleaseCheckTaskHandler handler = new(service);
        await handler.ExecuteAsync(CancellationToken.None);

        upstream.NextFailure = new HttpRequestException("no route to host");
        await handler.ExecuteAsync(CancellationToken.None);

        ReleaseOverview read = await service.GetOverviewAsync(CancellationToken.None);
        Assert.Equal(ReleaseOverviewStatus.Unreachable, read.Status);
        Assert.Equal(["1.4.0"], read.Versions.Select(version => version.Version));
    }

    [Fact]
    public void TheBuiltInDefinitionIsReadOnlyWithManualRunAndFiresAtStartup()
    {
        ServiceCollection services = new();
        ScheduledTaskCatalog.AddBuiltInScheduledTasks(services);
        using ServiceProvider provider = services.BuildServiceProvider();
        ScheduledTaskCatalog catalog = new(provider.GetServices<ScheduledTaskDefinition>());

        ScheduledTaskDefinition definition = Assert.Single(catalog.Definitions, candidate => candidate.TaskKey == "releaseCheck");
        Assert.True(definition.ReadOnly);
        Assert.True(definition.CanRunManually);
        Assert.True(definition.RunAtStartup);

        ScheduledTaskSettings defaults = definition.CreateDefaults();
        Assert.True(defaults.Enabled);
        Assert.Equal(ScheduleMode.Interval, defaults.ScheduleMode);
        Assert.Equal(ScheduleIntervalUnit.Hour, defaults.IntervalUnit);
        Assert.Equal(12, defaults.IntervalValue);
    }

    private static ReleaseOverviewService CreateService(FakeTimeProvider time, HttpMessageHandler handler)
    {
        GitHubReleasesClient client = new(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") });

        return new ReleaseOverviewService(
            client,
            new ReleaseOverviewCache(),
            time,
            NullLogger<ReleaseOverviewService>.Instance)
        {
            CurrentVersion = "1.2.3"
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public StubHandler(string json)
        {
            Json = json;
        }

        public string? Json { get; }

        public Exception? NextFailure { get; set; }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (NextFailure is Exception failure)
            {
                throw failure;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Json!, Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }
}
