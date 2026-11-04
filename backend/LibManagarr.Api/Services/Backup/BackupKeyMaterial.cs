using System.Buffers.Binary;
using System.Security.Cryptography;

namespace LibManagarr.Api.Services.Backup;

/// <summary>
/// Password based key material for one container. A single PBKDF2-SHA512 expansion yields both
/// the HMAC key that authenticates the plaintext header and the AES-256-GCM key that protects
/// the payload, so a tampered iteration count or salt never reaches the payload decryptor.
/// Nothing here depends on the source installation, which keeps a backup readable on a fresh one.
/// </summary>
internal sealed class BackupKeyMaterial : IDisposable
{
    private readonly byte[] headerKey;
    private readonly byte[] contentKey;

    private BackupKeyMaterial(byte[] headerKey, byte[] contentKey)
    {
        this.headerKey = headerKey;
        this.contentKey = contentKey;
    }

    public byte[] HeaderKey => headerKey;

    public byte[] ContentKey => contentKey;

    public static BackupKeyMaterial Derive(string password, byte[] salt, int iterations)
    {
        byte[] expanded = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA512,
            BackupArchiveFormat.KdfOutputLength);
        byte[] header = new byte[BackupArchiveFormat.AesKeyLength];
        byte[] content = new byte[BackupArchiveFormat.AesKeyLength];
        Buffer.BlockCopy(expanded, 0, header, 0, header.Length);
        Buffer.BlockCopy(expanded, BackupArchiveFormat.AesKeyLength, content, 0, content.Length);
        CryptographicOperations.ZeroMemory(expanded);
        return new BackupKeyMaterial(header, content);
    }

    public byte[] SignHeader(ReadOnlySpan<byte> headerBytes) => HMACSHA256.HashData(headerKey, headerBytes);

    public bool VerifyHeaderTag(ReadOnlySpan<byte> headerBytes, ReadOnlySpan<byte> tag) =>
        CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(headerKey, headerBytes), tag);

    /// <summary>
    /// Builds the per chunk nonce. The random prefix comes from the header, the counter is the
    /// chunk index, so no nonce is ever reused under the same key even across equal plaintexts.
    /// </summary>
    public static byte[] BuildChunkNonce(ReadOnlySpan<byte> prefix, long chunkIndex, bool isFinal)
    {
        byte[] nonce = new byte[BackupArchiveFormat.AesGcmNonceLength];
        prefix.CopyTo(nonce);
        BinaryPrimitives.WriteInt64LittleEndian(
            nonce.AsSpan(prefix.Length),
            isFinal ? -chunkIndex - 1 : chunkIndex);
        return nonce;
    }

    /// <summary>
    /// Binds each chunk to its position and to the final-chunk flag, so neither reordering,
    /// duplication nor flipping the end-of-payload marker can survive decryption.
    /// </summary>
    public static byte[] BuildChunkAdditionalData(ReadOnlySpan<byte> headerTag, long chunkIndex, bool isFinal)
    {
        byte[] aad = new byte[BackupArchiveFormat.HmacTagLength + 8 + 1];
        headerTag.CopyTo(aad);
        BinaryPrimitives.WriteInt64LittleEndian(
            aad.AsSpan(BackupArchiveFormat.HmacTagLength), isFinal ? -chunkIndex - 1 : chunkIndex);
        aad[^1] = (byte)(isFinal ? 1 : 0);
        return aad;
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(headerKey);
        CryptographicOperations.ZeroMemory(contentKey);
    }
}
