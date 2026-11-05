using Microsoft.Extensions.DependencyInjection;
using LibManagarr.Api.Models;
using LibManagarr.Api.Models.Auth;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests;

public sealed class AccountSecurityTransactionTests
{
    [Theory]
    [InlineData("cancelled")]
    [InlineData("expired")]
    [InlineData("revoked-session")]
    [InlineData("replaced")]
    [InlineData("changed-password")]
    public async Task SetupConfirmation_ShouldRecheckPersistedState(string invalidation)
    {
        using AuthTestAppFactory factory = new();
        using IServiceScope scope = factory.Services.CreateScope();
        AccountService accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        AuthSessionService sessions = scope.ServiceProvider.GetRequiredService<AuthSessionService>();
        TwoFactorService factors = scope.ServiceProvider.GetRequiredService<TwoFactorService>();
        IAccountStore store = scope.ServiceProvider.GetRequiredService<IAccountStore>();
        AccountRecord account = (await accounts.SetupAsync("admin", "a-valid-test-password", "a-valid-test-password", default)).Account!;
        IssuedSession session = await sessions.IssueSessionAsync(account, default);
        TwoFactorSetupRecord setup = await factors.CreateSetupAsync(account, session.SessionToken, TwoFactorService.GenerateSecret(), default);

        switch (invalidation)
        {
            case "cancelled":
                await factors.CancelSetupForSessionAsync(session.SessionToken, default);
                break;
            case "expired":
                factory.FakeTime.Advance(TimeSpan.FromMinutes(11));
                break;
            case "revoked-session":
                await sessions.RevokeCurrentSessionAsync(session.Principal, default);
                break;
            case "replaced":
                await factors.CreateSetupAsync(account, session.SessionToken, TwoFactorService.GenerateSecret(), default);
                break;
            case "changed-password":
                Assert.True(await store.TryChangePasswordAsync(account, AuthSessionServiceHash(session), account.PasswordHash, null, factory.FakeTime.Current, default));
                break;
        }

        Assert.Null(await factors.EnableFromSetupAsync(account, setup, 12345, default));
        Assert.False((await accounts.GetAccountAsync(default))!.TwoFactorEnabled);
        Assert.Equal(0, await factors.GetRecoveryCodesRemainingAsync(account.Id, default));
    }

    [Fact]
    public async Task CompletedChallenge_ShouldNotConsumeAnotherRecoveryCode()
    {
        using AuthTestAppFactory factory = new();
        using IServiceScope scope = factory.Services.CreateScope();
        AccountService accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        AuthSessionService sessions = scope.ServiceProvider.GetRequiredService<AuthSessionService>();
        TwoFactorService factors = scope.ServiceProvider.GetRequiredService<TwoFactorService>();
        AccountRecord account = (await accounts.SetupAsync("admin", "a-valid-test-password", "a-valid-test-password", default)).Account!;
        IssuedSession session = await sessions.IssueSessionAsync(account, default);
        TwoFactorSetupRecord setup = await factors.CreateSetupAsync(account, session.SessionToken, TwoFactorService.GenerateSecret(), default);
        IReadOnlyList<string> codes = (await factors.EnableFromSetupAsync(account, setup, 12345, default))!;
        account = (await accounts.GetAccountAsync(default))!;
        IssuedChallenge issued = await factors.CreateLoginChallengeAsync(account, default);
        TwoFactorChallengeRecord challenge = (await factors.FindChallengeAsync(issued.Token, default))!;
        SecondFactorProbe first = await factors.ProbeSecondFactorAsync(account, codes[0], true, default);
        SecondFactorProbe second = await factors.ProbeSecondFactorAsync(account, codes[1], true, default);

        bool[] completed = await Task.WhenAll(factors.CompleteLoginAsync(account, challenge, first, default),
            factors.CompleteLoginAsync(account, challenge, second, default));

        Assert.Single(completed, success => success);
        Assert.Equal(9, await factors.GetRecoveryCodesRemainingAsync(account.Id, default));
        int unused = 0;
        foreach (string code in codes.Take(2))
            if ((await factors.ProbeSecondFactorAsync(account, code, true, default)).Status == SecondFactorProofStatus.Success) unused++;
        Assert.Equal(1, unused);
    }

    [Fact]
    public async Task PasswordChange_WithStaleAccount_ShouldNotOverwriteNewPassword()
    {
        using AuthTestAppFactory factory = new();
        using IServiceScope scope = factory.Services.CreateScope();
        AccountService accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        AuthSessionService sessions = scope.ServiceProvider.GetRequiredService<AuthSessionService>();
        IAccountStore store = scope.ServiceProvider.GetRequiredService<IAccountStore>();
        AccountRecord stale = (await accounts.SetupAsync("admin", "a-valid-test-password", "a-valid-test-password", default)).Account!;
        IssuedSession session = await sessions.IssueSessionAsync(stale, default);
        Assert.True(await store.TryChangePasswordAsync(stale, AuthSessionServiceHash(session), "first-change", null, factory.FakeTime.Current, default));

        Assert.False(await store.TryChangePasswordAsync(stale, AuthSessionServiceHash(session), "stale-change", null, factory.FakeTime.Current, default));
        Assert.Equal("first-change", (await accounts.GetAccountAsync(default))!.PasswordHash);
    }

    [Fact]
    public async Task FailedProof_ShouldUseTheLimitFromSettingsWithoutRestart()
    {
        using AuthTestAppFactory factory = new();
        using IServiceScope scope = factory.Services.CreateScope();
        AccountService accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        ISettingsService settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        AccountRecord account = (await accounts.SetupAsync("admin", "a-valid-test-password", "a-valid-test-password", default)).Account!;
        AppSettings stored = await settings.GetAsync(default);
        stored.AuthProtection.MaxFailedAttempts = 2;
        await settings.SaveAsync(stored, default);

        Assert.Equal(
            AccountOperationStatus.InvalidCredentials,
            (await accounts.VerifyIdentityProofAsync(account, "wrong-password", null, false, default)).Result.Status);
        AccountOperationResult second = (await accounts.VerifyIdentityProofAsync(account, "wrong-password", null, false, default)).Result;

        Assert.Equal(AccountOperationStatus.LockedOut, second.Status);
        Assert.Equal(factory.FakeTime.Current + AccountService.LockoutDuration, second.LockedUntil!.Value);
    }

    [Fact]
    public async Task SuccessfulProof_ShouldClearTheSharedFailureCounter()
    {
        using AuthTestAppFactory factory = new();
        using IServiceScope scope = factory.Services.CreateScope();
        AccountService accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        AccountRecord account = (await accounts.SetupAsync("admin", "a-valid-test-password", "a-valid-test-password", default)).Account!;
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            Assert.Equal(
                AccountOperationStatus.InvalidCredentials,
                (await accounts.VerifyIdentityProofAsync(account, "wrong-password", null, false, default)).Result.Status);
        }

        Assert.Equal(
            AccountOperationStatus.Success,
            (await accounts.VerifyIdentityProofAsync(account, "a-valid-test-password", null, false, default)).Result.Status);

        for (int attempt = 1; attempt <= 4; attempt++)
        {
            Assert.Equal(
                AccountOperationStatus.InvalidCredentials,
                (await accounts.VerifyIdentityProofAsync(account, "wrong-password", null, false, default)).Result.Status);
        }

        Assert.Equal(
            AccountOperationStatus.LockedOut,
            (await accounts.VerifyIdentityProofAsync(account, "wrong-password", null, false, default)).Result.Status);
    }

    [Fact]
    public async Task ProofAgainstLockedAccount_ShouldLeaveTheStoredLockoutAlone()
    {
        using AuthTestAppFactory factory = new();
        using IServiceScope scope = factory.Services.CreateScope();
        AccountService accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        IAccountStore store = scope.ServiceProvider.GetRequiredService<IAccountStore>();
        AccountRecord account = (await accounts.SetupAsync("admin", "a-valid-test-password", "a-valid-test-password", default)).Account!;
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            await accounts.VerifyIdentityProofAsync(account, "wrong-password", null, false, default);
        }

        AccountRecord locked = (await accounts.GetAccountAsync(default))!;
        DateTimeOffset lockedUntil = locked.LockoutUntil!.Value;
        Assert.True(locked.IsLockedOut(factory.FakeTime.Current));
        factory.FakeTime.Advance(TimeSpan.FromMinutes(1));

        AccountRecord? afterRacingAttempt = await store.RegisterFailedAccessAttemptAsync(
            locked.Id, 5, AccountService.LockoutDuration, factory.FakeTime.Current, default);
        await store.ResetAccessFailuresAsync(locked.Id, factory.FakeTime.Current, default);
        AccountRecord afterReset = (await accounts.GetAccountAsync(default))!;

        Assert.Equal(lockedUntil, afterRacingAttempt!.LockoutUntil!.Value);
        Assert.Equal(0, afterRacingAttempt.FailedAccessCount);
        Assert.Equal(lockedUntil, afterReset.LockoutUntil!.Value);
        Assert.True(afterReset.IsLockedOut(factory.FakeTime.Current));
    }

    private static string AuthSessionServiceHash(IssuedSession session) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(session.SessionToken)));
}
