using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LibManagarr.Api.Tests.Endpoints;

/// <summary>
/// Identity proof in front of the export route for the ordinary case of an installation without a
/// second factor: the login password alone is the whole proof, exactly as at a password change, and
/// only a wrong password is refused. A wrong one is still answered by the archive layer, so the
/// proof is read from which failure the request reached rather than from a written archive. The
/// proof owns the shared failure counter, so a wrong password is counted once per request and the
/// attempt that spends the budget is answered by the lockout itself.
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

    [Fact]
    public async Task Export_WithRepeatedWrongPasswords_CountsEachRequestOnceAndThenLocks()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAsync(client);

        for (int attempt = 1; attempt <= 4; attempt++)
        {
            using HttpResponseMessage refused = await client.PostAsJsonAsync("/api/backups/export", new
            {
                backupPassword = "long-enough-backup-password",
                currentPassword = "not-the-account-password",
            });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("backup_identity_verification_failed", await GetErrorCodeAsync(refused));
        }

        using HttpResponseMessage locked = await client.PostAsJsonAsync("/api/backups/export", new
        {
            backupPassword = "long-enough-backup-password",
            currentPassword = "not-the-account-password",
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal("account_locked", await GetErrorCodeAsync(locked));
        Assert.True(locked.Headers.Contains("Retry-After"));

        // The lock belongs to the account, not to the route, so signing in is refused while the
        // export waits - the guessing session cannot continue through another endpoint.
        using HttpResponseMessage signIn = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "testuser",
            password = Password,
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, signIn.StatusCode);
        Assert.Equal("account_locked", await GetErrorCodeAsync(signIn));
    }

    /// <summary>
    /// The two passwords of the export form stay apart in the answer: the refused identity proof names
    /// the credential this form asked for and joins the shared attempt warning, while the archive
    /// password is a matter of the archive layer alone and says nothing about the account.
    /// </summary>
    [Fact]
    public async Task Export_TwoPasswords_AnsweredAsTwoDifferentFailures()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAsync(client);

        // A correct identity proof in front of an archive password the archive layer refuses: the
        // answer carries no identity state at all, because nothing about the account failed.
        using (HttpResponseMessage archiveFailure = await client.PostAsJsonAsync("/api/backups/export", new
        {
            backupPassword = "too-short",
            currentPassword = Password,
        }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, archiveFailure.StatusCode);
            using JsonDocument body = JsonDocument.Parse(await archiveFailure.Content.ReadAsStringAsync());
            Assert.Equal("backup_weak_password", body.RootElement.GetProperty("code").GetString());
            Assert.False(body.RootElement.TryGetProperty("failureKind", out JsonElement _));
            Assert.False(body.RootElement.TryGetProperty("remainingAttempts", out JsonElement _));
        }

        // Of five shared attempts the last two are named, exactly as at a password change, and the
        // credential is the one this form showed: the account password inside the backup form.
        int?[] expectedRemaining = [null, null, 2, 1];
        foreach (int? expected in expectedRemaining)
        {
            using HttpResponseMessage refused = await client.PostAsJsonAsync("/api/backups/export", new
            {
                backupPassword = "long-enough-backup-password",
                currentPassword = "not-the-account-password",
            });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            using JsonDocument body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
            Assert.Equal("backup_identity_verification_failed", body.RootElement.GetProperty("code").GetString());
            Assert.Equal("backup_password", body.RootElement.GetProperty("failureKind").GetString());
            if (expected is null)
            {
                Assert.False(body.RootElement.TryGetProperty("remainingAttempts", out JsonElement _));
            }
            else
            {
                Assert.Equal(expected.Value, body.RootElement.GetProperty("remainingAttempts").GetInt32());
            }
        }
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
