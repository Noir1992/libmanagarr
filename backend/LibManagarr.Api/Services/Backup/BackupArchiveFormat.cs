namespace LibManagarr.Api.Services.Backup;

/// <summary>
/// Fixed on-disk contract of the .lmbak container. The plaintext header carries only what a
/// reader needs to identify the file and derive its keys; everything else, including the
/// manifest, lives inside the authenticated payload. Changing any of these constants breaks
/// previously exported backups, so a new reader path needs a new <see cref="FormatVersion"/>.
/// </summary>
public static class BackupArchiveFormat
{
    public const string FileExtension = ".lmbak";

    public const string DownloadContentType = "application/octet-stream";

    /// <summary>Bytes before the variable-length salt, always present at offset zero.</summary>
    public static ReadOnlySpan<byte> Magic => "LMBK"u8;

    public const ushort FormatVersion = 2;

    public const byte CipherSuiteAesGcm256 = 1;

    public const byte KeyDerivationPbkdf2Sha512 = 1;

    public const int SaltLength = 32;

    public const int KdfOutputLength = 64;

    public const int AesKeyLength = 32;

    public const int AesGcmNonceLength = 12;

    /// <summary>Random header bytes at the start of every chunk nonce; the rest is the chunk counter.</summary>
    public const int NoncePrefixLength = 4;

    public const int AesGcmTagLength = 16;

    public const int HmacTagLength = 32;

    /// <summary>Plaintext bytes per encrypted payload chunk.</summary>
    public const int ChunkSize = 1 << 20;

    public const int MinIterations = 150_000;

    public const int DefaultIterations = 600_000;

    public const int MaxIterations = 2_000_000;

    /// <summary>Compressed container size accepted on upload.</summary>
    public const long MaxArchiveBytes = 512L * 1024 * 1024;

    /// <summary>Plaintext bytes the payload may expand to.</summary>
    public const long MaxUncompressedBytes = 4L * 1024 * 1024 * 1024;

    public const int MaxArchiveEntries = 2_000;

    /// <summary>Deflate ratio guard against decompression bombs.</summary>
    public const int MaxCompressionRatio = 200;

    public const int MinBackupPasswordLength = 12;

    public const int MaxBackupPasswordLength = 512;

    /// <summary>Header length prefix written before each payload chunk.</summary>
    public const int ChunkLengthPrefixLength = 4;

    /// <summary>Fixed-size header region: magic, version, suite, kdf id, iterations, salt length.</summary>
    public const int FixedHeaderLength = 4 + 2 + 1 + 1 + 4 + 1;

    public static string BuildDownloadFileName(DateTimeOffset createdAt) =>
        $"libmanagarr-backup-{createdAt.ToLocalTime():yyyy-MM-dd-HHmmss}{FileExtension}";
}
