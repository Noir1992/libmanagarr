namespace LibManagarr.Api.Models.Auth;

public enum AccountOperationStatus
{
    Success,
    ValidationFailed,
    InvalidCredentials,
    LockedOut,
    AlreadyExists,
    NoAccount,
    SecondFactorRequired,
    InvalidSecondFactor,
    StateChanged
}

/// <summary>
/// Field-level validation failure with a stable code the UI translates, e.g.
/// ("password", "password_length").
/// </summary>
public sealed record AccountFieldError(string Field, string Code);

/// <summary>
/// Outcome of an account operation. Validation failures list their field errors, a lockout
/// carries the point in time when the account accepts attempts again, and a counted failure
/// carries the attempts the row really has left against the limit that was in force.
/// </summary>
public sealed record AccountOperationResult(
    AccountOperationStatus Status,
    AccountRecord? Account = null,
    IReadOnlyList<AccountFieldError>? Errors = null,
    DateTimeOffset? LockedUntil = null,
    int? RemainingAttempts = null)
{
    public static AccountOperationResult Success(AccountRecord account) => new(AccountOperationStatus.Success, account);

    public static AccountOperationResult ValidationFailure(IReadOnlyList<AccountFieldError> errors) =>
        new(AccountOperationStatus.ValidationFailed, Errors: errors);

    public static AccountOperationResult InvalidCredentialsFailure { get; } = new(AccountOperationStatus.InvalidCredentials);

    public static AccountOperationResult AlreadyExistsFailure { get; } = new(AccountOperationStatus.AlreadyExists);

    public static AccountOperationResult NoAccountFailure { get; } = new(AccountOperationStatus.NoAccount);

    public static AccountOperationResult LockedOutUntil(DateTimeOffset lockedUntil) =>
        new(AccountOperationStatus.LockedOut, LockedUntil: lockedUntil);

    /// <summary>
    /// Turns the account row a registered failure produced into the operation outcome. The lockout
    /// wins over the failure that caused it, so the request reaching the limit already answers the
    /// lock instead of repeating the credential failure behind it. A missing row stays without
    /// counts, which is also how an unknown username answers without details about the real one.
    /// </summary>
    public static AccountOperationResult FromFailure(
        AccountRecord? state, DateTimeOffset now, AccountOperationStatus failure, int maxFailedAttempts)
    {
        if (state is null)
        {
            return new AccountOperationResult(failure);
        }

        return state.IsLockedOut(now)
            ? LockedOutUntil(state.LockoutUntil!.Value)
            : new AccountOperationResult(failure, RemainingAttempts: Math.Max(0, maxFailedAttempts - state.FailedAccessCount));
    }
}
