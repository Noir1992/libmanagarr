namespace LibManagarr.Api.Services;

/// <summary>
/// Symlink operations on the real file system. Entry checks deliberately do not use
/// <see cref="File.Exists"/>: that follows the link, so a broken link would look like a missing file
/// and a foreign file could be mistaken for our own entry.
/// </summary>
public sealed class RealSymlinkFileSystem : ISymlinkFileSystem
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public void CreateFileSymlink(string linkPath, string targetPath) => File.CreateSymbolicLink(linkPath, targetPath);

    public void ReplaceFileSymlink(string linkPath, string expectedLinkText, string newLinkText)
    {
        SymlinkPathGuard.CheckParents(this, linkPath);
        string temporary = linkPath + ".libmanagarr-" + Guid.NewGuid().ToString("N");
        try
        {
            File.CreateSymbolicLink(temporary, newLinkText);
            OpenForRead(temporary);
            if (ReadLinkTarget(linkPath) != expectedLinkText)
                throw new IOException("The previous link changed before replacement. Nothing was overwritten.");
            File.Move(temporary, linkPath, overwrite: true);
        }
        finally
        {
            if (ReadLinkTarget(temporary) == newLinkText) File.Delete(temporary);
        }
    }

    public bool EntryExists(string path)
    {
        try
        {
            File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            // A link whose target is gone cannot be stat'ed, but it is still a directory entry and has to
            // be reported as one, or a foreign file could silently take its place.
            return ParentHasEntry(path);
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    public bool IsSymlink(string path) => ReadLinkTarget(path) is not null
        || (EntryExists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0);

    public string? ReadLinkTarget(string path)
    {
        if (!EntryExists(path)) return null;
        try
        {
            // lstat based, so this also answers for a link that points at nothing.
            return new FileInfo(path).LinkTarget;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void OpenForRead(string path)
    {
        // ReadWrite sharing on purpose: a file an Arr instance is still writing is openable, and refusing
        // it here would report a healthy link as broken because of a download in progress.
        using FileStream handle = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    public void WriteAllBytes(string path, byte[] content) => File.WriteAllBytes(path, content);

    public void DeleteEntry(string path)
    {
        if (IsSymlink(path) || !Directory.Exists(path))
        {
            File.Delete(path);
            return;
        }

        // Never recursive: an unexpected directory below a media tree is not this call's to empty.
        Directory.Delete(path, recursive: false);
    }

    public void MoveEntry(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath, overwrite: false);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public IReadOnlyList<string> ListEntries(string directoryPath) => Directory.EnumerateFileSystemEntries(directoryPath).ToList();

    private static bool ParentHasEntry(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        string name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(parent) || name.Length == 0) return false;
        // Only a provably absent parent or entry answers "no". A directory that cannot be read keeps
        // throwing, because "not there" and "not readable" lead to different actions.
        try
        {
            return Directory.EnumerateFileSystemEntries(parent).Any(entry =>
                string.Equals(Path.GetFileName(entry), name, PathComparison));
        }
        catch (Exception exception) when (exception is DirectoryNotFoundException or FileNotFoundException)
        {
            return false;
        }
    }
}
