using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class RealSymlinkFactAttribute : FactAttribute
{
    public RealSymlinkFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) return; // Linux CI must fail if it cannot create links.
        string source = Path.Combine(Path.GetTempPath(), "libmanagarr-privilege-" + Guid.NewGuid().ToString("N"));
        string link = source + ".link";
        try { File.WriteAllText(source, "probe"); File.CreateSymbolicLink(link, source); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        { Skip = "Real symlink semantics were not executed: " + exception.Message; }
        finally { File.Delete(link); File.Delete(source); }
    }
}

public sealed class RealSymlinkHardeningTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "libmanagarr-real-links-" + Guid.NewGuid().ToString("N"));

    public RealSymlinkHardeningTests() => Directory.CreateDirectory(root);

    [RealSymlinkFact]
    public void AtomicReplacementKeepsBothOriginalFilesAndTheirTimestamps()
    {
        string first = Path.Combine(root, "v1.bin");
        string second = Path.Combine(root, "v2.bin");
        string link = Path.Combine(root, "managed.bin");
        File.WriteAllBytes(first, [1, 2, 3]);
        File.WriteAllBytes(second, [4, 5, 6]);
        DateTime firstTime = File.GetLastWriteTimeUtc(first), secondTime = File.GetLastWriteTimeUtc(second);
        RealSymlinkFileSystem files = new();
        files.CreateFileSymlink(link, "v1.bin");
        files.ReplaceFileSymlink(link, "v1.bin", "v2.bin");
        Assert.Equal("v2.bin", files.ReadLinkTarget(link));
        Assert.Equal(new byte[] { 4, 5, 6 }, files.ReadAllBytes(link));
        files.DeleteEntry(link);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(first));
        Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(second));
        Assert.Equal(firstTime, File.GetLastWriteTimeUtc(first));
        Assert.Equal(secondTime, File.GetLastWriteTimeUtc(second));
    }

    [RealSymlinkFact]
    public void ReplacementRefusesAForeignTargetAndCleansItsOwnTemporaryLink()
    {
        string source = Path.Combine(root, "source.bin");
        string link = Path.Combine(root, "managed.bin");
        File.WriteAllText(source, "original");
        RealSymlinkFileSystem files = new();
        files.CreateFileSymlink(link, "source.bin");
        Assert.Throws<IOException>(() => files.ReplaceFileSymlink(link, "different.bin", "source.bin"));
        Assert.Equal("source.bin", files.ReadLinkTarget(link));
        Assert.Equal(2, Directory.GetFileSystemEntries(root).Length);
        Assert.Equal("original", File.ReadAllText(source));
    }

    [RealSymlinkFact]
    public void ExistingDirectoryLinksCannotRedirectAMutationOutsideTheLexicalRoot()
    {
        string destination = Path.Combine(root, "outside");
        string linkedParent = Path.Combine(root, "series");
        Directory.CreateDirectory(destination);
        Directory.CreateSymbolicLink(linkedParent, destination);
        RealSymlinkFileSystem files = new();
        IOException error = Assert.Throws<IOException>(() => SymlinkPathGuard.CheckParents(files, Path.Combine(linkedParent, "special.mkv")));
        Assert.Contains(linkedParent, error.Message);
        Assert.Empty(Directory.GetFileSystemEntries(destination));
        Directory.Delete(linkedParent);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
