namespace LibManagarr.Api.Models.Backup;

/// <summary>
/// Proof of identity plus the password that protects the new archive. The identity fields follow
/// the account area, so a backup needs the same proof as a password change. The backup password
/// is never stored: it only ever exists in this request and in the browser tab that typed it.
/// </summary>
public sealed class BackupExportRequest
{
    public required string BackupPassword { get; init; }

    public required string CurrentPassword { get; init; }

    public string? Code { get; init; }

    public bool UseRecoveryCode { get; init; }
}

/// <summary>
/// Repeats the identity proof for the one action that replaces the installation. The backup
/// password is not part of this request because the archive was already decrypted during the
/// check, and its ticket is bound to this session.
/// </summary>
public sealed class BackupApplyRequest
{
    public required string CurrentPassword { get; init; }

    public string? Code { get; init; }

    public bool UseRecoveryCode { get; init; }
}

/// <summary>
/// Answer to a confirmed import. RestartRequired indicates the host restart that is now queued;
/// the application performs it automatically after this response, including under an IDE.
/// </summary>
public sealed record BackupApplyResultDto(
    string OperationId,
    DateTimeOffset AppliedAt,
    bool RestartRequired);
