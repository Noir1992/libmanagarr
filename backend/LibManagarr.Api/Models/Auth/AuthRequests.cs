namespace LibManagarr.Api.Models.Auth;

public sealed class SetupRequest
{
    public string? Username { get; init; }

    public string? Password { get; init; }

    public string? ConfirmPassword { get; init; }

    public AppLanguage? Language { get; init; }
}

public sealed class LoginRequest
{
    public string? Username { get; init; }

    public string? Password { get; init; }

    /// <summary>Persistent cookie for the full session lifetime when true; a browser-session cookie otherwise.</summary>
    public bool Remember { get; init; }
}

public sealed class ChangePasswordRequest
{
    public string? CurrentPassword { get; init; }

    public string? NewPassword { get; init; }

    public string? ConfirmPassword { get; init; }

    /// <summary>Fresh TOTP or recovery code; mandatory for the change while 2FA is active.</summary>
    public string? Code { get; init; }

    public bool UseRecoveryCode { get; init; }
}

public sealed class TwoFactorConfirmRequest
{
    public string? Code { get; init; }
}

/// <summary>
/// Current password plus a fresh second factor; used by the endpoints that disable 2FA or
/// rotate the recovery codes. The password is always verified before any code is consumed.
/// </summary>
public sealed class TwoFactorProofRequest
{
    public string? CurrentPassword { get; init; }

    public string? Code { get; init; }

    public bool UseRecoveryCode { get; init; }
}

public sealed class TwoFactorVerifyRequest
{
    public string? Code { get; init; }

    public bool UseRecoveryCode { get; init; }

    /// <summary>Persistent cookie for the full session lifetime when true; a browser-session cookie otherwise.</summary>
    public bool Remember { get; init; }
}
