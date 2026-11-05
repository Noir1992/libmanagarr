using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using OtpNet;
using QRCoder;
using LibManagarr.Api.Models.Auth;

namespace LibManagarr.Api.Services;

public enum SecondFactorProofStatus
{
    Success,

    MissingCode,

    InvalidCode
}

/// <summary>
/// Non-consuming second-factor check result. Keeping the consumed step or recovery code hash
/// around lets multi-step flows (password change) run the probe first and consume atomically
/// only after every other step succeeded, so a later failure never burns a recovery code.
/// </summary>
public sealed record SecondFactorProbe(SecondFactorProofStatus Status, long? Step = null, string? RecoveryCodeHash = null);

public sealed record IssuedChallenge(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// TOTP setup, login challenges and recovery codes on top of Otp.NET. Secrets are 160-bit
/// random values persisted only through Data Protection under their own purpose; QR codes are
/// rendered locally as SVG. Six digits, 30-second SHA-1 steps and a verification window of one
/// step in each direction keep the flow compatible with common authenticator apps while the
/// strictly monotonic replay marker accepts every time step at most once per account.
/// </summary>
public sealed class TwoFactorService
{
    public const int TotpDigits = 6;
    public const int TotpStepSeconds = 30;
    public const int MaxSetupAttempts = 5;
    public const int MaxWrongChallengeAttempts = 5;
    public const int RecoveryCodeCount = 10;
    public static readonly TimeSpan SetupLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
    private const int SecretByteLength = 20;
    private const int RecoveryCodeByteLength = 16;
    private const string IssuerName = "LibManagarr";

    private readonly IAccountStore accountStore;
    private readonly TimeProvider timeProvider;
    private readonly IDataProtector secretProtector;
    private readonly ISettingsService settingsService;

    public TwoFactorService(IAccountStore accountStore, TimeProvider timeProvider, IDataProtectionProvider dataProtectionProvider, ISettingsService settingsService)
    {
        this.accountStore = accountStore;
        this.timeProvider = timeProvider;
        this.secretProtector = dataProtectionProvider.CreateProtector("LibManagarr.TwoFactorSecret.v1");
        this.settingsService = settingsService;
    }

    public static byte[] GenerateSecret() => KeyGeneration.GenerateRandomKey(SecretByteLength);

    public static string EncodeSharedKey(byte[] secret) => Base32Encoding.ToString(secret);

    public string ProtectSecret(byte[] secret) => Convert.ToBase64String(secretProtector.Protect(secret));

    public byte[] UnprotectSecret(string protectedSecret) => secretProtector.Unprotect(Convert.FromBase64String(protectedSecret));

    public static string BuildAuthenticatorUri(string username, byte[] secret) =>
        new OtpUri(OtpType.Totp, secret, username, IssuerName, OtpHashMode.Sha1, TotpDigits, TotpStepSeconds, 0).ToString();

    public static string BuildQrCodeSvg(string authenticatorUri)
    {
        using QRCodeGenerator generator = new();
        using QRCodeData data = generator.CreateQrCode(authenticatorUri, QRCodeGenerator.ECCLevel.Q);
        using SvgQRCode svg = new(data);
        return svg.GetGraphic(20);
    }

    public static string NormalizeRecoveryCode(string? code) =>
        string.Concat((code ?? string.Empty).Where((ch) => ch is not ' ' and not '-' and not '\t').Select(char.ToUpperInvariant));

    public static string HashRecoveryCode(string normalizedCode) => AuthSessionService.HashToken(normalizedCode);

    private static (IReadOnlyList<string> Plaintext, IReadOnlyList<RecoveryCodeRecord> Records) GenerateRecoveryCodes()
    {
        string[] plaintext = Enumerable.Range(0, RecoveryCodeCount)
            .Select(_ => Base32Encoding.ToString(RandomNumberGenerator.GetBytes(RecoveryCodeByteLength))).ToArray();
        RecoveryCodeRecord[] records = plaintext.Select(code => new RecoveryCodeRecord
        {
            Id = UuidV7.Create(), CodeHash = HashRecoveryCode(NormalizeRecoveryCode(code))
        }).ToArray();
        return (plaintext, records);
    }

    public async Task<IReadOnlyList<string>?> RotateRecoveryCodesAsync(AccountRecord account, string sessionToken,
        SecondFactorProbe proof, CancellationToken cancellationToken)
    {
        var codes = GenerateRecoveryCodes();
        bool changed = await accountStore.TryRotateRecoveryCodesAsync(account, AuthSessionService.HashToken(sessionToken),
            proof, codes.Records, timeProvider.GetUtcNow(), cancellationToken);
        return changed ? codes.Plaintext : null;
    }

    public Task<int> GetRecoveryCodesRemainingAsync(int accountId, CancellationToken cancellationToken) =>
        accountStore.CountUnusedRecoveryCodesAsync(accountId, cancellationToken);

    public async Task<TwoFactorSetupRecord> CreateSetupAsync(AccountRecord account, string sessionToken, byte[] secret, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        DateTimeOffset now = timeProvider.GetUtcNow();
        TwoFactorSetupRecord setup = new()
        {
            Id = UuidV7.Create(),
            AccountId = account.Id,
            SessionTokenHash = AuthSessionService.HashToken(sessionToken),
            ProtectedSecret = ProtectSecret(secret),
            CreatedAt = now,
            ExpiresAt = now + SetupLifetime,
            AttemptCount = 0
        };
        await accountStore.CreateSetupAsync(setup, now, cancellationToken);
        return setup;
    }

    public Task<TwoFactorSetupRecord?> GetActiveSetupForSessionAsync(string sessionToken, CancellationToken cancellationToken) =>
        accountStore.FindActiveSetupForSessionAsync(AuthSessionService.HashToken(sessionToken), cancellationToken);

    public bool TryVerifySetupCode(TwoFactorSetupRecord setup, string? code, out long step) =>
        TryVerifyTotpCode(UnprotectSecret(setup.ProtectedSecret), code, out step);

    /// <summary>
    /// Flips 2FA on for the account of a confirmed setup. The WHERE guard on the account row
    /// makes concurrent confirms have exactly one winner and never replaces an active secret.
    /// </summary>
    public async Task<IReadOnlyList<string>?> EnableFromSetupAsync(AccountRecord account, TwoFactorSetupRecord setup,
        long firstStep, CancellationToken cancellationToken)
    {
        var codes = GenerateRecoveryCodes();
        bool enabled = await accountStore.TryEnableTwoFactorAsync(account, setup, firstStep, codes.Records,
            timeProvider.GetUtcNow(), cancellationToken);
        return enabled ? codes.Plaintext : null;
    }

    public Task<bool> DisableAndRevokeAsync(AccountRecord account, string sessionToken, SecondFactorProbe proof,
        CancellationToken cancellationToken) => accountStore.TryDisableTwoFactorAsync(account,
            AuthSessionService.HashToken(sessionToken), proof, timeProvider.GetUtcNow(), cancellationToken);

    public Task CancelSetupForSessionAsync(string sessionToken, CancellationToken cancellationToken) =>
        accountStore.RevokeSetupsForSessionAsync(AuthSessionService.HashToken(sessionToken), timeProvider.GetUtcNow(), cancellationToken);

    public async Task<AccountOperationResult> RecordSetupFailureAsync(TwoFactorSetupRecord setup, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        await accountStore.IncrementSetupAttemptAsync(setup.Id, MaxSetupAttempts, now, cancellationToken);
        return await RegisterAccountFailureAsync(setup.AccountId, now, cancellationToken);
    }

    public async Task<IssuedChallenge> CreateLoginChallengeAsync(AccountRecord account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset expiresAt = now + ChallengeLifetime;
        await accountStore.AddChallengeAsync(
            new TwoFactorChallengeRecord
            {
                TokenHash = AuthSessionService.HashToken(token),
                AccountId = account.Id,
                SecurityVersion = account.SecurityVersion,
                CreatedAt = now,
                ExpiresAt = expiresAt,
                WrongAttemptCount = 0
            },
            cancellationToken);
        return new IssuedChallenge(token, expiresAt);
    }

    public Task<TwoFactorChallengeRecord?> FindChallengeAsync(string? challengeToken, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(challengeToken)
            ? Task.FromResult<TwoFactorChallengeRecord?>(null)
            : accountStore.FindChallengeByTokenHashAsync(AuthSessionService.HashToken(challengeToken), cancellationToken);

    public async Task<AccountOperationResult> RecordChallengeFailureAsync(TwoFactorChallengeRecord challenge, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        await accountStore.IncrementChallengeAttemptsAsync(challenge.TokenHash, MaxWrongChallengeAttempts, now, cancellationToken);
        return await RegisterAccountFailureAsync(challenge.AccountId, now, cancellationToken);
    }

    /// <summary>
    /// A wrong second factor is counted on the shared account counter with the currently stored
    /// limit. The answer is the state the row really holds afterwards, so reaching the limit is
    /// reported as the lock itself and never as another wrong code.
    /// </summary>
    private async Task<AccountOperationResult> RegisterAccountFailureAsync(int accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        int maxFailedAttempts = (await settingsService.GetAsync(cancellationToken)).AuthProtection.MaxFailedAttempts;
        AccountRecord? state = await accountStore.RegisterFailedAccessAttemptAsync(accountId, maxFailedAttempts, AccountService.LockoutDuration, now, cancellationToken);
        return AccountOperationResult.FromFailure(state, now, AccountOperationStatus.InvalidSecondFactor, maxFailedAttempts);
    }

    public Task<bool> CompleteLoginAsync(AccountRecord account, TwoFactorChallengeRecord challenge,
        SecondFactorProbe proof, CancellationToken cancellationToken) =>
        accountStore.TryCompleteLoginChallengeAsync(account, challenge, proof, timeProvider.GetUtcNow(), cancellationToken);

    public async Task<SecondFactorProbe> ProbeSecondFactorAsync(AccountRecord account, string? code, bool useRecoveryCode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (!account.TwoFactorEnabled)
        {
            return new SecondFactorProbe(SecondFactorProofStatus.InvalidCode);
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return new SecondFactorProbe(SecondFactorProofStatus.MissingCode);
        }

        if (useRecoveryCode)
        {
            string normalized = NormalizeRecoveryCode(code);
            if (normalized.Length == 0)
            {
                return new SecondFactorProbe(SecondFactorProofStatus.MissingCode);
            }

            string codeHash = HashRecoveryCode(normalized);
            bool unused = await accountStore.HasUnusedRecoveryCodeAsync(account.Id, codeHash, cancellationToken);
            return unused
                ? new SecondFactorProbe(SecondFactorProofStatus.Success, RecoveryCodeHash: codeHash)
                : new SecondFactorProbe(SecondFactorProofStatus.InvalidCode);
        }

        if (account.TotpSecret is null)
        {
            return new SecondFactorProbe(SecondFactorProofStatus.InvalidCode);
        }

        if (!TryVerifyTotpCode(UnprotectSecret(account.TotpSecret), code, out long step))
        {
            return new SecondFactorProbe(SecondFactorProofStatus.InvalidCode);
        }

        if (account.LastUsedStep.HasValue && step <= account.LastUsedStep.Value)
        {
            return new SecondFactorProbe(SecondFactorProofStatus.InvalidCode);
        }

        return new SecondFactorProbe(SecondFactorProofStatus.Success, Step: step);
    }

    private bool TryVerifyTotpCode(byte[] secret, string? code, out long step)
    {
        step = 0;
        string trimmed = (code ?? string.Empty).Trim();
        if (trimmed.Length != TotpDigits || !trimmed.All(char.IsAsciiDigit))
        {
            return false;
        }

        Totp totp = new(secret, TotpStepSeconds, OtpHashMode.Sha1, TotpDigits, TimeCorrection.UncorrectedInstance);
        return totp.VerifyTotp(timeProvider.GetUtcNow().UtcDateTime, trimmed, out step, new VerificationWindow(1, 1));
    }
}
