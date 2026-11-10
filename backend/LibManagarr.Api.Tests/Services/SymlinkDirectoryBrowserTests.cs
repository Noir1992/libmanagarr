using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class SymlinkDirectoryBrowserTests
{
    [Fact]
    public void Browse_ListsOnlyDirectoriesAndSuppliesParent()
    {
        string root = Path.Combine(Path.GetTempPath(), "libmanagarr-browser-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "series"));
            Directory.CreateDirectory(Path.Combine(root, "movies"));
            Directory.CreateDirectory(Path.Combine(root, "movies", "Example Movie"));
            File.WriteAllText(Path.Combine(root, "private.txt"), "not listed");
            SymlinkDirectoryListing listing = SymlinkDirectoryBrowser.Browse(root);
            Assert.Equal(root, listing.Path);
            Assert.Equal(Directory.GetParent(root)!.FullName, listing.ParentPath);
            Assert.True(listing.CanSelect);
            Assert.False(listing.Truncated);
            Assert.Equal(["movies", "series"], listing.Directories.Select(child => child.Name));
            Assert.All(listing.Directories, child => Assert.True(Path.IsPathFullyQualified(child.Path)));
            SymlinkDirectoryListing movies = SymlinkDirectoryBrowser.Browse(Path.Combine(root, "movies"));
            Assert.Equal("Example Movie", Assert.Single(movies.Directories).Name);
            Assert.Equal(root, movies.ParentPath);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Browse_LocationsAndFilesystemRootCannotBeSelected()
    {
        SymlinkDirectoryListing locations = SymlinkDirectoryBrowser.Browse(null);
        if (OperatingSystem.IsWindows()) Assert.Null(locations.Path);
        else
        {
            Assert.Equal("/", locations.Path);
            Assert.Null(locations.ParentPath);
            Assert.All(locations.Directories, child => Assert.Equal("/", Directory.GetParent(child.Path)!.FullName));
        }
        Assert.False(locations.CanSelect);
        Assert.NotEmpty(locations.Directories);
        string root = Path.GetPathRoot(Path.GetTempPath())!;
        SymlinkDirectoryListing listing = SymlinkDirectoryBrowser.Browse(root);
        Assert.False(listing.CanSelect);
        Assert.Null(listing.ParentPath);
    }

    [Fact]
    public void Browse_RefusesRelativeAndMissingPaths()
    {
        Assert.Equal(400, Assert.Throws<ApiProblemException>(() => SymlinkDirectoryBrowser.Browse("relative/path")).StatusCode);
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Assert.Equal(404, Assert.Throws<ApiProblemException>(() => SymlinkDirectoryBrowser.Browse(missing)).StatusCode);
    }
}
