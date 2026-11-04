using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibManagarr.Api.Services.Backup;

/// <summary>
/// Inventory of one backup, stored inside the encrypted payload so its contents stay private.
/// Everything the restore path needs to decide about compatibility lives here: which application
/// wrote the file, which database schemas it holds and the checksums of every component.
/// </summary>
public sealed record BackupManifest
{
    public required ushort FormatVersion { get; init; }

    public required string ApplicationVersion { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Schema version of libmanagarr.db at capture time.</summary>
    public required int LibrarySchemaVersion { get; init; }

    public required IReadOnlyList<BackupManifestEntry> Entries { get; init; }

    public int SonarrInstanceCount { get; init; }

    public int RadarrInstanceCount { get; init; }

    public bool HasSeerrInstance { get; init; }
}

/// <summary>
/// Non secret counts shown in the import preview and recorded in the manifest. They are read
/// from the staged copies only, so previewing a backup never normalizes, migrates or otherwise
/// touches the settings of the running installation.
/// </summary>
public sealed record BackupSettingsSummary(
    int SonarrInstanceCount, int RadarrInstanceCount, bool HasSeerrInstance)
{
    public static readonly BackupSettingsSummary Empty = new(0, 0, false);
}

public sealed record BackupManifestEntry
{
    /// <summary>Archive relative path using forward slashes.</summary>
    public required string Path { get; init; }

    public required long SizeBytes { get; init; }

    /// <summary>Lowercase hex SHA-256 over the stored file bytes.</summary>
    public required string Sha256 { get; init; }
}

/// <summary>
/// Reads and writes the manifest document. The shape is versioned by the archive format itself,
/// so a future format bump adds a reader here instead of tolerating unknown fields silently.
/// </summary>
internal static class BackupManifestSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    public const string EntryName = "manifest.json";

    public static byte[] Serialize(BackupManifest manifest) => JsonSerializer.SerializeToUtf8Bytes(manifest, Options);

    public static BackupManifest Deserialize(ReadOnlySpan<byte> bytes)
    {
        BackupManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<BackupManifest>(bytes, Options);
        }
        catch (JsonException exception)
        {
            throw new BackupException(BackupErrorCodes.CorruptArchive, "The backup manifest could not be read.", exception);
        }

        if (manifest is null || manifest.Entries.Count == 0)
        {
            throw new BackupException(BackupErrorCodes.CorruptArchive, "The backup manifest carries no entries.");
        }

        if (manifest.FormatVersion != BackupArchiveFormat.FormatVersion)
        {
            throw new BackupException(BackupErrorCodes.UnsupportedFormat,
                $"The backup was written in format version {manifest.FormatVersion}; this build reads version {BackupArchiveFormat.FormatVersion}.");
        }

        return manifest;
    }
}
