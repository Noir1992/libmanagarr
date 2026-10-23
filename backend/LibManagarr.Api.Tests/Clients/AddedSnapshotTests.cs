using System.Net;
using System.Text;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Tests.Clients;

public sealed class AddedSnapshotTests
{
    [Theory]
    [InlineData(false, "2026-01-10T15:30:00Z")]
    [InlineData(true, "2026-01-10T15:30:00Z")]
    [InlineData(false, null)]
    [InlineData(true, null)]
    public async Task FetchSnapshot_ShouldKeepEntityAddedDatesForMissingMedia(bool sonarr, string? added)
    {
        using HttpClient http = new(new ArrHandler(added));
        ArrInstanceSettings instance = new() { Id = "test", BaseUrl = "http://arr.test", ApiKey = "test" };
        DateTimeOffset? actual;
        if (sonarr)
        {
            SonarrInstanceSnapshot snapshot = await new SonarrClient(http).FetchSnapshotAsync(instance, CancellationToken.None);
            SonarrSeriesSnapshot series = Assert.Single(snapshot.Series);
            Assert.False(Assert.Single(Assert.Single(series.Seasons).Episodes).HasFile);
            actual = series.AddedAt;
        }
        else
        {
            RadarrInstanceSnapshot snapshot = await new RadarrClient(http).FetchSnapshotAsync(instance, CancellationToken.None);
            RadarrMovieSnapshot movie = Assert.Single(snapshot.Movies);
            Assert.False(movie.HasFile);
            actual = movie.AddedAt;
        }
        Assert.Equal(added is null ? (DateTimeOffset?)null : DateTimeOffset.Parse(added), actual);
    }

    private sealed class ArrHandler(string? added) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string addedProperty = added is null ? "" : $",\"added\":\"{added}\"";
            string body = request.RequestUri!.AbsolutePath switch
            {
                "/api/v3/qualityprofile" or "/api/v3/tag" or "/api/v3/episodefile" => "[]",
                "/api/v3/movie" or "/api/v3/series" => "[{\"id\":1,\"hasFile\":false,\"title\":\"Media\"" + addedProperty + "}]",
                "/api/v3/episode" => """[{"id":1,"hasFile":false,"seasonNumber":1,"episodeNumber":1}]""",
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
