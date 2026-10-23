using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LibManagarr.Api.Tests;

/// <summary>
/// End-to-end tests for step 2 over the real middleware stack: setup confirm, login challenges,
/// recovery codes, replay rejection, revocation and the second-factor proof at the password
/// change. TOTP codes are computed independently from RFC 6238 primitives (HMAC-SHA1 over the
/// Base32-decoded shared key) so the suite cross-checks the Otp.NET wiring instead of mirroring
/// it. The fake clock is aligned to fresh step boundaries to keep every code deterministic.
/// Uses the "auth-host" collection for the HTTP authentication integration tests.
/// </summary>
[Collection("auth-host")]
public sealed class TwoFactorApiTests
{
    private const string Username = "gollin";
    private const string ValidPassword = "correct-horse-battery-staple";
    private const string OtherPassword = "another-correct-horse";
    private const string AuthCookieName = "LibManagarr.Auth";
    private const string ChallengeCookieName = "LibManagarr.TfaChallenge";

    [Fact]
    public async Task Account_ShouldReportTwoFactorDisabled_AndNoRecoveryCodes()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAccountAsync(client);

        JsonDocument document = await ReadJsonAsync(await client.GetAsync("/api/account"));
        Assert.False(document.RootElement.GetProperty("twoFactorEnabled").GetBoolean());
        Assert.Equal(0, document.RootElement.GetProperty("recoveryCodesRemaining").GetInt32());
    }

    [Fact]
    public async Task TwoFactorSetup_ShouldStartWithoutCurrentPassword()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAccountAsync(client);
        AdvanceToStepBoundary(factory);
        string token = await FetchCsrfTokenAsync(client);

        using HttpResponseMessage response = await PostJsonAsync(client, "/api/account/two-factor/setup", token, new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrEmpty(document.RootElement.GetProperty("sharedKey").GetString()));
    }

    [Fact]
    public async Task TwoFactorSetupAndConfirm_ShouldEnableAndIssueRecoveryCodesOnce()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAccountAsync(client);
        AdvanceToStepBoundary(factory);
        string token = await FetchCsrfTokenAsync(client);

        using HttpResponseMessage start = await PostJsonAsync(
            client, "/api/account/two-factor/setup", token, new { });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        string sharedKey;
        using (JsonDocument document = await ReadJsonAsync(start))
        {
            sharedKey = document.RootElement.GetProperty("sharedKey").GetString()!;
            Assert.Contains("otpauth://", document.RootElement.GetProperty("authenticatorUri").GetString(), StringComparison.Ordinal);
            Assert.Contains("LibManagarr", document.RootElement.GetProperty("authenticatorUri").GetString(), StringComparison.Ordinal);
            Assert.Contains("<svg", document.RootElement.GetProperty("qrCodeSvg").GetString(), StringComparison.Ordinal);
        }

        token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage wrongConfirm = await PostJsonAsync(client, "/api/account/two-factor/setup/confirm", token, new { code = "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongConfirm.StatusCode);
        Assert.Equal("invalid_two_factor_code", await GetErrorCodeAsync(wrongConfirm));

        token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage confirm = await PostJsonAsync(
            client, "/api/account/two-factor/setup/confirm", token, new { code = ComputeTotpCode(sharedKey, factory.FakeTime.Current) });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        using (JsonDocument document = await ReadJsonAsync(confirm))
        {
            Assert.Equal(10, document.RootElement.GetProperty("recoveryCodes").GetArrayLength());
        }

        using JsonDocument account = await ReadJsonAsync(await client.GetAsync("/api/account"));
        Assert.True(account.RootElement.GetProperty("twoFactorEnabled").GetBoolean());
        Assert.Equal(10, account.RootElement.GetProperty("recoveryCodesRemaining").GetInt32());

        token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage secondConfirm = await PostJsonAsync(client, "/api/account/two-factor/setup/confirm", token, new { code = "123456" });
        Assert.Equal(HttpStatusCode.Conflict, secondConfirm.StatusCode);
        Assert.Equal("two_factor_already_enabled", await GetErrorCodeAsync(secondConfirm));
    }

    [Fact]
    public async Task TwoFactorConfirm_WithWrongCode_ShouldLeaveTwoFactorDisabled()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAccountAsync(client);
        AdvanceToStepBoundary(factory);
        string token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage start = await PostJsonAsync(
            client, "/api/account/two-factor/setup", token, new { });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage confirm = await PostJsonAsync(client, "/api/account/two-factor/setup/confirm", token, new { code = "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);

        using JsonDocument account = await ReadJsonAsync(await client.GetAsync("/api/account"));
        Assert.False(account.RootElement.GetProperty("twoFactorEnabled").GetBoolean());
    }

    [Fact]
    public async Task TwoFactorConfirm_FromAnotherSession_ShouldFail()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient setupClient = factory.CreateClient();
        await SetupAccountAsync(setupClient);
        AdvanceToStepBoundary(factory);
        string startToken = await FetchCsrfTokenAsync(setupClient);
        using HttpResponseMessage start = await PostJsonAsync(
            setupClient, "/api/account/two-factor/setup", startToken, new { });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        using HttpClient otherClient = factory.CreateClient();
        string loginToken = await FetchCsrfTokenAsync(otherClient);
        using HttpResponseMessage login = await PostJsonAsync(
            otherClient, "/api/auth/login", loginToken, new { username = Username, password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        string confirmToken = await FetchCsrfTokenAsync(otherClient);
        using HttpResponseMessage confirm = await PostJsonAsync(otherClient, "/api/account/two-factor/setup/confirm", confirmToken, new { code = "123456" });
        Assert.Equal(HttpStatusCode.Gone, confirm.StatusCode);
        Assert.Equal("two_factor_setup_invalid", await GetErrorCodeAsync(confirm));
    }

    [Fact]
    public async Task TwoFactorConfirm_AfterSetupExpiry_ShouldFail()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await SetupAccountAsync(client);
        AdvanceToStepBoundary(factory);
        string token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage start = await PostJsonAsync(
            client, "/api/account/two-factor/setup", token, new { });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        factory.FakeTime.Advance(TimeSpan.FromMinutes(11));

        token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage confirm = await PostJsonAsync(client, "/api/account/two-factor/setup/confirm", token, new { code = "123456" });
        Assert.Equal(HttpStatusCode.Gone, confirm.StatusCode);
        Assert.Equal("two_factor_setup_invalid", await GetErrorCodeAsync(confirm));
    }

    [Fact]
    public async Task Login_WithTwoFactorEnabled_ShouldIssueChallengeNotSession()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await EnableTwoFactorAsync(factory, client);

        using HttpClient loginClient = factory.CreateClient();
        string token = await FetchCsrfTokenAsync(loginClient);
        using HttpResponseMessage login = await PostJsonAsync(
            loginClient, "/api/auth/login", token, new { username = Username, password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using (JsonDocument document = await ReadJsonAsync(login))
        {
            Assert.True(document.RootElement.GetProperty("requiresTwoFactor").GetBoolean());
        }

        string setCookie = Assert.Single(
            login.Headers.GetValues("Set-Cookie"),
            (value) => value.StartsWith(ChallengeCookieName + "=", StringComparison.Ordinal));
        Assert.Contains("HttpOnly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            login.Headers.GetValues("Set-Cookie"),
            (value) => value.StartsWith(AuthCookieName + "=", StringComparison.Ordinal));

        using HttpResponseMessage account = await loginClient.GetAsync("/api/account");
        Assert.Equal(HttpStatusCode.Unauthorized, account.StatusCode);

        using JsonDocument state = await ReadJsonAsync(await loginClient.GetAsync("/api/auth/two-factor/state"));
        Assert.True(state.RootElement.GetProperty("pending").GetBoolean());
    }

    [Fact]
    public async Task TwoFactorVerify_WithoutChallenge_ShouldReturn410()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await EnableTwoFactorAsync(factory, client);

        using HttpClient freshClient = factory.CreateClient();
        string token = await FetchCsrfTokenAsync(freshClient);
        using HttpResponseMessage response = await PostJsonAsync(freshClient, "/api/auth/two-factor/verify", token, new { code = "123456" });

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("two_factor_challenge_invalid", await GetErrorCodeAsync(response));
    }

    [Fact]
    public async Task TwoFactorVerify_WithTotpCode_ShouldIssueFullSession()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        (string sharedKey, _) = await EnableTwoFactorAsync(factory, client);

        using HttpClient loginClient = factory.CreateClient();
        await LoginWithChallengeAsync(loginClient);
        string code = ComputeTotpCode(sharedKey, factory.FakeTime.Current);
        string verifyToken = await FetchCsrfTokenAsync(loginClient);
        using HttpResponseMessage verify = await PostJsonAsync(loginClient, "/api/auth/two-factor/verify", verifyToken, new { code });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        using (JsonDocument document = await ReadJsonAsync(verify))
        {
            Assert.False(document.RootElement.GetProperty("requiresTwoFactor").GetBoolean());
        }

        Assert.Equal(HttpStatusCode.OK, (await loginClient.GetAsync("/api/account")).StatusCode);

        using JsonDocument state = await ReadJsonAsync(await loginClient.GetAsync("/api/auth/two-factor/state"));
        Assert.False(state.RootElement.GetProperty("pending").GetBoolean());
    }

    [Fact]
    public async Task TwoFactorVerify_WithRemember_ShouldIssuePersistentCookie()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        (string sharedKey, _) = await EnableTwoFactorAsync(factory, client);

        using HttpClient loginClient = factory.CreateClient();
        await LoginWithChallengeAsync(loginClient);
        string code = ComputeTotpCode(sharedKey, factory.FakeTime.Current);
        string verifyToken = await FetchCsrfTokenAsync(loginClient);
        using HttpResponseMessage verify = await PostJsonAsync(loginClient, "/api/auth/two-factor/verify", verifyToken, new { code, remember = true });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        string setCookie = Assert.Single(
            verify.Headers.GetValues("Set-Cookie"),
            (value) => value.StartsWith(AuthCookieName + "=", StringComparison.Ordinal));
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TwoFactorVerify_SameStepReplay_ShouldBeRejected()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        (string sharedKey, _) = await EnableTwoFactorAsync(factory, client);

        using HttpClient loginClient = factory.CreateClient();
        await LoginWithChallengeAsync(loginClient);
        string code = ComputeTotpCode(sharedKey, factory.FakeTime.Current);
        string firstToken = await FetchCsrfTokenAsync(loginClient);
        using (HttpResponseMessage first = await PostJsonAsync(loginClient, "/api/auth/two-factor/verify", firstToken, new { code }))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        await LoginWithChallengeAsync(loginClient);
        string secondToken = await FetchCsrfTokenAsync(loginClient);
        using HttpResponseMessage replay = await PostJsonAsync(loginClient, "/api/auth/two-factor/verify", secondToken, new { code });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal("invalid_two_factor_code", await GetErrorCodeAsync(replay));
    }

    [Fact]
    public async Task TwoFactorVerify_AfterChallengeExpiry_ShouldReturn410()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        (string sharedKey, _) = await EnableTwoFactorAsync(factory, client);

        using HttpClient loginClient = factory.CreateClient();
        await LoginWithChallengeAsync(loginClient);
        factory.FakeTime.Advance(TimeSpan.FromMinutes(6));

        string token = await FetchCsrfTokenAsync(loginClient);
        using HttpResponseMessage verify = await PostJsonAsync(
            loginClient,
            "/api/auth/two-factor/verify",
            token,
            new { code = ComputeTotpCode(sharedKey, factory.FakeTime.Current) });
        Assert.Equal(HttpStatusCode.Gone, verify.StatusCode);
        Assert.Equal("two_factor_challenge_invalid", await GetErrorCodeAsync(verify));
    }

    [Fact]
    public async Task TwoFactorVerify_FiveWrongCodes_ShouldDropChallengeAndLockAccount()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        (string sharedKey, _) = await EnableTwoFactorAsync(factory, client);

        using HttpClient loginClient = factory.CreateClient();
        await LoginWithChallengeAsync(loginClient);

        HttpStatusCode[] statuses = [];
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            string token = await FetchCsrfTokenAsync(loginClient);
            using HttpResponseMessage wrong = await PostJsonAsync(loginClient, "/api/auth/two-factor/verify", token, new { code = "000000" });
            statuses = [.. statuses, wrong.StatusCode];
        }

        Assert.Equal(
            [
                HttpStatusCode.BadRequest,
                HttpStatusCode.BadRequest,
                HttpStatusCode.BadRequest,
                HttpStatusCode.BadRequest,
                HttpStatusCode.TooManyRequests
            ],
            statuses);

        string loginToken = await FetchCsrfTokenAsync(loginClient);
        using HttpResponseMessage lockedLogin = await PostJsonAsync(
            loginClient, "/api/auth/login", loginToken, new { username = Username, password = ValidPassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, lockedLogin.StatusCode);
        Assert.Equal("account_locked", await GetErrorCodeAsync(lockedLogin));
    }

    [Fact]
    public async Task TwoFactorVerify_RecoveryCode_ShouldWorkOnceAndDecreaseRemaining()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        (_, IReadOnlyList<string> recoveryCodes) = await EnableTwoFactorAsync(factory, client);

        using HttpClient loginClient = factory.CreateClient();
        await LoginWithChallengeAsync(loginClient);
        string firstToken = await FetchCsrfTokenAsync(loginClient);
        using (HttpResponseMessage verify = await PostJsonAsync(
            loginClient,
            "/api/auth/two-factor/verify",
            firstToken,
            new { code = recoveryCodes[0], useRecoveryCode = true }))
        {
            Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        }

        using (JsonDocument account = await ReadJsonAsync(await loginClient.GetAsync("/api/account")))
        {
            Assert.Equal(9, account.RootElement.GetProperty("recoveryCodesRemaining").GetInt32());
        }

        await LoginWithChallengeAsync(loginClient);
        string secondToken = await FetchCsrfTokenAsync(loginClient);
        using HttpResponseMessage reuse = await PostJsonAsync(
            loginClient,
            "/api/auth/two-factor/verify",
            secondToken,
            new { code = recoveryCodes[0], useRecoveryCode = true });
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        Assert.Equal("invalid_two_factor_code", await GetErrorCodeAsync(reuse));
    }

    [Fact]
    public async Task PasswordChange_WrongPassword_ShouldNotConsumeRecoveryCode()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        (_, IReadOnlyList<string> recoveryCodes) = await EnableTwoFactorAsync(factory, client);
        string token = await FetchCsrfTokenAsync(client);

        using HttpResponseMessage change = await PostJsonAsync(
            client,
            "/api/account/password",
            token,
            new
            {
                currentPassword = "wrong-current-password",
                newPassword = OtherPassword,
                confirmPassword = OtherPassword,
                code = recoveryCodes[0],
                useRecoveryCode = true
            });
        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        Assert.Equal("current_password_incorrect", await GetErrorCodeAsync(change));

        using JsonDocument account = await ReadJsonAsync(await client.GetAsync("/api/account"));
        Assert.Equal(10, account.RootElement.GetProperty("recoveryCodesRemaining").GetInt32());
    }

    [Fact]
    public async Task PasswordChange_WithTwoFactor_RequiresProof()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await EnableTwoFactorAsync(factory, client);
        string token = await FetchCsrfTokenAsync(client);

        using HttpResponseMessage change = await PostJsonAsync(
            client,
            "/api/account/password",
            token,
            new { currentPassword = ValidPassword, newPassword = OtherPassword, confirmPassword = OtherPassword });
        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        Assert.Equal("second_factor_required", await GetErrorCodeAsync(change));
    }

    [Fact]
    public async Task PasswordChange_WithRecoveryProof_ShouldChangeAndConsumeCode()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        (string sharedKey, IReadOnlyList<string> recoveryCodes) = await EnableTwoFactorAsync(factory, client);
        string token = await FetchCsrfTokenAsync(client);

        using HttpResponseMessage change = await PostJsonAsync(
            client,
            "/api/account/password",
            token,
            new
            {
                currentPassword = ValidPassword,
                newPassword = OtherPassword,
                confirmPassword = OtherPassword,
                code = recoveryCodes[0],
                useRecoveryCode = true
            });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        using (HttpResponseMessage oldSession = await client.GetAsync("/api/account"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
        }

        using HttpClient loginClient = factory.CreateClient();
        string loginToken = await FetchCsrfTokenAsync(loginClient);
        using HttpResponseMessage login = await PostJsonAsync(
            loginClient, "/api/auth/login", loginToken, new { username = Username, password = OtherPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using (JsonDocument document = await ReadJsonAsync(login))
        {
            Assert.True(document.RootElement.GetProperty("requiresTwoFactor").GetBoolean());
        }

        AdvanceToStepBoundary(factory);
        string verifyToken = await FetchCsrfTokenAsync(loginClient);
        using HttpResponseMessage verify = await PostJsonAsync(
            loginClient,
            "/api/auth/two-factor/verify",
            verifyToken,
            new { code = ComputeTotpCode(sharedKey, factory.FakeTime.Current) });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        using JsonDocument account = await ReadJsonAsync(await loginClient.GetAsync("/api/account"));
        Assert.Equal(9, account.RootElement.GetProperty("recoveryCodesRemaining").GetInt32());
    }

    [Fact]
    public async Task Disable_WithFreshProof_ShouldTurnOffAndReissueSession()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        (string sharedKey, _) = await EnableTwoFactorAsync(factory, client);

        AdvanceToStepBoundary(factory);
        string token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage disable = await PostJsonAsync(
            client,
            "/api/account/two-factor/disable",
            token,
            new { currentPassword = ValidPassword, code = ComputeTotpCode(sharedKey, factory.FakeTime.Current) });
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);

        using JsonDocument account = await ReadJsonAsync(await client.GetAsync("/api/account"));
        Assert.False(account.RootElement.GetProperty("twoFactorEnabled").GetBoolean());
        Assert.Equal(0, account.RootElement.GetProperty("recoveryCodesRemaining").GetInt32());
    }

    [Fact]
    public async Task RotateRecoveryCodes_ShouldInvalidateOldCodes()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        (string sharedKey, IReadOnlyList<string> recoveryCodes) = await EnableTwoFactorAsync(factory, client);

        AdvanceToStepBoundary(factory);
        string token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage rotate = await PostJsonAsync(
            client,
            "/api/account/two-factor/recovery-codes",
            token,
            new { currentPassword = ValidPassword, code = ComputeTotpCode(sharedKey, factory.FakeTime.Current) });
        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        using (JsonDocument document = await ReadJsonAsync(rotate))
        {
            Assert.Equal(10, document.RootElement.GetProperty("recoveryCodes").GetArrayLength());
        }

        using HttpClient loginClient = factory.CreateClient();
        await LoginWithChallengeAsync(loginClient);
        string verifyToken = await FetchCsrfTokenAsync(loginClient);
        using HttpResponseMessage verify = await PostJsonAsync(
            loginClient,
            "/api/auth/two-factor/verify",
            verifyToken,
            new { code = recoveryCodes[0], useRecoveryCode = true });
        Assert.Equal(HttpStatusCode.BadRequest, verify.StatusCode);
        Assert.Equal("invalid_two_factor_code", await GetErrorCodeAsync(verify));
    }

    [Fact]
    public async Task TwoFactorVerify_Concurrently_ShouldHaveExactlyOneWinner()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        (string sharedKey, _) = await EnableTwoFactorAsync(factory, client);

        // The racing verifies run on a cookieless client with manually replayed cookies: the
        // antiforgery pair stays bound to the anonymous identity, and the winner's fresh session
        // cookie cannot leak into the loser's request through a shared jar. The antiforgery cookie
        // is only ever sent on first issuance, so a brand-new jar client mints the pair here.
        using HttpClient raceClient = factory.CreateClient();
        using HttpResponseMessage csrfResponse = await raceClient.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.NoContent, csrfResponse.StatusCode);
        string token = GetCookieValue(csrfResponse, "XSRF-TOKEN")!;
        string antiforgeryCookie = GetCookieByPrefix(csrfResponse, ".AspNetCore.Antiforgery");

        using HttpResponseMessage login = await PostJsonAsync(
            raceClient, "/api/auth/login", token, new { username = Username, password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        string challenge = GetCookieValue(login, ChallengeCookieName)!;
        string cookieHeader = $"{ChallengeCookieName}={challenge}; {antiforgeryCookie}";
        string code = ComputeTotpCode(sharedKey, factory.FakeTime.Current);

        using HttpClient cookieless = factory.CreateCookielessClient();
        Task<HttpResponseMessage> firstTask = PostJsonWithChallengeAsync(cookieless, token, cookieHeader, new { code });
        Task<HttpResponseMessage> secondTask = PostJsonWithChallengeAsync(cookieless, token, cookieHeader, new { code });
        using HttpResponseMessage first = await firstTask;
        using HttpResponseMessage second = await secondTask;

        // Exactly one request may establish a session. Depending on the interleaving the loser
        // is rejected at the per-step replay guard (400) or at the atomic challenge completion
        // (410); both outcomes are correct as long as only one request wins.
        HttpStatusCode[] statuses = [first.StatusCode, second.StatusCode];
        Assert.Equal(1, statuses.Count((status) => status == HttpStatusCode.OK));
        HttpResponseMessage loser = first.StatusCode == HttpStatusCode.OK ? second : first;
        Assert.True(
            loser.StatusCode is HttpStatusCode.Gone or HttpStatusCode.BadRequest,
            "the losing request must be rejected, but was " + loser.StatusCode);
        IEnumerable<string> loserCookies = loser.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values) ? values : [];
        Assert.DoesNotContain(
            loserCookies,
            (value) => value.StartsWith(AuthCookieName + "=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1234567893L, "005924")]
    public void ComputeTotpCode_ShouldMatchRfc6238TestVectors(long unixSeconds, string expected)
    {
        const string RfcSecretBase32 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        string code = ComputeTotpCode(RfcSecretBase32, DateTimeOffset.FromUnixTimeSeconds(unixSeconds));
        Assert.Equal(expected, code);
    }

    [Fact]
    public async Task PasswordChange_InvalidNewPassword_ShouldNotConsumeRecoveryCode()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        (_, IReadOnlyList<string> codes) = await EnableTwoFactorAsync(factory, client);
        string token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage rejected = await PostJsonAsync(client, "/api/account/password", token,
            new { currentPassword = ValidPassword, newPassword = "short", confirmPassword = "short", code = codes[0], useRecoveryCode = true });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using JsonDocument account = await ReadJsonAsync(await client.GetAsync("/api/account"));
        Assert.Equal(10, account.RootElement.GetProperty("recoveryCodesRemaining").GetInt32());
        using HttpResponseMessage valid = await PostJsonAsync(client, "/api/account/password", token,
            new { currentPassword = ValidPassword, newPassword = OtherPassword, confirmPassword = OtherPassword, code = codes[0], useRecoveryCode = true });
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
    }

    [Fact]
    public async Task PasswordChange_ShouldLockOutRepeatedInvalidSecondFactors()
    {
        using AuthTestAppFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await EnableTwoFactorAsync(factory, client);
        string token = await FetchCsrfTokenAsync(client);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage rejected = await PostJsonAsync(client, "/api/account/password", token,
                new { currentPassword = ValidPassword, newPassword = OtherPassword, confirmPassword = OtherPassword, code = "not-a-code" });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        using HttpResponseMessage limited = await PostJsonAsync(client, "/api/account/password", token,
            new { currentPassword = ValidPassword, newPassword = OtherPassword, confirmPassword = OtherPassword, code = "not-a-code" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("account_locked", await GetErrorCodeAsync(limited));
    }

    private static object SetupBody() => new { username = Username, password = ValidPassword, confirmPassword = ValidPassword };

    private static async Task SetupAccountAsync(HttpClient client)
    {
        string token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage response = await PostJsonAsync(client, "/api/auth/setup", token, SetupBody());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// Runs the whole enable flow and leaves the fake clock at the next TOTP step boundary so
    /// follow-up logins never collide with the step consumed during confirmation.
    /// </summary>
    private static async Task<(string SharedKey, IReadOnlyList<string> RecoveryCodes)> EnableTwoFactorAsync(
        AuthTestAppFactory factory,
        HttpClient client)
    {
        await SetupAccountAsync(client);
        AdvanceToStepBoundary(factory);
        string token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage start = await PostJsonAsync(
            client, "/api/account/two-factor/setup", token, new { });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        string sharedKey;
        using (JsonDocument document = await ReadJsonAsync(start))
        {
            sharedKey = document.RootElement.GetProperty("sharedKey").GetString()!;
        }

        token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage confirm = await PostJsonAsync(
            client, "/api/account/two-factor/setup/confirm", token, new { code = ComputeTotpCode(sharedKey, factory.FakeTime.Current) });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        List<string> recoveryCodes = [];
        using (JsonDocument document = await ReadJsonAsync(confirm))
        {
            foreach (JsonElement element in document.RootElement.GetProperty("recoveryCodes").EnumerateArray())
            {
                recoveryCodes.Add(element.GetString()!);
            }
        }

        AdvanceToStepBoundary(factory);
        return (sharedKey, recoveryCodes);
    }

    private static async Task<string> LoginWithChallengeAsync(HttpClient client)
    {
        string token = await FetchCsrfTokenAsync(client);
        using HttpResponseMessage login = await PostJsonAsync(
            client, "/api/auth/login", token, new { username = Username, password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        string? challenge = GetCookieValue(login, ChallengeCookieName);
        Assert.False(string.IsNullOrEmpty(challenge));
        return challenge!;
    }

    private static void AdvanceToStepBoundary(AuthTestAppFactory factory)
    {
        long nowSeconds = factory.FakeTime.Current.ToUnixTimeSeconds();
        long nextStepStart = (nowSeconds / 30 + 1) * 30;
        factory.FakeTime.Advance(TimeSpan.FromSeconds(nextStepStart - nowSeconds + 2));
    }

    private static string ComputeTotpCode(string base32Key, DateTimeOffset at)
    {
        byte[] key = Base32Decode(base32Key);
        long counter = at.ToUnixTimeSeconds() / 30;
        byte[] counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        using HMACSHA1 hmac = new(key);
        byte[] hash = hmac.ComputeHash(counterBytes);
        int offset = hash[^1] & 0x0F;
        int binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string input)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        input = input.TrimEnd('=').ToUpperInvariant();
        byte[] buffer = new byte[input.Length * 5 / 8];
        int bits = 0;
        int value = 0;
        int index = 0;
        foreach (char symbol in input)
        {
            int digit = Alphabet.IndexOf(symbol);
            Assert.True(digit >= 0, "shared key must be valid base32");
            value = (value << 5) | digit;
            bits += 5;
            if (bits >= 8)
            {
                buffer[index++] = (byte)(value >> (bits - 8));
                bits -= 8;
            }
        }

        return buffer;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
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

    private static async Task<HttpResponseMessage> PostJsonWithChallengeAsync(
        HttpClient cookielessClient,
        string csrfToken,
        string cookieHeader,
        object body)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/auth/two-factor/verify");
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        request.Headers.Add("X-XSRF-TOKEN", csrfToken);
        request.Headers.Add("Cookie", cookieHeader);
        return await cookielessClient.SendAsync(request);
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

    private static string GetCookieByPrefix(HttpResponseMessage response, string prefix)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values));
        foreach (string value in values!)
        {
            string nameValue = value.Split(';')[0];
            if (nameValue.StartsWith(prefix, StringComparison.Ordinal))
            {
                return nameValue;
            }
        }

        throw new InvalidOperationException("expected a Set-Cookie header starting with " + prefix);
    }
}
