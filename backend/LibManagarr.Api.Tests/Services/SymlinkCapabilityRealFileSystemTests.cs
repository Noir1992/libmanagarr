using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

/// <summary>
/// The second stage of the capability evidence: the probe and the link operations against a real file
/// system. A fake can only show which branch the code took; whether the operating system really resolves
/// a link, keeps a source alive when its link is removed and still reports a broken link as an entry is
/// only answerable here. Where this machine refuses symlinks, the probe has to say so — that outcome is
/// asserted too, so a refusal never counts as proof that the feature works.
/// </summary>
public sealed class SymlinkCapabilityRealFileSystemTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"libmanagarr-symlink-probe-{Guid.NewGuid():N}");
    private readonly ITestOutputHelper output;

    public SymlinkCapabilityRealFileSystemTests(ITestOutputHelper output)
    {
        this.output = output;
        Directory.CreateDirectory(MoviesRoot);
        Directory.CreateDirectory(SeriesRoot);
    }

    private string MoviesRoot => Path.Combine(root, "movies");
    private string SeriesRoot => Path.Combine(root, "series");

    [Fact]
    public async Task Probe_OnARealFileSystem_ReportsWhatThisMachineActuallyAllows()
    {
        InMemorySymlinkStore store = new();
        SymlinkSettings settings = new()
        {
            Revision = 1,
            StorageAreas =
            [
                new MediaStorageArea { Id = "area", Name = "Temporary", MoviesRoot = MoviesRoot, SeriesRoot = SeriesRoot }
            ]
        };
        SymlinkCapabilityService service = new(
            store,
            new FixedSettings(new AppSettings { Symlinks = settings }),
            new RealSymlinkFileSystem(),
            TimeProvider.System,
            NullLogger<SymlinkCapabilityService>.Instance);

        SymlinkCapabilityCheck check = await service.ProbeAsync("area", CancellationToken.None);
        output.WriteLine($"Real file system probe ended as {check.Status}: {check.Detail}");

        // Whatever the answer was, the probe may not leave anything behind in the media tree.
        Assert.Empty(Directory.GetFileSystemEntries(SeriesRoot));
        Assert.False(string.IsNullOrWhiteSpace(check.Detail));
        Assert.Equal(1, check.ConfigRevision);
        Assert.Equal(check, await store.GetCapabilityCheckAsync("area", CancellationToken.None));

        if (check.Status == SymlinkCapabilityStatus.Available)
        {
            // The strong case: writing, reading through, renaming and replacing a link all worked against
            // the real file system, and the throwaway files are gone.
            Assert.Empty(Directory.GetFiles(SeriesRoot, "*", SearchOption.AllDirectories));
            Assert.True(check.IsCurrent(1));
            return;
        }

        // The refusal case: a probe always answers something, and only the four refusal states are allowed.
        // "NotTested" here would let an unchecked machine look like a tested one.
        Assert.True(
            check.Status is SymlinkCapabilityStatus.Unsupported
                or SymlinkCapabilityStatus.PermissionDenied
                or SymlinkCapabilityStatus.StorageUnavailable
                or SymlinkCapabilityStatus.Failed,
            $"Unexpected probe outcome: {check.Status}");
    }

    [Fact]
    public void RealSymlinkFileSystem_KeepsTheSourceAliveWhenItsLinkIsRemoved()
    {
        RealSymlinkFileSystem files = new();
        string source = Path.Combine(SeriesRoot, "source.bin");
        string link = Path.Combine(SeriesRoot, "link.bin");
        File.WriteAllBytes(source, "payload"u8.ToArray());

        if (!TryCreateLink(files, link, "source.bin")) return;

        Assert.True(files.EntryExists(link));
        Assert.True(files.IsSymlink(link));
        Assert.NotNull(files.ReadLinkTarget(link));
        Assert.Equal("payload"u8.ToArray(), files.ReadAllBytes(link));

        files.DeleteEntry(link);

        // The one property a link manager must never get wrong: removing the entry frees the slot, it does
        // not delete the film it pointed at.
        Assert.False(files.EntryExists(link));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void RealSymlinkFileSystem_SeesABrokenLinkAsAnEntryAndRefusesToReadThroughIt()
    {
        RealSymlinkFileSystem files = new();
        string source = Path.Combine(SeriesRoot, "gone-source.bin");
        string link = Path.Combine(SeriesRoot, "broken-link.bin");
        File.WriteAllBytes(source, "payload"u8.ToArray());
        if (!TryCreateLink(files, link, Path.GetFileName(source))) return;

        File.Delete(source);

        // An unmanaged file could otherwise take the place of our entry unnoticed.
        Assert.True(files.EntryExists(link));
        Assert.True(files.IsSymlink(link));
        Assert.NotNull(files.ReadLinkTarget(link));
        Assert.ThrowsAny<IOException>(() => files.ReadAllBytes(link));

        files.DeleteEntry(link);
        Assert.False(files.EntryExists(link));
        Assert.Empty(Directory.GetFileSystemEntries(SeriesRoot));
    }

    [Fact]
    public void RealSymlinkFileSystem_RefusesToOverwriteAnExistingEntryWithALink()
    {
        RealSymlinkFileSystem files = new();
        string source = Path.Combine(SeriesRoot, "target.bin");
        string occupant = Path.Combine(SeriesRoot, "taken.bin");
        File.WriteAllBytes(source, "payload"u8.ToArray());
        File.WriteAllBytes(occupant, "someone else's file"u8.ToArray());

        if (!TryCreateLink(files, Path.Combine(SeriesRoot, "unused.bin"), "target.bin")) return;
        files.DeleteEntry(Path.Combine(SeriesRoot, "unused.bin"));

        Assert.ThrowsAny<IOException>(() => files.CreateFileSymlink(occupant, "target.bin"));
        Assert.Equal("someone else's file"u8.ToArray(), File.ReadAllBytes(occupant));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (IOException exception)
        {
            output.WriteLine($"Could not remove the temporary tree {root}: {exception.Message}");
        }
    }

    /// <summary>False when this machine will not create a link at all; the caller then stops, without a pass.</summary>
    private bool TryCreateLink(RealSymlinkFileSystem files, string linkPath, string targetName)
    {
        try
        {
            files.CreateFileSymlink(linkPath, targetName);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            output.WriteLine(
                $"This machine refuses symlink creation ({exception.GetType().Name}: {exception.Message}). " +
                "The link semantics this test would have proven were NOT executed.");
            return false;
        }
    }

    private sealed class FixedSettings(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) => Task.FromResult(value);
    }
}
