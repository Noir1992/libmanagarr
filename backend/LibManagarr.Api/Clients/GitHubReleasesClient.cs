using System.Net.Http.Headers;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Clients;

// Reads the published releases of the project repository, the authoritative source for the
// version list and the change notes shown on the releases page.
public sealed class GitHubReleasesClient(HttpClient httpClient)
{
    public const string RepositoryOwner = "Noir1992";
    public const string RepositoryName = "libmanagarr";

    // One page is enough: the repository has far fewer published releases than this limit.
    private const int MaximumResults = 100;

    private static readonly Uri ApiBaseAddress = new("https://api.github.com/");

    public async Task<IReadOnlyList<GitHubReleaseResource>> FetchReleasesAsync(CancellationToken cancellationToken)
    {
        Uri requestUri = new(ApiBaseAddress, $"repos/{RepositoryOwner}/{RepositoryName}/releases?per_page={MaximumResults}");

        return await GetAsync<List<GitHubReleaseResource>>(requestUri, cancellationToken) ?? [];
    }

    private async Task<T?> GetAsync<T>(Uri requestUri, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        // GitHub rejects requests that do not identify themselves, so both headers are mandatory.
        request.Headers.Add("User-Agent", "libmanagarr");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }
}
