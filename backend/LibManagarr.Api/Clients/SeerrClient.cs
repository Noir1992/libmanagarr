using System.Net.Http.Headers;
using System.Text.Json;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Clients;

public sealed class SeerrClient(HttpClient httpClient)
{
    public async Task TestConnectionAsync(
        SeerrInstanceSettings instance,
        CancellationToken cancellationToken)
    {
        _ = await GetAsync<JsonElement>(instance, "/api/v1/auth/me", cancellationToken);
    }

    private async Task<T?> GetAsync<T>(
        SeerrInstanceSettings instance,
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"{NormalizeBaseUrl(instance.BaseUrl)}{relativeUrl}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Api-Key", instance.ApiKey);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        return baseUrl.Trim().TrimEnd('/');
    }
}
