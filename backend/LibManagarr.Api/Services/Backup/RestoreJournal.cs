using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibManagarr.Api.Services.Backup;

/// <summary>
/// Recovery journal of one restore. Replacing several files cannot be one atomic filesystem
/// operation, so every step is written down before it happens: a crash between two moves leaves
/// enough information to either finish the takeover or undo it completely. The journal is the
/// only signal a fresh process reads to decide what to do.
/// </summary>
public enum RestoreJournalState
{
    /// <summary>Imported files are staged and ready; nothing has been replaced yet.</summary>
    Prepared,

    /// <summary>Files were moved out of the config directory into the rollback area.</summary>
    MovedAside,

    /// <summary>Imported files were moved into place; verification still has to pass.</summary>
    TakenOver,

    /// <summary>Restore finished and the application started from the imported state.</summary>
    Completed,

    /// <summary>The previous state was restored and the import was abandoned.</summary>
    RolledBack
}

/// <summary>
/// One component of a restore: the staged file or directory and where it belongs. Keeping the
/// pairs in the journal makes the takeover resumable, because a half moved component can always
/// be put back exactly where it came from.
/// </summary>
public sealed record RestoreComponent
{
    public required string SourcePath { get; init; }

    public required string TargetPath { get; init; }

    /// <summary>
    /// Retained to detect and reject older restore orders containing directories.
    /// Current restores contain only settings and the library database.
    /// </summary>
    public bool IsDirectory { get; init; }

    /// <summary>Path the previous version was moved to, empty until it was moved aside.</summary>
    public string? RollbackPath { get; init; }

    public bool MovedIntoPlace { get; init; }
}

/// <summary>
/// Durable record of a prepared restore, written before the application shuts down. It binds the
/// staged files to an operation id and to the hashes that were verified during the preview, so a
/// file swapped after the preview is detected before anything is replaced.
/// </summary>
public sealed record RestoreJournal
{
    public required string OperationId { get; init; }

    public required RestoreJournalState State { get; init; }

    public required DateTimeOffset PreparedAt { get; init; }

    /// <summary>Lowercase hex SHA-256 over the original uploaded archive.</summary>
    public required string ArchiveSha256 { get; init; }

    public required string StagingDirectory { get; init; }

    public required string RollbackDirectory { get; init; }

    public required IReadOnlyList<RestoreComponent> Components { get; init; }
}

/// <summary>
/// Reads and writes the journal file. Every write is atomic, because a journal torn in half by a
/// power loss would leave the next start unable to tell a finished restore from a broken one.
/// </summary>
public sealed class RestoreJournalStore(StoragePaths paths)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };

    public string JournalPath => Path.Combine(paths.ConfigDirectory, "restore-journal.json");

    public RestoreJournal? Read()
    {
        if (!File.Exists(JournalPath))
        {
            return null;
        }

        try
        {
            RestoreJournal? journal = JsonSerializer.Deserialize<RestoreJournal>(File.ReadAllBytes(JournalPath), Options);
            return journal is null || journal.Components.Count == 0 ? null : journal;
        }
        catch (JsonException)
        {
            // A damaged journal must not guess. Without a usable journal the start proceeds and
            // leaves the untouched config directory alone.
            return null;
        }
    }

    public void Write(RestoreJournal journal)
    {
        // The journal is the one file a restart has to find, so it is written even when nothing
        // created the config directory yet in this process.
        Directory.CreateDirectory(paths.ConfigDirectory);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(journal, Options);
        string temporary = JournalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, JournalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public void Delete()
    {
        if (File.Exists(JournalPath))
        {
            File.Delete(JournalPath);
        }
    }

    public RestoreJournal WithState(RestoreJournal journal, RestoreJournalState state, IReadOnlyList<RestoreComponent>? components = null)
    {
        RestoreJournal updated = journal with
        {
            State = state,
            Components = components ?? journal.Components
        };
        Write(updated);
        return updated;
    }
}
