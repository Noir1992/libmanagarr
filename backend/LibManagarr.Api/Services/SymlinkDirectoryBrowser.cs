using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public sealed record SymlinkDirectory(string Name, string Path);
public sealed record SymlinkDirectoryListing(string? Path, string? ParentPath, bool CanSelect,
    IReadOnlyList<SymlinkDirectory> Directories, bool Truncated);

/// <summary>Lists directories visible to this server without opening files or following linked directories.</summary>
public static class SymlinkDirectoryBrowser
{
    private const int Limit = 1000;

    public static SymlinkDirectoryListing Browse(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            if (OperatingSystem.IsWindows())
                return new(null, null, false, Directory.GetLogicalDrives()
                    .Select(root => new SymlinkDirectory(root, root)).ToArray(), false);
            // Unix logical drives include nested mounts, not just the current directory level.
            path = "/";
        }

        if (!ArrPathRules.TryParse(path, out ParsedPath? parsed, out string? failure)
            || parsed!.Syntax != ArrPathRules.RuntimeSyntax)
            throw new ApiProblemException(400, "Invalid directory", failure ?? "Use an absolute path on this server.");

        try
        {
            string fullPath = System.IO.Path.GetFullPath(parsed.Format());
            SymlinkPathGuard.CheckParents(new RealSymlinkFileSystem(), System.IO.Path.Combine(fullPath, ".browse"));
            DirectoryInfo directory = new(fullPath);
            if (!directory.Exists) throw new ApiProblemException(404, "Directory not found", "This directory is not available on the server.");
            SymlinkDirectory[] children = directory.EnumerateDirectories()
                .Where(child => (child.Attributes & FileAttributes.ReparsePoint) == 0)
                .Take(Limit + 1).Select(child => new SymlinkDirectory(child.Name, child.FullName)).ToArray();
            return new(fullPath, directory.Parent?.FullName, parsed.Segments.Count > 0,
                children.Take(Limit).OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(child => child.Name, StringComparer.Ordinal).ToArray(), children.Length > Limit);
        }
        catch (UnauthorizedAccessException)
        { throw new ApiProblemException(403, "Directory inaccessible", "The server does not have permission to browse this directory."); }
        catch (IOException exception)
        { throw new ApiProblemException(400, "Directory unavailable", exception.Message); }
    }
}
