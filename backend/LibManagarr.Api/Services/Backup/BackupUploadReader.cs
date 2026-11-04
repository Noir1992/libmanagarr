using Microsoft.Extensions.Primitives;

namespace LibManagarr.Api.Services.Backup;

/// <summary>
/// An archive stored in the staging tree plus the password that came with it. The password exists
/// only here: it is never written to disk, logged or returned to the caller.
/// </summary>
public sealed record BackupUpload(string ArchivePath, string BackupPassword, long ArchiveBytes);

/// <summary>
/// Moves the two fields of an import request into the staging tree. Only the two field names this
/// service accepts are read, and the file name a client sends is never used: the bytes go to the
/// staging path chosen here and nowhere else. The size ceiling runs while copying, so an oversized
/// upload stops at the limit instead of filling the disk.
/// </summary>
public static class BackupUploadReader
{
    public const string FilePartName = "file";

    public const string PasswordPartName = "backupPassword";

    /// <summary>Room for the multipart framing on top of the archive itself.</summary>
    public const long BodyAllowance = 1L * 1024 * 1024;

    public static async Task<BackupUpload> ReadAsync(
        HttpRequest request, string stagingDirectory, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            throw new BackupException(BackupErrorCodes.UnsupportedFormat,
                "Send the backup file and the backup password as a multipart form request.")
            {
                StatusCode = StatusCodes.Status415UnsupportedMediaType
            };
        }

        IFormCollection form = await request.ReadFormAsync(cancellationToken);
        if (form.Files.Count != 1 || form.Files.GetFile(FilePartName) is not { } upload)
        {
            throw new BackupException(BackupErrorCodes.MissingComponent,
                $"The upload needs exactly one {FilePartName} file.")
            {
                StatusCode = form.Files.Count == 0 ? StatusCodes.Status400BadRequest : StatusCodes.Status422UnprocessableEntity
            };
        }

        StringValues passwordFields = form[PasswordPartName];
        if (passwordFields.Count > 1)
        {
            throw new BackupException(BackupErrorCodes.CorruptArchive, $"The upload carries more than one {PasswordPartName} field.");
        }

        if (passwordFields.Count == 0 || StringValues.IsNullOrEmpty(passwordFields))
        {
            throw new BackupException(BackupErrorCodes.InvalidPassword, $"The upload is missing the {PasswordPartName} field.");
        }

        string[] unexpectedFields = [.. form.Keys.Where((key) =>
            !string.Equals(key, FilePartName, StringComparison.Ordinal) &&
            !string.Equals(key, PasswordPartName, StringComparison.Ordinal))];
        if (unexpectedFields.Length > 0)
        {
            throw new BackupException(BackupErrorCodes.UnsafePath,
                $"The upload carries the unsupported form fields {string.Join(", ", unexpectedFields)}.");
        }

        string backupPassword = passwordFields.ToString()!;
        if (backupPassword.Length > BackupArchiveFormat.MaxBackupPasswordLength)
        {
            throw new BackupException(BackupErrorCodes.WeakPassword,
                $"The backup password exceeds {BackupArchiveFormat.MaxBackupPasswordLength} characters.");
        }

        string archivePath = Path.Combine(stagingDirectory, "upload" + BackupArchiveFormat.FileExtension);
        long archiveBytes;
        await using (Stream source = upload.OpenReadStream())
        {
            archiveBytes = await CopyBoundedAsync(source, archivePath, BackupArchiveFormat.MaxArchiveBytes, cancellationToken);
        }

        return new BackupUpload(archivePath, backupPassword, archiveBytes);
    }

    private static async Task<long> CopyBoundedAsync(Stream source, string destination, long limit, CancellationToken cancellationToken)
    {
        await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > limit)
            {
                throw new BackupException(BackupErrorCodes.SizeLimitExceeded,
                    $"The backup file exceeds the limit of {limit / (1024 * 1024)} MB.")
                {
                    StatusCode = StatusCodes.Status413PayloadTooLarge
                };
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        BackupStaging.ProtectFile(destination);
        return total;
    }
}
