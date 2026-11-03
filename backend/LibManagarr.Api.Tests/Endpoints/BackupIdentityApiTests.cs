using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LibManagarr.Api.Tests.Endpoints;

/// <summary>
/// Identity proof in front of the export route for the ordinary case of an installation without a
/// second factor: the login password alone is the whole proof, exactly as at a password change, and
/// only a wrong password is refused. A wrong one is still answered by the archive layer, so the
/// proof is read from which failure the request reached rather than from a written archive.
/// Uses the "auth-host" collection for the HTTP authentication integration tests.
/// </summary>
[Collection("auth-host")]
public sealed class BackupIdentityApiTests
{
    private const string Password = "correct-horse-battery-staple";

    [Fact]
    public async Task Export_WithoutTwoFactor_ProvesIdentityWithTheLoginPasswordAlone()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAsync(client);

        // Below the archive minimum, so the export service is the one answering: reaching it means
        // the identity proof passed without a code having been asked for.
        using HttpResponseMessage proved = await client.PostAsJsonAsync("/api/backups/export", new
        {
            backupPassword = "too-short",
            currentPassword = Password,
        });
        Assert.Equal(HttpStatusCode.BadRequest, proved.StatusCode);
        Assert.Equal("backup_weak_password", await GetErrorCodeAsync(proved));

        using HttpResponseMessage refused = await client.PostAsJsonAsync("/api/backups/export", new
        {
            backupPassword = "long-enough-backup-password",
            currentPassword = "not-the-account-password",
        });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("backup_identity_verification_failed", await GetErrorCodeAsync(refused));
    }

    private static async Task SetupAsync(HttpClient client)
    {
        await SetCsrfAsync(client);
        using HttpResponseMessage setup = await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "testuser", password = Password, confirmPassword = Password
        });
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);
        await SetCsrfAsync(client);
    }

    private static async Task SetCsrfAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/auth/csrf");
        string cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", cookie.Split(';')[0]["XSRF-TOKEN=".Length..]);
    }

    private static async Task<string?> GetErrorCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }
}
