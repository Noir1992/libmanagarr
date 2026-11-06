namespace LibManagarr.Api.Services;

/// <summary>
/// The few file-system operations a managed file symlink needs, kept separate from the operating
/// system so the error paths around a link — missing entry, denied rights, unsupported platform — are
/// reproducible on any development machine. Real symlink semantics are still proven against a real
/// file system in their own test stage; a fake never stands in for that.
/// </summary>
public interface ISymlinkFileSystem
{
    /// <summary>
    /// Creates a file symlink at <paramref name="linkPath"/> pointing at <paramref name="targetPath"/>.
    /// Throws <see cref="IOException"/> when the entry is already taken and
    /// <see cref="UnauthorizedAccessException"/> or <see cref="PlatformNotSupportedException"/> when the
    /// environment refuses, which is exactly what the capability probe has to tell apart.
    /// </summary>
    void CreateFileSymlink(string linkPath, string targetPath);

    /// <summary>True for any directory entry, including a link whose target is gone and an empty name.</summary>
    bool EntryExists(string path);

    bool IsSymlink(string path);

    /// <summary>The stored link content, or null when the entry is not a link.</summary>
    string? ReadLinkTarget(string path);

    /// <summary>Reads through the link, which is what proves the target is really reachable.</summary>
    byte[] ReadAllBytes(string path);

    /// <summary>
    /// Opens the path for reading and closes it again without reading it. A media file is never read to
    /// the end here, so this is the affordable way to tell "the entry is listed" apart from "the rights
    /// of this process can actually read through it", which the health check has to report separately.
    /// Throws the same exceptions the other operations do.
    /// </summary>
    void OpenForRead(string path);

    void WriteAllBytes(string path, byte[] content);

    /// <summary>Removes this entry only. A link is deleted as a link, never by following it.</summary>
    void DeleteEntry(string path);

    void MoveEntry(string sourcePath, string destinationPath);

    void CreateDirectory(string path);

    /// <summary>Lists one directory; used for the root reachability check without walking the tree.</summary>
    IReadOnlyList<string> ListEntries(string directoryPath);
}
