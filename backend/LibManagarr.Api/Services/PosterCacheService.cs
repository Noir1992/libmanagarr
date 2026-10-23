using System.Text.RegularExpressions;

namespace LibManagarr.Api.Services;

public sealed record PosterImage(byte[] Bytes, string ContentType);

public interface IPosterCacheService
{
    Task<PosterImage?> GetOrDownloadPosterAsync(string cacheKey, string? remoteUrl, int width, CancellationToken ct);
}

public sealed partial class PosterCacheService : IPosterCacheService
{
    // TMDB only serves fixed size steps; a request maps up to the next step (there is no 250 step).
    private static readonly (int Width, string Step)[] TmdbSteps =
    [
        (92, "w92"),
        (154, "w154"),
        (185, "w185"),
        (342, "w342"),
        (500, "w500"),
    ];

    private readonly string _cacheDir;
    private readonly HttpClient _http;

    public PosterCacheService(HttpClient httpClient, IWebHostEnvironment env)
    {
        _http = httpClient;
        string dataDir = Path.Combine(env.ContentRootPath, "data");
        _cacheDir = Path.Combine(dataDir, "poster-cache");
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<PosterImage?> GetOrDownloadPosterAsync(string cacheKey, string? remoteUrl, int width, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(remoteUrl))
            return null;

        string step = ResolveStep(width);
        string downloadUrl = TmdbStepSegment().Replace(remoteUrl, $"/t/p/{step}/", count: 1);
        string extension = ResolveExtension(remoteUrl);
        string cacheFile = Path.Combine(_cacheDir, $"{cacheKey}_{step}{extension}");

        if (File.Exists(cacheFile))
        {
            return new PosterImage(await File.ReadAllBytesAsync(cacheFile, ct), ContentTypeFor(extension));
        }

        HttpResponseMessage? response = null;
        try
        {
            response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(ct);
            await File.WriteAllBytesAsync(cacheFile, bytes, ct);
            return new PosterImage(bytes, ContentTypeFor(extension));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static string ResolveStep(int width)
    {
        foreach ((int stepWidth, string step) in TmdbSteps)
        {
            if (width <= stepWidth)
                return step;
        }
        return "original";
    }

    private static string ResolveExtension(string remoteUrl)
    {
        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out Uri? uri))
        {
            string extension = Path.GetExtension(uri.AbsolutePath);
            if (extension is ".jpg" or ".jpeg" or ".png" or ".webp")
                return extension;
        }
        return ".jpg";
    }

    private static string ContentTypeFor(string extension) => extension switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };

    [GeneratedRegex("/t/p/[^/]+/")]
    private static partial Regex TmdbStepSegment();
}
