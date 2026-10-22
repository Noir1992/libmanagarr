using Microsoft.Extensions.DependencyInjection;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace LibManagarr.Api.Tests;

/// <summary>
/// End-to-end tests for the step-1 auth pipeline over the real middleware stack: setup
/// singleton, login, absolute 30-day sessions, logout and password revocation, lockout,
/// rate limiting, antiforgery and the plain-JSON protection of every API answer.
/// Uses the "auth-host" collection for the HTTP authentication integration tests.
/// </summary>
[Collection("auth-host")]
public sealed class AuthApiTests
{
    private const string Username = "gollin";
    private const string ValidPassword = "correct-horse-battery-staple";
    private const string OtherPassword = "another-correct-horse";
    private const string AuthCookieName = "LibManagarr.Auth";

    [Fact]
    public async Task GetState_ShouldReportSetupRequired_WhenNoAccountExists()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        (bool setupRequired, bool authenticated) = await ReadStateAsync(client);

        Assert.True(setupRequired);
        Assert.False(authenticated);
    }

    [Fact]
    public async Task GetState_ShouldReportConfiguredLanguageOnceAnAccountExists()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using (HttpResponseMessage before = await client.GetAsync("/api/auth/state"))
        {
            using JsonDocument beforeDocument = JsonDocument.Parse(await before.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Null, beforeDocument.RootElement.GetProperty("language").ValueKind);
        }

        string token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage setup = await PostJsonAsync(client, "/api/auth/setup", token,
            new { username = Username, password = ValidPassword, confirmPassword = ValidPassword, language = "de" });
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);

        using HttpResponseMessage response = await client.GetAsync("/api/auth/state");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("De", document.RootElement.GetProperty("language").GetString());
    }

    [Fact]
    public async Task Setup_ShouldCreateAccountIssueCookieAndExposeAccount()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string token = await FetchCsrfTokenAsync(client);

        using HttpResponseMessage response = await PostJsonAsync(client, "/api/auth/setup", token, SetupBody());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        string setCookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            (value) => value.StartsWith(AuthCookieName + "=", StringComparison.Ordinal));
        Assert.Contains("HttpOnly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);

        (bool setupRequired, bool authenticated) = await ReadStateAsync(client);
        Assert.False(setupRequired);
        Assert.True(authenticated);

        using HttpResponseMessage account = await client.GetAsync("/api/account");
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await account.Content.ReadAsStringAsync());
        Assert.Equal(Username, document.RootElement.GetProperty("username").GetString());
        Assert.False(document.RootElement.GetProperty("twoFactorEnabled").GetBoolean());
    }

    [Fact]
    public async Task Setup_ShouldRejectSecondSetup_WithSetupNotAvailable()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAccountAsync(client);
        string token = await FetchCsrfTokenAsync(client);

        using HttpResponseMessage response = await PostJsonAsync(
            client,
            "/api/auth/setup",
            token,
            new { username = "other-user", password = OtherPassword, confirmPassword = OtherPassword });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("setup_not_available", await GetErrorCodeAsync(response));
    }

    [Fact]
    public async Task Setup_ShouldReturnFieldValidationErrors()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string token = await FetchCsrfTokenAsync(client);

        using HttpResponseMessage response = await PostJsonAsync(
            client,
            "/api/auth/setup",
            token,
            new { username = "ab", password = "short", confirmPassword = "different" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_failed", document.RootElement.GetProperty("code").GetString());
        JsonElement errors = document.RootElement.GetProperty("errors");
        Assert.Equal("username_length", errors.GetProperty("username")[0].GetString());
        Assert.Equal("password_length", errors.GetProperty("password")[0].GetString());
        Assert.Equal("password_confirmation_mismatch", errors.GetProperty("confirmPassword")[0].GetString());
    }

    [Fact]
    public async Task Setup_ShouldAdmitExactlyOneWinner_WhenCalledConcurrently()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string token = await FetchCsrfTokenAsync(client);

        Task<HttpResponseMessage> firstTask = PostJsonAsync(
            client, "/api/auth/setup", token, new { username = "alice-one", password = ValidPassword, confirmPassword = ValidPassword });
        Task<HttpResponseMessage> secondTask = PostJsonAsync(
            client, "/api/auth/setup", token, new { username = "bob-two", password = OtherPassword, confirmPassword = OtherPassword });

        using HttpResponseMessage first = await firstTask;
        using HttpResponseMessage second = await secondTask;

        HttpStatusCode[] statuses = [first.StatusCode, second.StatusCode];
        Array.Sort(statuses);
        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], statuses);
    }

    [Fact]
    public async Task Login_ShouldIssueSessionAndSatisfyProtectedApi()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient setupClient = factory.CreateClient();
        await SetupAccountAsync(setupClient);

        using HttpClient client = factory.CreateClient();
        string token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage login = await PostJsonAsync(
            client, "/api/auth/login", token, new { username = Username, password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("requiresTwoFactor").GetBoolean());

        using HttpResponseMessage account = await client.GetAsync("/api/account");
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
    }

    [Fact]
    public async Task Login_WithRemember_ShouldIssuePersistentCookie()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient setupClient = factory.CreateClient();
        await SetupAccountAsync(setupClient);

        using HttpClient client = factory.CreateClient();
        string token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage login = await PostJsonAsync(
            client, "/api/auth/login", token, new { username = Username, password = ValidPassword, remember = true });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        string setCookie = Assert.Single(
            login.Headers.GetValues("Set-Cookie"),
            (value) => value.StartsWith(AuthCookieName + "=", StringComparison.Ordinal));
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WithoutRemember_ShouldIssueBrowserSessionCookie()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient setupClient = factory.CreateClient();
        await SetupAccountAsync(setupClient);

        using HttpClient client = factory.CreateClient();
        string token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage login = await PostJsonAsync(
            client, "/api/auth/login", token, new { username = Username, password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        string setCookie = Assert.Single(
            login.Headers.GetValues("Set-Cookie"),
            (value) => value.StartsWith(AuthCookieName + "=", StringComparison.Ordinal));
        Assert.DoesNotContain("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_ShouldAnswerIdentically_ForUnknownUserAndWrongPassword()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient setupClient = factory.CreateClient();
        await SetupAccountAsync(setupClient);

        using HttpClient client = factory.CreateClient();
        string token = await FetchCsrfTokenAsync(client);

        using HttpResponseMessage unknownUser = await PostJsonAsync(
            client, "/api/auth/login", token, new { username = "who-is-this", password = ValidPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal("invalid_credentials", await GetErrorCodeAsync(unknownUser));

        using HttpResponseMessage wrongPassword = await PostJsonAsync(
            client, "/api/auth/login", token, new { username = Username, password = "definitely-wrong-pass" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal("invalid_credentials", await GetErrorCodeAsync(wrongPassword));
    }

    [Fact]
    public async Task Login_ShouldLockAccountForFiveMinutes_ThenUnlock()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAccountAsync(client);

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            using HttpResponseMessage wrong = await PostJsonAsync(
                client, "/api/auth/login", await FetchCsrfTokenAsync(client), new { username = Username, password = "definitely-wrong-pass" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        using HttpResponseMessage locked = await PostJsonAsync(
            client, "/api/auth/login", await FetchCsrfTokenAsync(client), new { username = Username, password = ValidPassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal("account_locked", await GetErrorCodeAsync(locked));
        Assert.True(locked.Headers.Contains("Retry-After"));

        factory.FakeTime.Advance(TimeSpan.FromMinutes(5));

        using HttpResponseMessage unlocked = await PostJsonAsync(
            client, "/api/auth/login", await FetchCsrfTokenAsync(client), new { username = Username, password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
    }

    [Fact]
    public async Task Logout_ShouldRevokeSessionServerSide()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage setup = await PostJsonAsync(client, "/api/auth/setup", token, SetupBody());
        string? authCookie = GetCookieValue(setup, AuthCookieName);
        Assert.False(string.IsNullOrEmpty(authCookie));

        using HttpClient cookieless = factory.CreateCookielessClient();
        Assert.Equal(HttpStatusCode.OK, await GetAccountStatusWithCookieAsync(cookieless, authCookie!));

        string authenticatedToken = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage logout = await PostJsonAsync(client, "/api/auth/logout", authenticatedToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetAccountStatusWithCookieAsync(cookieless, authCookie!));
    }

    [Fact]
    public async Task ChangePassword_ShouldRevokeEverySession()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient clientA = factory.CreateClient();
        string tokenA = await FetchCsrfTokenAsync(clientA);
        using HttpResponseMessage setup = await PostJsonAsync(clientA, "/api/auth/setup", tokenA, SetupBody());
        string? cookieA = GetCookieValue(setup, AuthCookieName);

        using HttpClient clientB = factory.CreateClient();
        string tokenB = await FetchCsrfTokenAsync(clientB);
        using HttpResponseMessage loginB = await PostJsonAsync(
            clientB, "/api/auth/login", tokenB, new { username = Username, password = ValidPassword });
        string? cookieB = GetCookieValue(loginB, AuthCookieName);
        Assert.False(string.IsNullOrEmpty(cookieA));
        Assert.False(string.IsNullOrEmpty(cookieB));

        string authenticatedTokenA = await FetchCsrfTokenAsync(clientA);
        using HttpResponseMessage change = await PostJsonAsync(
            clientA, "/api/account/password", authenticatedTokenA, new { currentPassword = ValidPassword, newPassword = OtherPassword, confirmPassword = OtherPassword });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        using HttpClient cookieless = factory.CreateCookielessClient();
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAccountStatusWithCookieAsync(cookieless, cookieA!));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAccountStatusWithCookieAsync(cookieless, cookieB!));

        string anonymousTokenA = await FetchCsrfTokenAsync(clientA);
        using HttpResponseMessage reLogin = await PostJsonAsync(
            clientA, "/api/auth/login", anonymousTokenA, new { username = Username, password = OtherPassword });
        Assert.Equal(HttpStatusCode.OK, reLogin.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ShouldAnswerBusinessError_ForWrongCurrentPassword()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAccountAsync(client);
        string token = await FetchCsrfTokenAsync(client);

        using HttpResponseMessage response = await PostJsonAsync(
            client,
            "/api/account/password",
            token,
            new { currentPassword = "wrong-current-password", newPassword = OtherPassword, confirmPassword = OtherPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("current_password_incorrect", await GetErrorCodeAsync(response));
    }

    [Fact]
    public async Task ProtectedEndpoints_ShouldReturnProblemJson_WithoutSession()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage account = await client.GetAsync("/api/account");
        Assert.Equal(HttpStatusCode.Unauthorized, account.StatusCode);
        Assert.Equal("application/problem+json", account.Content.Headers.ContentType?.MediaType);
        Assert.Equal("authentication_required", await GetErrorCodeAsync(account));

        using HttpResponseMessage settings = await client.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, settings.StatusCode);
        Assert.Equal("application/problem+json", settings.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task MutableRequests_ShouldRequireAntiforgeryToken()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAccountAsync(client);

        using HttpResponseMessage response = await client.PostAsync(
            "/api/account/password",
            new StringContent(
                JsonSerializer.Serialize(new { currentPassword = ValidPassword, newPassword = OtherPassword, confirmPassword = OtherPassword }),
                Encoding.UTF8,
                "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request_verification_token", await GetErrorCodeAsync(response));
    }

    [Fact]
    public async Task Login_ShouldRateLimitPerSourceIp()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string token = await FetchCsrfTokenAsync(client);

        for (int attempt = 1; attempt <= 10; attempt++)
        {
            using HttpResponseMessage response = await PostJsonAsync(
                client, "/api/auth/login", token, new { username = Username, password = "definitely-wrong-pass" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using HttpResponseMessage limited = await PostJsonAsync(
            client, "/api/auth/login", token, new { username = Username, password = "definitely-wrong-pass" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("too_many_requests", await GetErrorCodeAsync(limited));
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task UnknownApiPath_ShouldReturnProblem404()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetState_ShouldFailLoudly_WhenAuthStorageCorrupt()
    {
        using AuthTestAppFactory factory = new();
        await File.WriteAllTextAsync(Path.Combine(factory.ConfigDirectory, "auth.db"), "this-is-not-a-sqlite-database");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/auth/state");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("auth_storage_unavailable", await GetErrorCodeAsync(response));
    }

    [Fact]
    public async Task Session_ShouldExpireAbsently_AfterThirtyDays()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAccountAsync(client);

        using (HttpResponseMessage nearExpiry = await client.GetAsync("/api/account"))
        {
            Assert.Equal(HttpStatusCode.OK, nearExpiry.StatusCode);
        }

        factory.FakeTime.Advance(TimeSpan.FromDays(29));
        using (HttpResponseMessage stillValid = await client.GetAsync("/api/account"))
        {
            Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
        }

        factory.FakeTime.Advance(TimeSpan.FromDays(2));
        using (HttpResponseMessage expired = await client.GetAsync("/api/account"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        }
    }

    [Fact]
    public async Task ExpiredSession_WithPreviouslyValidCsrfToken_ShouldReturn401ForLibraryPost()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAccountAsync(client);
        string token = await FetchCsrfTokenAsync(client);
        factory.FakeTime.Advance(TimeSpan.FromDays(31));
        using HttpResponseMessage response = await PostJsonAsync(client, "/api/sonarr/instances/library", token, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("authentication_required", await GetErrorCodeAsync(response));
    }

    [Fact]
    public async Task PasswordChange_ShouldLockOutRepeatedWrongCurrentPasswords()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAccountAsync(client);
        string token = await FetchCsrfTokenAsync(client);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage rejected = await PostJsonAsync(client, "/api/account/password", token,
                new { currentPassword = "wrong", newPassword = OtherPassword, confirmPassword = OtherPassword });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        using HttpResponseMessage limited = await PostJsonAsync(client, "/api/account/password", token,
            new { currentPassword = ValidPassword, newPassword = OtherPassword, confirmPassword = OtherPassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("account_locked", await GetErrorCodeAsync(limited));
    }

    [Fact]
    public async Task PasswordChange_ShouldDeleteAuthenticationCookie()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAccountAsync(client);
        string token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage response = await PostJsonAsync(client, "/api/account/password", token,
            new { currentPassword = ValidPassword, newPassword = OtherPassword, confirmPassword = OtherPassword });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("LibManagarr.Auth=;", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/api/settings/general")]
    [InlineData("/api/settings/instances")]
    [InlineData("/api/logs")]
    [InlineData("/api/account")]
    public async Task ProtectedResponses_ShouldPreventCaching(string path)
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAccountAsync(client);
        using HttpResponseMessage response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("de", "De")]
    [InlineData("fr", "Fr")]
    [InlineData("en", "En")]
    [InlineData(null, "En")]
    public async Task Setup_ShouldPersistSelectedLanguageAndPreserveTheme(string? language, string expected)
    {
        using AuthTestAppFactory factory = new();
        ISettingsService settings = factory.Services.GetRequiredService<ISettingsService>();
        AppSettings original = await settings.GetAsync(default);
        original.Theme = ThemeMode.Dark;
        await settings.SaveAsync(original, default);
        using HttpClient client = factory.CreateClient();
        string token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage setup = await PostJsonAsync(client, "/api/auth/setup", token,
            new { username = Username, password = ValidPassword, confirmPassword = ValidPassword, language });
        Assert.Equal(HttpStatusCode.Created, setup.StatusCode);
        using JsonDocument general = JsonDocument.Parse(await (await client.GetAsync("/api/settings/general")).Content.ReadAsStringAsync());
        Assert.Equal(expected, general.RootElement.GetProperty("language").GetString());
        Assert.Equal("Dark", general.RootElement.GetProperty("theme").GetString());
        string authenticatedToken = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage repeated = await PostJsonAsync(client, "/api/auth/setup", authenticatedToken,
            new { username = Username, password = ValidPassword, confirmPassword = ValidPassword, language = "fr" });
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Equal(expected, (await settings.GetAsync(default)).Language.ToString());
    }

    [Fact]
    public async Task Setup_ShouldRejectInvalidLanguageBeforeCreatingAccount()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        string token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage setup = await PostJsonAsync(client, "/api/auth/setup", token,
            new { username = Username, password = ValidPassword, confirmPassword = ValidPassword, language = 99 });
        Assert.Equal(HttpStatusCode.BadRequest, setup.StatusCode);
        Assert.True((await ReadStateAsync(client)).SetupRequired);
    }

    private static object SetupBody() => new { username = Username, password = ValidPassword, confirmPassword = ValidPassword };

    private static async Task SetupAccountAsync(HttpClient client)
    {
        string token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage response = await PostJsonAsync(client, "/api/auth/setup", token, SetupBody());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<(bool SetupRequired, bool Authenticated)> ReadStateAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/auth/state");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (
            document.RootElement.GetProperty("setupRequired").GetBoolean(),
            document.RootElement.GetProperty("authenticated").GetBoolean());
    }

    private static async Task<string> FetchCsrfTokenAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        string? token = GetCookieValue(response, "XSRF-TOKEN");
        Assert.False(string.IsNullOrEmpty(token));
        return token!;
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(
        HttpClient client,
        string url,
        string? csrfToken,
        object? body = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, url);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        if (csrfToken is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", csrfToken);
        }

        return await client.SendAsync(request);
    }

    private static async Task<string?> GetErrorCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }

    private static string? GetCookieValue(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values))
        {
            return null;
        }

        foreach (string value in values)
        {
            string nameValue = value.Split(';')[0];
            if (nameValue.StartsWith(name + "=", StringComparison.Ordinal))
            {
                return nameValue[(name.Length + 1)..];
            }
        }

        return null;
    }

    private static async Task<HttpStatusCode> GetAccountStatusWithCookieAsync(HttpClient cookielessClient, string authCookieValue)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/account");
        request.Headers.Add("Cookie", AuthCookieName + "=" + authCookieValue);
        using HttpResponseMessage response = await cookielessClient.SendAsync(request);
        return response.StatusCode;
    }
}
