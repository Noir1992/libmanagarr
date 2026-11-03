namespace LibManagarr.Api.Services.Backup;

/// <summary>
/// The exact set of paths a backup may contain. Anything else is rejected before it touches the
/// filesystem, which is what keeps a crafted archive from writing outside the staging directory.
/// </summary>
internal static class BackupComponentNames
{
    public const string Settings = "settings.json";

    public const string LibraryDatabase = "libmanagarr.db";

    /// <summary>
    /// Validates an archive relative path and maps it onto a path inside the staging root.
    /// Absolute paths, parent traversal, backslashes, empty segments, duplicate separators and
    /// unexpected files all fail closed.
    /// </summary>
    public static bool TryResolve(string archivePath, string stagingRoot, out string destination)
    {
        destination = string.Empty;
        if (!IsAllowed(archivePath))
        {
            return false;
        }

        string root = Path.GetFullPath(stagingRoot + Path.DirectorySeparatorChar.ToString());
        string candidate = Path.GetFullPath(Path.Combine(root, archivePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root, StringComparison.Ordinal))
        {
            return false;
        }

        destination = candidate;
        return true;
    }

    public static bool IsAllowed(string archivePath)
    {
        if (string.IsNullOrEmpty(archivePath) ||
            archivePath.Contains('\\') ||
            archivePath.Contains(':') ||
            archivePath.StartsWith("/", StringComparison.Ordinal) ||
            archivePath.EndsWith("/", StringComparison.Ordinal) ||
            archivePath.Contains("//", StringComparison.Ordinal))
        {
            return false;
        }

        string[] segments = archivePath.Split('/');
        if (segments.Any((segment) => segment.Length == 0 || segment == "." || segment == ".."))
        {
            return false;
        }

        if (segments.Length == 1)
        {
            return segments[0] is Settings or LibraryDatabase or "manifest.json";
        }

        return false;
    }
}
