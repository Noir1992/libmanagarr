namespace LibManagarr.Api.Models.Auth;

public sealed class AuthStateResponse
{
    public required bool SetupRequired { get; init; }

    public required bool Authenticated { get; init; }

    /// <summary>Configured interface language; null until the initial setup has created an account.</summary>
    public AppLanguage? Language { get; init; }
}

public sealed class LoginResponse
{
    public required bool RequiresTwoFactor { get; init; }
}

public sealed class TwoFactorStateResponse
{
    public required bool Pending { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed class TwoFactorSetupResponse
{
    public required string SharedKey { get; init; }

    public required string AuthenticatorUri { get; init; }

    /// <summary>Locally rendered SVG of the otpauth URI; no external QR service is ever called.</summary>
    public required string QrCodeSvg { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Plaintext recovery codes shown exactly once in the response that created them.</summary>
public sealed class RecoveryCodesResponse
{
    public required IReadOnlyList<string> RecoveryCodes { get; init; }
}
