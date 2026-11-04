using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace LibManagarr.Api.Services.Backup;

/// <summary>
/// Reads and writes the .lmbak container. The payload is a ZIP archive encrypted in fixed size
/// AES-256-GCM chunks, so a large library database streams through a fixed buffer instead of
/// landing in memory, and one altered byte fails authenticated decryption before anything is
/// extracted. Reads always land in a staging file, never in the live config directory.
/// </summary>
internal static class BackupArchiveCodec
{
    public static async Task WriteAsync(string archivePath, string password, string stagingRoot, CancellationToken cancellationToken)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(BackupArchiveFormat.SaltLength);
        byte[] noncePrefix = RandomNumberGenerator.GetBytes(BackupArchiveFormat.NoncePrefixLength);
        using BackupKeyMaterial key = BackupKeyMaterial.Derive(password, salt, BackupArchiveFormat.DefaultIterations);

        await using FileStream archive = new(
            archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BackupArchiveFormat.ChunkSize, FileOptions.Asynchronous);
        byte[] headerTag = WriteHeader(archive, key, salt, noncePrefix, BackupArchiveFormat.DefaultIterations);

        // Disposing the archive before the chunk writer is what writes the ZIP central directory
        // into the payload; the final authenticated chunk must close after it.
        await using (BackupChunkWriter payload = new(archive, key, headerTag, noncePrefix))
        {
            await ZipFile.CreateFromDirectoryAsync(stagingRoot, payload, CompressionLevel.Optimal, includeBaseDirectory: false, cancellationToken);
        }

        await archive.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Decrypts the payload into a staging ZIP and returns the uncompressed byte count. Both size
    /// ceilings are enforced while reading, so an archive that only explodes after decryption
    /// still stops at the limit instead of filling the disk.
    /// </summary>
    public static async Task<long> ReadAsync(string archivePath, string password, string destinationZipPath, CancellationToken cancellationToken)
    {
        await using FileStream archive = new(
            archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, BackupArchiveFormat.ChunkSize, FileOptions.Asynchronous);
        if (archive.Length > BackupArchiveFormat.MaxArchiveBytes)
        {
            throw new BackupException(BackupErrorCodes.SizeLimitExceeded,
                $"The backup file exceeds the limit of {BackupArchiveFormat.MaxArchiveBytes / (1024 * 1024)} MB.");
        }

        BackupArchiveHeader header = ReadHeader(archive);
        using BackupKeyMaterial key = BackupKeyMaterial.Derive(password, header.Salt, header.Iterations);
        if (!key.VerifyHeaderTag(header.Raw, header.Tag))
        {
            throw new BackupException(BackupErrorCodes.InvalidPassword, "The backup password is wrong or the file was altered.");
        }

        byte[] headerTag = key.SignHeader(header.Raw);
        await using FileStream destination = new(
            destinationZipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BackupArchiveFormat.ChunkSize, FileOptions.Asynchronous);
        return await BackupChunkReader.ReadAsync(archive, destination, key, headerTag, header.NoncePrefix, cancellationToken);
    }

    private static byte[] WriteHeader(Stream archive, BackupKeyMaterial key, byte[] salt, byte[] noncePrefix, int iterations)
    {
        byte[] header = new byte[BackupArchiveFormat.FixedHeaderLength + salt.Length + noncePrefix.Length];
        int offset = 0;
        BackupArchiveFormat.Magic.CopyTo(header.AsSpan(offset));
        offset += BackupArchiveFormat.Magic.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(offset), BackupArchiveFormat.FormatVersion);
        offset += 2;
        header[offset++] = BackupArchiveFormat.CipherSuiteAesGcm256;
        header[offset++] = BackupArchiveFormat.KeyDerivationPbkdf2Sha512;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(offset), (uint)iterations);
        offset += 4;
        header[offset++] = (byte)salt.Length;
        salt.CopyTo(header.AsSpan(offset));
        offset += salt.Length;
        noncePrefix.CopyTo(header.AsSpan(offset));

        byte[] tag = key.SignHeader(header);
        byte[] complete = new byte[header.Length + tag.Length];
        header.CopyTo(complete, 0);
        tag.CopyTo(complete, header.Length);
        archive.Write(complete);
        return tag;
    }

    /// <summary>
    /// Structural header checks only. The salt, iteration count and cipher identifiers are read
    /// unauthenticated by necessity, so every value is bounded here and the header itself is
    /// authenticated against the caller password before the payload is touched.
    /// </summary>
    private static BackupArchiveHeader ReadHeader(Stream archive)
    {
        int fixedLength = BackupArchiveFormat.FixedHeaderLength + BackupArchiveFormat.SaltLength + BackupArchiveFormat.NoncePrefixLength;
        byte[] header = new byte[fixedLength];
        archive.ReadExactly(header);

        if (!header.AsSpan(0, BackupArchiveFormat.Magic.Length).SequenceEqual(BackupArchiveFormat.Magic))
        {
            throw new BackupException(BackupErrorCodes.CorruptArchive, "The file is not a LibManagarr backup.");
        }

        int offset = BackupArchiveFormat.Magic.Length;
        ushort formatVersion = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(offset));
        if (formatVersion != BackupArchiveFormat.FormatVersion)
        {
            throw new BackupException(BackupErrorCodes.UnsupportedFormat,
                $"The backup was written in format version {formatVersion}; this build reads version {BackupArchiveFormat.FormatVersion}.");
        }

        offset += 2;
        if (header[offset++] != BackupArchiveFormat.CipherSuiteAesGcm256 || header[offset++] != BackupArchiveFormat.KeyDerivationPbkdf2Sha512)
        {
            throw new BackupException(BackupErrorCodes.UnsupportedFormat, "The backup uses an unsupported cipher or key derivation.");
        }

        uint iterations = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(offset));
        if (iterations is < BackupArchiveFormat.MinIterations or > BackupArchiveFormat.MaxIterations)
        {
            throw new BackupException(BackupErrorCodes.CorruptArchive, "The backup declares an unusable key derivation cost.");
        }

        offset += 4;
        if (header[offset++] != BackupArchiveFormat.SaltLength)
        {
            throw new BackupException(BackupErrorCodes.CorruptArchive, "The backup declares an invalid salt length.");
        }

        byte[] salt = new byte[BackupArchiveFormat.SaltLength];
        Buffer.BlockCopy(header, offset, salt, 0, salt.Length);
        offset += salt.Length;
        byte[] noncePrefix = new byte[BackupArchiveFormat.NoncePrefixLength];
        Buffer.BlockCopy(header, offset, noncePrefix, 0, noncePrefix.Length);

        byte[] tag = new byte[BackupArchiveFormat.HmacTagLength];
        archive.ReadExactly(tag);
        return new BackupArchiveHeader(header, salt, noncePrefix, tag, (int)iterations);
    }

    private sealed record BackupArchiveHeader(byte[] Raw, byte[] Salt, byte[] NoncePrefix, byte[] Tag, int Iterations);
}

/// <summary>
/// Write side of the chunk framing: length prefixed ciphertext plus its GCM tag, one chunk per
/// full buffer, closed by an authenticated empty final chunk. Seeking is deliberately absent
/// because the payload is written once, forward.
/// </summary>
internal sealed class BackupChunkWriter(Stream output, BackupKeyMaterial key, byte[] headerTag, byte[] noncePrefix) : Stream
{
    private readonly byte[] pending = new byte[BackupArchiveFormat.ChunkSize];
    private readonly byte[] lengthPrefix = new byte[BackupArchiveFormat.ChunkLengthPrefixLength];
    private int buffered;
    private long chunkIndex;
    private bool closed;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => !closed;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (closed)
        {
            throw new BackupException(BackupErrorCodes.CorruptArchive, "The backup payload was already closed.");
        }

        int consumed = 0;
        while (consumed < buffer.Length)
        {
            int take = Math.Min(BackupArchiveFormat.ChunkSize - buffered, buffer.Length - consumed);
            buffer.Slice(consumed, take).CopyTo(pending.AsSpan(buffered));
            buffered += take;
            consumed += take;
            if (buffered == BackupArchiveFormat.ChunkSize)
            {
                EmitChunk(buffered, isFinal: false);
            }
        }
    }

    public override void Flush()
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !closed)
        {
            if (buffered > 0)
            {
                EmitChunk(buffered, isFinal: false);
            }

            EmitChunk(0, isFinal: true);
            closed = true;
        }

        base.Dispose(disposing);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    private void EmitChunk(int length, bool isFinal)
    {
        byte[] nonce = BackupKeyMaterial.BuildChunkNonce(noncePrefix, chunkIndex, isFinal);
        byte[] additionalData = BackupKeyMaterial.BuildChunkAdditionalData(headerTag, chunkIndex, isFinal);
        byte[] ciphertext = new byte[length];
        byte[] tag = new byte[BackupArchiveFormat.AesGcmTagLength];
        using (AesGcm aes = new(key.ContentKey, BackupArchiveFormat.AesGcmTagLength))
        {
            aes.Encrypt(nonce, pending.AsSpan(0, length), ciphertext, tag, additionalData);
        }

        BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, ciphertext.Length);
        output.Write(lengthPrefix);
        output.Write(ciphertext);
        output.Write(tag);
        chunkIndex++;
        buffered = 0;
    }
}

/// <summary>
/// Read side of the chunk framing. A wrong password surfaces as an authentication failure on the
/// first chunk; a truncated, reordered or replayed payload fails because every chunk is bound to
/// its own index and only the empty chunk is allowed to end the payload.
/// </summary>
internal static class BackupChunkReader
{
    public static async Task<long> ReadAsync(
        Stream archive, Stream destination, BackupKeyMaterial key, byte[] headerTag, byte[] noncePrefix, CancellationToken cancellationToken)
    {
        byte[] lengthPrefix = new byte[BackupArchiveFormat.ChunkLengthPrefixLength];
        byte[] tag = new byte[BackupArchiveFormat.AesGcmTagLength];
        long chunkIndex = 0;
        long total = 0;

        while (true)
        {
            await archive.ReadExactlyAsync(lengthPrefix, cancellationToken);
            int length = BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);
            if (length < 0 || length > BackupArchiveFormat.ChunkSize)
            {
                throw new BackupException(BackupErrorCodes.CorruptArchive, "The backup payload declares an impossible chunk size.");
            }

            bool isFinal = length == 0;
            byte[] ciphertext = new byte[length];
            if (length > 0)
            {
                await archive.ReadExactlyAsync(ciphertext, cancellationToken);
            }

            await archive.ReadExactlyAsync(tag, cancellationToken);
            byte[] nonce = BackupKeyMaterial.BuildChunkNonce(noncePrefix, chunkIndex, isFinal);
            byte[] additionalData = BackupKeyMaterial.BuildChunkAdditionalData(headerTag, chunkIndex, isFinal);
            byte[] plaintext = new byte[length];
            try
            {
                using AesGcm aes = new(key.ContentKey, BackupArchiveFormat.AesGcmTagLength);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, additionalData);
            }
            catch (CryptographicException exception)
            {
                throw new BackupException(BackupErrorCodes.InvalidPassword, "The backup password is wrong or the file was altered.", exception);
            }

            if (isFinal)
            {
                return total;
            }

            total += plaintext.Length;
            if (total > BackupArchiveFormat.MaxUncompressedBytes)
            {
                throw new BackupException(BackupErrorCodes.SizeLimitExceeded, "The backup expands beyond the allowed size.");
            }

            await destination.WriteAsync(plaintext, cancellationToken);
            chunkIndex++;
        }
    }
}
