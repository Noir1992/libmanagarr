using System.Net;
using System.Text;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Tests.Clients;

// The release page only learns about newer versions if the client asks the published repository
// route, so the request target is the part worth pinning down here.
public sealed class GitHubReleasesClientTests
{
    private const string ReleasesJson = """
        [ { "tag_name": "v1.4.0", "name": "Version 1.4.0", "body": "notes",
            "html_url": "https://github.com/Noir1992/libmanagarr/releases/tag/v1.4.0",
            "published_at": "2026-10-28T09:12:00Z", "prerelease": false, "draft": false } ]
        """;

    [Fact]
    public async Task FetchReleases_RequestsThePublishedRepository_OnTheGitHubHost()
    {
        RouteCapturingHandler handler = new(ReleasesJson);
        GitHubReleasesClient client = new(new HttpClient(handler));

        IReadOnlyList<GitHubReleaseResource> releases = await client.FetchReleasesAsync(CancellationToken.None);

        Assert.Single(releases);
        Assert.Equal("https://api.github.com/repos/Noir1992/libmanagarr/releases", handler.LastRequestUri?.GetLeftPart(UriPartial.Path));
        Assert.Equal("per_page=100", handler.LastRequestUri?.Query.TrimStart('?'));
    }

    private sealed class RouteCapturingHandler(string json) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }
}
