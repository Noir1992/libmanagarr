namespace LibManagarr.Api.Models.Auth;

/// <summary>
/// Stable machine-readable error codes placed in the ProblemDetails "code" extension so the
/// UI can translate failures without parsing human-readable messages.
/// </summary>
public static class AuthErrorCodes
{
    public const string SecurityStateChanged = "security_state_changed";

    public const string InvalidCredentials = "invalid_credentials";

    public const string AccountLocked = "account_locked";

    public const string TooManyRequests = "too_many_requests";

    public const string InvalidCsrfToken = "invalid_request_verification_token";

    public const string SetupNotAvailable = "setup_not_available";

    public const string CurrentPasswordIncorrect = "current_password_incorrect";

    public const string NoAccount = "no_account";

    public const string AuthStorageUnavailable = "auth_storage_unavailable";

    public const string AuthenticationRequired = "authentication_required";

    public const string AccessDenied = "access_denied";

    public const string InvalidTwoFactorCode = "invalid_two_factor_code";

    public const string SecondFactorRequired = "second_factor_required";

    public const string TwoFactorChallengeInvalid = "two_factor_challenge_invalid";

    public const string TwoFactorSetupInvalid = "two_factor_setup_invalid";

    public const string TwoFactorAlreadyEnabled = "two_factor_already_enabled";

    public const string TwoFactorNotEnabled = "two_factor_not_enabled";
}
