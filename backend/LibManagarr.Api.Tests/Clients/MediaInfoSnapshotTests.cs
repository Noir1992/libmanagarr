using System.Net;
using System.Text;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Tests.Clients;

public sealed class MediaInfoSnapshotTests
{
    [Theory]
    [InlineData(false, 12500000L)]
    [InlineData(true, 12500000L)]
    [InlineData(false, 0L)]
    [InlineData(true, 0L)]
    public async Task Sync_ShouldPersistBitrateAndDetailedDynamicRange(bool sonarr, long bitrate)
    {
        MediaFileSnapshot file = await Fetch(sonarr,
            $$"""{"videoBitrate":{{bitrate}},"videoDynamicRange":"HDR","videoDynamicRangeType":"DOVIWithHDR10","resolution":"3840x1600","videoCodec":"HEVC","audioCodec":"TrueHD"}""");
        Assert.Equal(bitrate > 0 ? bitrate : (long?)null, file.VideoBitrate);
        Assert.Equal("DOVIWithHDR10", file.VideoFormat);
        Assert.Equal("3840x1600", file.Resolution);
        Assert.Equal("HEVC", file.VideoCodec);
        Assert.Equal("TrueHD", file.AudioCodec);
        Assert.Equal("mkv", file.Container);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingMediaInfo_ShouldRemainUnknownInsteadOfSdr(bool sonarr)
    {
        MediaFileSnapshot file = await Fetch(sonarr, "null");
        Assert.Null(file.VideoBitrate);
        Assert.Null(file.VideoFormat);
        Assert.Null(file.Resolution);
        Assert.Equal("mkv", file.Container);
    }

    private static async Task<MediaFileSnapshot> Fetch(bool sonarr, string mediaInfo)
    {
        using HttpClient http = new(new ArrHandler(mediaInfo));
        ArrInstanceSettings instance = new() { Id = "test", BaseUrl = "http://arr.test", ApiKey = "test" };
        if (sonarr)
        {
            SonarrInstanceSnapshot snapshot = await new SonarrClient(http).FetchSnapshotAsync(instance, CancellationToken.None);
            return Assert.Single(Assert.Single(Assert.Single(snapshot.Series).Seasons).Episodes).File;
        }
        RadarrInstanceSnapshot movies = await new RadarrClient(http).FetchSnapshotAsync(instance, CancellationToken.None);
        return Assert.Single(movies.Movies).File;
    }

    private sealed class ArrHandler(string mediaInfo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.RequestUri!.AbsolutePath switch
            {
                "/api/v3/qualityprofile" or "/api/v3/tag" => "[]",
                "/api/v3/movie" => """[{"id":1,"hasFile":true,"movieFileId":10,"title":"Movie"}]""",
                "/api/v3/series" => """[{"id":1,"title":"Series"}]""",
                "/api/v3/episode" => """[{"id":1,"hasFile":true,"episodeFileId":10,"seasonNumber":1,"episodeNumber":1}]""",
                "/api/v3/moviefile" or "/api/v3/episodefile" =>
                    $$"""[{"id":10,"relativePath":"file.mkv","size":1000,"mediaInfo":{{mediaInfo}}}]""",
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
