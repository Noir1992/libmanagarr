using Microsoft.Extensions.DependencyInjection;
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

    private static string AuthSessionServiceHash(IssuedSession session) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(session.SessionToken)));
}
