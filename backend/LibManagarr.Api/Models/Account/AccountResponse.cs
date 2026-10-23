namespace LibManagarr.Api.Models.Account;

/// <summary>
/// Public account view for the settings account tab. It never carries password hashes,
/// session data, secrets or recovery codes.
/// </summary>
public sealed class AccountResponse
{
    public required string Username { get; init; }

    public required bool TwoFactorEnabled { get; init; }

    /// <summary>Unused one-time recovery codes left; part of the 2FA health display.</summary>
    public required int RecoveryCodesRemaining { get; init; }
}
