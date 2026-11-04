namespace LibManagarr.Api.Services.Backup;

/// <summary>
/// Stable machine readable reasons a backup operation fails. The UI translates these keys, so
/// the wording in <c>title</c> stays for logs and only the code is part of the contract.
/// </summary>
public static class BackupErrorCodes
{
    public const string InvalidPassword = "backup_invalid_password";

    public const string CorruptArchive = "backup_corrupt_archive";

    public const string UnsupportedFormat = "backup_unsupported_format";

    public const string UnsupportedSchema = "backup_unsupported_schema";

    public const string UnsafePath = "backup_unsafe_path";

    public const string SizeLimitExceeded = "backup_size_limit_exceeded";

    public const string MissingComponent = "backup_missing_component";

    public const string ChecksumMismatch = "backup_checksum_mismatch";

    public const string IntegrityCheckFailed = "backup_integrity_check_failed";

    public const string InsufficientDiskSpace = "backup_insufficient_disk_space";

    public const string OperationInProgress = "backup_operation_in_progress";

    public const string WritersNotPaused = "backup_writers_not_paused";

    public const string ImportExpired = "backup_import_expired";

    public const string ImportUnknown = "backup_import_unknown";

    public const string ImportConflict = "backup_import_conflict";

    public const string IdentityVerificationFailed = "backup_identity_verification_failed";

    public const string RestorePending = "backup_restore_pending";

    public const string WeakPassword = "backup_weak_password";
}

/// <summary>
/// Failure raised by the backup services. It carries one of the <see cref="BackupErrorCodes"/>
/// values plus optional detail, and the endpoints turn it into a problem response without ever
/// echoing back archive contents or key material.
/// </summary>
public sealed class BackupException : Exception
{
    public BackupException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }

    public int StatusCode { get; init; } = StatusCodes.Status400BadRequest;

    public BackupException WithStatus(int statusCode) => new(Code, Message, InnerException) { StatusCode = statusCode };
}
