using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class PosterCacheServiceTests
{
    private static readonly byte[] PosterBytes = [0xFF, 0xD8, 0xFF, 0xD9];

    [Fact]
    public async Task GetOrDownloadPosterAsync_ShouldReturnNull_ForNullRemoteUrl()
    {
        PosterCacheService service = CreateService();

        PosterImage? result = await service.GetOrDownloadPosterAsync("test_key", null, 342, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOrDownloadPosterAsync_ShouldReturnNull_ForEmptyRemoteUrl()
    {
        PosterCacheService service = CreateService();

        PosterImage? result = await service.GetOrDownloadPosterAsync("test_key", "", 342, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOrDownloadPosterAsync_ShouldReturnNull_ForNonSuccessStatusCode()
    {
        PosterCacheService service = CreateService(new StubHttpMessageHandler(HttpStatusCode.NotFound));

        PosterImage? result = await service.GetOrDownloadPosterAsync("test_key", "http://example.com/poster.jpg", 342, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOrDownloadPosterAsync_ShouldReturnNull_ForHttpDownloadError()
    {
        PosterCacheService service = CreateService(new ErrorHttpMessageHandler());

        PosterImage? result = await service.GetOrDownloadPosterAsync("test_key", "http://example.com/fail.jpg", 342, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOrDownloadPosterAsync_ShouldReturnCachedFile_OnSecondCall()
    {
        int callCount = 0;
        StubHttpMessageHandler handler = new(new Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>((request, ct) =>
        {
            callCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new System.Net.Http.ByteArrayContent(PosterBytes),
                RequestMessage = request
            });
        }));

        PosterCacheService service = CreateService(handler);

        PosterImage? firstCall = await service.GetOrDownloadPosterAsync("cache_test", "http://example.com/poster.jpg", 342, CancellationToken.None);
        PosterImage? secondCall = await service.GetOrDownloadPosterAsync("cache_test", "http://example.com/poster.jpg", 342, CancellationToken.None);

        Assert.NotNull(firstCall);
        Assert.NotNull(secondCall);
        Assert.Equal(firstCall!.Bytes, secondCall!.Bytes);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetOrDownloadPosterAsync_ShouldDownloadBytesUnchanged_AndReportJpegContentType()
    {
        StubHttpMessageHandler handler = CreateOkHandler();

        PosterCacheService service = CreateService(handler);

        PosterImage? result = await service.GetOrDownloadPosterAsync("jpg_test", "http://example.com/poster.jpg", 342, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(PosterBytes, result!.Bytes);
        Assert.Equal("image/jpeg", result.ContentType);
    }

    [Fact]
    public async Task GetOrDownloadPosterAsync_ShouldReportPngContentType_ForPngUrl()
    {
        StubHttpMessageHandler handler = CreateOkHandler();

        PosterCacheService service = CreateService(handler);

        PosterImage? result = await service.GetOrDownloadPosterAsync("png_test", "http://example.com/poster.png", 342, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("image/png", result!.ContentType);
    }

    [Fact]
    public async Task GetOrDownloadPosterAsync_ShouldRewriteTmdbSizeStep_ToNextStepForRequestedWidth()
    {
        List<string> requestedUrls = [];
        StubHttpMessageHandler handler = CreateCapturingHandler(requestedUrls);

        PosterCacheService service = CreateService(handler);

        await service.GetOrDownloadPosterAsync("tmdb_test", "https://image.tmdb.org/t/p/original/abc123.jpg", 250, CancellationToken.None);
        await service.GetOrDownloadPosterAsync("tmdb_test2", "https://image.tmdb.org/t/p/w500/abc123.jpg", 342, CancellationToken.None);
        await service.GetOrDownloadPosterAsync("tmdb_test3", "https://image.tmdb.org/t/p/w92/abc123.jpg", 600, CancellationToken.None);

        Assert.Equal("https://image.tmdb.org/t/p/w342/abc123.jpg", requestedUrls[0]);
        Assert.Equal("https://image.tmdb.org/t/p/w342/abc123.jpg", requestedUrls[1]);
        Assert.Equal("https://image.tmdb.org/t/p/original/abc123.jpg", requestedUrls[2]);
    }

    [Fact]
    public async Task GetOrDownloadPosterAsync_ShouldKeepNonTmdbUrlUnchanged()
    {
        List<string> requestedUrls = [];
        StubHttpMessageHandler handler = CreateCapturingHandler(requestedUrls);

        PosterCacheService service = CreateService(handler);

        await service.GetOrDownloadPosterAsync("plain_test", "http://example.com/images/poster.jpg", 342, CancellationToken.None);

        Assert.Equal("http://example.com/images/poster.jpg", requestedUrls[0]);
    }

    private static StubHttpMessageHandler CreateOkHandler()
    {
        return new StubHttpMessageHandler(new Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>((request, ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new System.Net.Http.ByteArrayContent(PosterBytes),
                RequestMessage = request
            })));
    }

    private static StubHttpMessageHandler CreateCapturingHandler(List<string> requestedUrls)
    {
        return new StubHttpMessageHandler(new Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>((request, ct) =>
        {
            requestedUrls.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new System.Net.Http.ByteArrayContent(PosterBytes),
                RequestMessage = request
            });
        }));
    }

    private static PosterCacheService CreateService(HttpMessageHandler? handler = null)
    {
        string contentRoot = Path.Combine(Path.GetTempPath(), $"poster-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(contentRoot);
        string emptyDir = Path.Combine(Path.GetTempPath(), $"empty-{Guid.NewGuid()}");
        Directory.CreateDirectory(emptyDir);
        IWebHostEnvironment env = new HostingEnvironment
        {
            ContentRootPath = contentRoot,
            ContentRootFileProvider = new PhysicalFileProvider(emptyDir),
            EnvironmentName = "Test",
        };
        HttpClient client = handler != null ? new HttpClient(handler) : new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK));
        return new PosterCacheService(client, env);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _factory;

        public StubHttpMessageHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> factory)
        {
            _factory = factory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_factory != null)
                return _factory(request, cancellationToken);

            HttpResponseMessage response = new(_statusCode)
            {
                Content = new System.Net.Http.StringContent(""),
                RequestMessage = request
            };

            return Task.FromResult(response);
        }
    }

    private sealed class ErrorHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Simulated network error");
        }
    }

    private sealed class HostingEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Test";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Test";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = string.Empty;
    }
}
