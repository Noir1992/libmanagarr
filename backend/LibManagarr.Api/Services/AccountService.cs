using Microsoft.AspNetCore.Identity;
using LibManagarr.Api.Models.Auth;

namespace LibManagarr.Api.Services;

/// <summary>
/// Account singleton: initial setup, credential validation with temporary lockouts and
/// password changes. Passwords are never trimmed; usernames are trimmed on the outside and
/// compared case-insensitively via their normalized form. All limits are enforced identically
/// in the UI so the backend stays the single authority.
/// </summary>
public sealed class AccountService
{
    public const int UsernameMinLength = 3;
    public const int UsernameMaxLength = 64;
    public const int PasswordMinLength = 12;
    public const int PasswordMaxLength = 128;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    private readonly IAccountStore accountStore;
    private readonly IPasswordHasher<AccountRecord> passwordHasher;
    private readonly TimeProvider timeProvider;
    private readonly TwoFactorService twoFactorService;
    private readonly ISettingsService settingsService;

    public AccountService(IAccountStore accountStore, IPasswordHasher<AccountRecord> passwordHasher, TimeProvider timeProvider, TwoFactorService twoFactorService, ISettingsService settingsService)
    {
        this.accountStore = accountStore;
        this.passwordHasher = passwordHasher;
        this.timeProvider = timeProvider;
        this.twoFactorService = twoFactorService;
        this.settingsService = settingsService;
    }

    public Task<AccountRecord?> GetAccountAsync(CancellationToken cancellationToken) =>
        accountStore.GetAccountAsync(cancellationToken);

    public DateTimeOffset GetUtcNow() => timeProvider.GetUtcNow();

    /// <summary>
    /// Shared identity proof of every protected operation: the current password first, then the
    /// second factor only when the account has one, so a wrong password never burns a recovery
    /// code. A server-side failure counts exactly once and the request that reaches the limit
    /// already answers the lockout; a complete proof clears the shared counter.
    /// </summary>
    public async Task<(SecondFactorProbe? Proof, AccountOperationResult Result)> VerifyIdentityProofAsync(
        AccountRecord account, string? currentPassword, string? code, bool useRecoveryCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(currentPassword))
        {
            return (null, AccountOperationResult.InvalidCredentialsFailure);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (account.IsLockedOut(now))
        {
            return (null, AccountOperationResult.LockedOutUntil(account.LockoutUntil!.Value));
        }

        if (passwordHasher.VerifyHashedPassword(account, account.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            return (null, await RegisterFailureAsync(account.Id, now, AccountOperationStatus.InvalidCredentials, cancellationToken));
        }

        if (!account.TwoFactorEnabled)
        {
            await accountStore.ResetAccessFailuresAsync(account.Id, now, cancellationToken);
            return (null, AccountOperationResult.Success(account));
        }

        SecondFactorProbe proof = await twoFactorService.ProbeSecondFactorAsync(account, code, useRecoveryCode, cancellationToken);
        if (proof.Status == SecondFactorProofStatus.Success)
        {
            await accountStore.ResetAccessFailuresAsync(account.Id, now, cancellationToken);
            return (proof, AccountOperationResult.Success(account));
        }

        AccountOperationStatus failure = proof.Status == SecondFactorProofStatus.MissingCode
            ? AccountOperationStatus.SecondFactorRequired
            : AccountOperationStatus.InvalidSecondFactor;
        return (null, await RegisterFailureAsync(account.Id, now, failure, cancellationToken));
    }

    /// <summary>
    /// Counts one failed proof with the currently stored limit and reports the state the row really
    /// holds afterwards, so reaching the limit is answered by the lock itself and not by the
    /// failure that caused it.
    /// </summary>
    private async Task<AccountOperationResult> RegisterFailureAsync(
        int accountId, DateTimeOffset now, AccountOperationStatus failure, CancellationToken cancellationToken)
    {
        int maxFailedAttempts = (await settingsService.GetAsync(cancellationToken)).AuthProtection.MaxFailedAttempts;
        AccountRecord? state = await accountStore.RegisterFailedAccessAttemptAsync(accountId, maxFailedAttempts, LockoutDuration, now, cancellationToken);
        return AccountOperationResult.FromFailure(state, now, failure, maxFailedAttempts);
    }

    public async Task<AccountOperationResult> SetupAsync(string? username, string? password, string? confirmPassword, CancellationToken cancellationToken)
    {
        List<AccountFieldError> errors = ValidateUsername(username);
        ValidatePassword(password, "password", errors);
        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            errors.Add(new AccountFieldError("confirmPassword", "password_confirmation_mismatch"));
        }

        if (errors.Count > 0)
        {
            return AccountOperationResult.ValidationFailure(errors);
        }

        string trimmedUsername = username!.Trim();
        string normalizedUsername = NormalizeUsername(trimmedUsername);
        AccountRecord placeholder = new()
        {
            Id = 1,
            Username = trimmedUsername,
            NormalizedUsername = normalizedUsername,
            PasswordHash = string.Empty,
            SecurityVersion = 1,
            CreatedAt = timeProvider.GetUtcNow(),
            UpdatedAt = timeProvider.GetUtcNow(),
            FailedAccessCount = 0
        };
        string passwordHash = passwordHasher.HashPassword(placeholder, password!);

        bool created = await accountStore.TryCreateAccountAsync(
            trimmedUsername,
            normalizedUsername,
            passwordHash,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (!created)
        {
            return AccountOperationResult.AlreadyExistsFailure;
        }

        AccountRecord? account = await accountStore.GetAccountAsync(cancellationToken);
        return account is null ? AccountOperationResult.NoAccountFailure : AccountOperationResult.Success(account);
    }

    public async Task<AccountOperationResult> ValidateCredentialsAsync(string? username, string? password, CancellationToken cancellationToken)
    {
        AccountRecord? account = await accountStore.GetAccountAsync(cancellationToken);
        if (account is null)
        {
            return AccountOperationResult.NoAccountFailure;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (account.IsLockedOut(now))
        {
            return AccountOperationResult.LockedOutUntil(account.LockoutUntil!.Value);
        }

        // Unknown usernames answer with the same public failure and never touch the failure
        // counter of the real account, so they cannot lock it out through the front door.
        string normalizedUsername = NormalizeUsername((username ?? string.Empty).Trim());
        if (password is null || !string.Equals(normalizedUsername, account.NormalizedUsername, StringComparison.Ordinal))
        {
            return AccountOperationResult.InvalidCredentialsFailure;
        }

        PasswordVerificationResult verification = passwordHasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return await RegisterFailureAsync(account.Id, now, AccountOperationStatus.InvalidCredentials, cancellationToken);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            if (!await accountStore.UpdatePasswordHashOnlyAsync(account, passwordHasher.HashPassword(account, password), now, cancellationToken))
                return AccountOperationResult.InvalidCredentialsFailure;
            AccountRecord? refreshed = await accountStore.GetAccountAsync(cancellationToken);
            if (refreshed is null || refreshed.SecurityVersion != account.SecurityVersion)
                return AccountOperationResult.InvalidCredentialsFailure;
            account = refreshed;
        }

        if (!account.TwoFactorEnabled) await accountStore.ResetAccessFailuresAsync(account.Id, now, cancellationToken);
        return AccountOperationResult.Success(account);
    }

    public async Task<AccountOperationResult> ChangePasswordAsync(
        AccountRecord account,
        string sessionToken,
        string? currentPassword,
        string? newPassword,
        string? confirmPassword,
        string? code,
        bool useRecoveryCode,
        CancellationToken cancellationToken)
    {
        List<AccountFieldError> errors = [];
        if (string.IsNullOrEmpty(currentPassword))
        {
            errors.Add(new AccountFieldError("currentPassword", "current_password_required"));
        }

        ValidatePassword(newPassword, "newPassword", errors);
        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            errors.Add(new AccountFieldError("confirmPassword", "password_confirmation_mismatch"));
        }

        if (errors.Count == 0 && string.Equals(newPassword, currentPassword, StringComparison.Ordinal))
        {
            errors.Add(new AccountFieldError("newPassword", "password_reuse"));
        }

        if (errors.Count > 0)
        {
            return AccountOperationResult.ValidationFailure(errors);
        }

        (SecondFactorProbe? proof, AccountOperationResult proofResult) = await VerifyIdentityProofAsync(
            account, currentPassword, code, useRecoveryCode, cancellationToken);
        if (proofResult.Status != AccountOperationStatus.Success) return proofResult;

        string newPasswordHash = passwordHasher.HashPassword(account, newPassword!);
        bool changed = await accountStore.TryChangePasswordAsync(account, AuthSessionService.HashToken(sessionToken),
            newPasswordHash, proof, timeProvider.GetUtcNow(), cancellationToken);
        if (!changed) return new AccountOperationResult(AccountOperationStatus.StateChanged);
        AccountRecord? updated = await accountStore.GetAccountAsync(cancellationToken);
        return updated is null ? AccountOperationResult.NoAccountFailure : AccountOperationResult.Success(updated);
    }

    internal static string NormalizeUsername(string username) => username.Trim().ToUpperInvariant();

    private static List<AccountFieldError> ValidateUsername(string? username)
    {
        List<AccountFieldError> errors = [];
        string trimmed = username?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            errors.Add(new AccountFieldError("username", "username_required"));
        }
        else if (trimmed.Length is < UsernameMinLength or > UsernameMaxLength)
        {
            errors.Add(new AccountFieldError("username", "username_length"));
        }

        return errors;
    }

    private static void ValidatePassword(string? password, string field, List<AccountFieldError> errors)
    {
        if (string.IsNullOrEmpty(password))
        {
            errors.Add(new AccountFieldError(field, "password_required"));
        }
        else if (password.Length is < PasswordMinLength or > PasswordMaxLength)
        {
            errors.Add(new AccountFieldError(field, "password_length"));
        }
    }
}
