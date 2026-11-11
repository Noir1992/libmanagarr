namespace LibManagarr.Api.Services;

/// <summary>Checks every existing ancestor before an operation. Linked directories are deliberately
/// refused, including junctions: a lexical root check cannot constrain where they lead.</summary>
public static class SymlinkPathGuard
{
    public static bool ResolvesTo(ISymlinkFileSystem files, string linkPath, string sourcePath)
    {
        try
        {
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(FinalPath(files, linkPath), FinalPath(files, sourcePath), comparison);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { return false; }
    }

    private static string FinalPath(ISymlinkFileSystem files, string path)
    {
        path = Path.GetFullPath(path);
        for (int depth = 0; depth <= 40; depth++)
        {
            CheckParents(files, path);
            if (!files.IsSymlink(path)) return path;
            string target = files.ReadLinkTarget(path) ?? throw new IOException("The link target cannot be read.");
            path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, target));
        }
        throw new IOException("The link chain is cyclic or too long.");
    }

    public static void CheckParents(ISymlinkFileSystem files, string path)
    {
        string? parent = Path.GetDirectoryName(Path.GetFullPath(path));
        while (!string.IsNullOrEmpty(parent))
        {
            if (files.IsSymlink(parent))
            {
                throw new IOException($"The parent directory '{parent}' is a symbolic link or junction. Use a media root without linked parent directories.");
            }

            parent = Path.GetDirectoryName(parent);
        }
    }
}
