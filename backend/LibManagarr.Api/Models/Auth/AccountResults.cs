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
/// carries the point in time when the account accepts attempts again.
/// </summary>
public sealed record AccountOperationResult(
    AccountOperationStatus Status,
    AccountRecord? Account = null,
    IReadOnlyList<AccountFieldError>? Errors = null,
    DateTimeOffset? LockedUntil = null)
{
    public static AccountOperationResult Success(AccountRecord account) => new(AccountOperationStatus.Success, account);

    public static AccountOperationResult ValidationFailure(IReadOnlyList<AccountFieldError> errors) =>
        new(AccountOperationStatus.ValidationFailed, Errors: errors);

    public static AccountOperationResult InvalidCredentialsFailure { get; } = new(AccountOperationStatus.InvalidCredentials);

    public static AccountOperationResult AlreadyExistsFailure { get; } = new(AccountOperationStatus.AlreadyExists);

    public static AccountOperationResult NoAccountFailure { get; } = new(AccountOperationStatus.NoAccount);

    public static AccountOperationResult LockedOutUntil(DateTimeOffset lockedUntil) =>
        new(AccountOperationStatus.LockedOut, LockedUntil: lockedUntil);
}
