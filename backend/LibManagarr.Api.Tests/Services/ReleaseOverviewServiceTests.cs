using System.Net;
using System.Text;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibManagarr.Api.Tests.Services;

public sealed class ReleaseOverviewServiceTests
{
    private const string ReleaseJson = """
        [
          {
            "tag_name": "v1.4.0",
            "name": "Version 1.4.0",
            "body": "- Release-Kalender ergaenzt",
            "html_url": "https://github.com/Noir1992/libmanagarr/releases/tag/v1.4.0",
            "published_at": "2026-10-28T09:12:00Z",
            "prerelease": false,
            "draft": false
          },
          {
            "tag_name": "v1.2.3",
            "name": null,
            "body": "",
            "html_url": "https://github.com/Noir1992/libmanagarr/releases/tag/v1.2.3",
            "published_at": "2026-09-14T18:00:00Z",
            "prerelease": false,
            "draft": false
          },
          {
            "tag_name": "v0.9.0",
            "name": "Draft release",
            "body": "not published yet",
            "html_url": "https://github.com/Noir1992/libmanagarr/releases/tag/v0.9.0",
            "published_at": "2026-08-01T00:00:00Z",
            "prerelease": false,
            "draft": true
          }
        ]
        """;

    [Fact]
    public async Task GetOverview_MarksTheRunningVersionAndFlagsNewerStableReleases()
    {
        FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 2, 14, 3, 0, TimeSpan.Zero));
        ReleaseOverviewService service = CreateService(time, new StubHandler(ReleaseJson), currentVersion: "1.2.3");

        ReleaseOverview overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(ReleaseOverviewStatus.Ok, overview.Status);
        Assert.Equal("1.2.3", overview.CurrentVersion);
        Assert.True(overview.UpdateAvailable);
        Assert.Equal(time.GetUtcNow(), overview.CheckedAt);
        // The draft release never reaches the response.
        Assert.Equal(2, overview.Versions.Count);
        Assert.Equal(["1.4.0", "1.2.3"], overview.Versions.Select(version => version.Version));

        ReleaseVersion newer = overview.Versions[0];
        Assert.False(newer.IsCurrent);
        Assert.True(newer.IsNewerThanCurrent);
        Assert.Equal("Version 1.4.0", newer.Name);
        Assert.Equal("v1.4.0", newer.Tag);
        Assert.Equal("- Release-Kalender ergaenzt", newer.Notes);
        Assert.Equal("ghcr.io/noir1992/libmanagarr:1.4.0", newer.PullReference);

        ReleaseVersion running = overview.Versions[1];
        Assert.True(running.IsCurrent);
        Assert.False(running.IsNewerThanCurrent);
        // A release without a name falls back to its version so the list is never blank.
        Assert.Equal("1.2.3", running.Name);
        Assert.Null(running.Notes);
    }

    [Fact]
    public async Task GetOverview_KeepsTheHintOffWhenTheRunningVersionIsTheNewestRelease()
    {
        FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 2, 14, 3, 0, TimeSpan.Zero));
        ReleaseOverviewService service = CreateService(time, new StubHandler(ReleaseJson), currentVersion: "v1.4.0");

        ReleaseOverview overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.False(overview.UpdateAvailable);
        Assert.True(overview.Versions[0].IsCurrent);
    }

    [Fact]
    public async Task GetOverview_KeepsTheHintOffForPrereleasesAndBuildsWithoutAReleaseNumber()
    {
        FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 2, 14, 3, 0, TimeSpan.Zero));
        const string PrereleaseJson = """
            [
              {
                "tag_name": "v1.5.0-rc1",
                "name": "Release candidate",
                "body": null,
                "html_url": "https://github.com/Noir1992/libmanagarr/releases/tag/v1.5.0-rc1",
                "published_at": "2026-10-30T09:12:00Z",
                "prerelease": true,
                "draft": false
              }
            ]
            """;

        ReleaseOverview prerelease = await CreateService(time, new StubHandler(PrereleaseJson), currentVersion: "1.3.0")
            .GetOverviewAsync(CancellationToken.None);
        Assert.True(prerelease.Versions[0].IsPrerelease);
        Assert.False(prerelease.Versions[0].IsNewerThanCurrent);
        Assert.False(prerelease.UpdateAvailable);

        // An edge identifier carries no release number, so nothing can be newer than it.
        ReleaseOverview edge = await CreateService(time, new StubHandler(ReleaseJson), currentVersion: "edge-sha-a03cf0d")
            .GetOverviewAsync(CancellationToken.None);
        Assert.Equal("edge-sha-a03cf0d", edge.CurrentVersion);
        Assert.False(edge.UpdateAvailable);
        Assert.DoesNotContain(edge.Versions, version => version.IsCurrent);
    }

    [Fact]
    public async Task GetOverview_KeepsTheDevelopmentVersionOutOfTheComparison()
    {
        FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 2, 14, 3, 0, TimeSpan.Zero));

        // A development run reports the SDK default, which matches no published release. Naming it
        // on the scale would make every release newer and light the navigation hint forever.
        ReleaseOverview overview = await CreateService(time, new StubHandler(ReleaseJson), currentVersion: "1.0.0")
            .GetOverviewAsync(CancellationToken.None);

        Assert.Equal(ReleaseOverviewStatus.Ok, overview.Status);
        Assert.Equal("1.0.0", overview.CurrentVersion);
        Assert.False(overview.UpdateAvailable);
        Assert.DoesNotContain(overview.Versions, version => version.IsCurrent);
        Assert.DoesNotContain(overview.Versions, version => version.IsNewerThanCurrent);
    }

    [Fact]
    public async Task GetOverview_ServesTheCachedResultUntilTheLifetimeElapses()
    {
        FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 2, 14, 3, 0, TimeSpan.Zero));
        StubHandler handler = new(ReleaseJson);
        ReleaseOverviewService service = CreateService(time, handler, currentVersion: "1.2.3");

        await service.GetOverviewAsync(CancellationToken.None);
        ReleaseOverview second = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(2, second.Versions.Count);

        time.Advance(ReleaseOverviewCache.Lifetime + TimeSpan.FromMinutes(1));
        await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetOverview_WithRefreshBypassesTheFreshCache()
    {
        FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 2, 14, 3, 0, TimeSpan.Zero));
        StubHandler handler = new(ReleaseJson);
        ReleaseOverviewService service = CreateService(time, handler, currentVersion: "1.2.3");

        await service.GetOverviewAsync(CancellationToken.None);
        await service.GetOverviewAsync(refresh: true, CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetOverview_ReportsUnreachableWithAnEmptyListWhenGitHubCannotBeReached()
    {
        FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 2, 14, 3, 0, TimeSpan.Zero));
        ReleaseOverviewService service = CreateService(
            time,
            new StubHandler(new HttpRequestException("no route to host")),
            currentVersion: "1.2.3");

        ReleaseOverview overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(ReleaseOverviewStatus.Unreachable, overview.Status);
        Assert.Empty(overview.Versions);
        Assert.False(overview.UpdateAvailable);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ReleaseOverviewStatus.RateLimited)]
    [InlineData(HttpStatusCode.TooManyRequests, ReleaseOverviewStatus.RateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, ReleaseOverviewStatus.Rejected)]
    [InlineData(HttpStatusCode.InternalServerError, ReleaseOverviewStatus.Rejected)]
    public async Task GetOverview_MapsUpstreamStatusCodesToAStatusInBody(HttpStatusCode statusCode, ReleaseOverviewStatus expected)
    {
        FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 2, 14, 3, 0, TimeSpan.Zero));
        ReleaseOverviewService service = CreateService(time, new StubHandler(statusCode), currentVersion: "1.2.3");

        ReleaseOverview overview = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(expected, overview.Status);
        Assert.False(overview.UpdateAvailable);
    }

    [Fact]
    public async Task GetOverview_KeepsTheLastSuccessfulListWhenAFollowingAttemptIsRateLimited()
    {
        FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 2, 14, 3, 0, TimeSpan.Zero));
        StubHandler handler = new(ReleaseJson);
        ReleaseOverviewService service = CreateService(time, handler, currentVersion: "1.2.3");
        DateTimeOffset firstSuccess = time.GetUtcNow();

        ReleaseOverview succeeded = await service.GetOverviewAsync(CancellationToken.None);
        handler.NextStatusCode = HttpStatusCode.Forbidden;
        time.Advance(ReleaseOverviewCache.Lifetime + TimeSpan.FromMinutes(1));
        ReleaseOverview rateLimited = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(ReleaseOverviewStatus.RateLimited, rateLimited.Status);
        Assert.Equal(succeeded.Versions.Select(version => version.Version), rateLimited.Versions.Select(version => version.Version));
        // The timestamp keeps reporting the last successful check, not the failed attempt.
        Assert.Equal(firstSuccess, rateLimited.CheckedAt);
        Assert.True(rateLimited.UpdateAvailable);
    }

    private static ReleaseOverviewService CreateService(
        FakeTimeProvider time,
        HttpMessageHandler handler,
        string currentVersion)
    {
        GitHubReleasesClient client = new(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") });

        return new ReleaseOverviewService(
            client,
            new ReleaseOverviewCache(time),
            time,
            NullLogger<ReleaseOverviewService>.Instance)
        {
            CurrentVersion = currentVersion
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage>? responder;

        public StubHandler(string json)
        {
            Json = json;
        }

        public StubHandler(HttpStatusCode statusCode)
        {
            NextStatusCode = statusCode;
        }

        public StubHandler(HttpRequestException failure)
        {
            responder = () => throw failure;
        }

        public string? Json { get; }

        public HttpStatusCode? NextStatusCode { get; set; }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (responder is not null)
            {
                return Task.FromResult(responder());
            }

            if (NextStatusCode is HttpStatusCode failureStatus)
            {
                return Task.FromResult(new HttpResponseMessage(failureStatus) { RequestMessage = request });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Json!, Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }
}
