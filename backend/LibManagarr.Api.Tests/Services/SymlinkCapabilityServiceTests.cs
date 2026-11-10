using Microsoft.Extensions.Logging.Abstractions;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class SymlinkCapabilityServiceTests
{
    private const string AreaId = "area-1";
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 8, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider clock = new(Now);
    private readonly InMemorySymlinkStore store = new();

    [Fact]
    public async Task Probe_ProvesTheLinkInTheSeriesRootAndRecordsTheCurrentRevision()
    {
        FakeSymlinkFileSystem files = new(roots: Roots());
        SymlinkSettings settings = Settings(revision: 7);

        SymlinkCapabilityCheck check = await CreateService(files, settings).ProbeAsync(AreaId, CancellationToken.None);

        Assert.Equal(SymlinkCapabilityStatus.Available, check.Status);
        Assert.Equal(AreaId, check.StorageAreaId);
        Assert.Equal(7, check.ConfigRevision);
        Assert.Equal(Now, check.CheckedAt);
        Assert.Contains(SeriesRoot(), check.Detail);
        Assert.Contains(MoviesRoot(), check.Detail);
        Assert.True(check.IsCurrent(7));
        Assert.False(check.IsCurrent(8));
        Assert.Same(check, await store.GetCapabilityCheckAsync(AreaId, CancellationToken.None));
        // A probe that leaves its throwaway files in the media tree would look like library content.
        Assert.DoesNotContain(files.Directories, dir => dir.Contains(".libmanagarr-symlink-check-", StringComparison.Ordinal));
        Assert.Empty(files.Files);
    }

    [Fact]
    public async Task Probe_UsesTheConfiguredSeriesRootOfTheSelectedArea()
    {
        FakeSymlinkFileSystem files = new(roots: Roots());
        SymlinkSettings settings = Settings(revision: 1);
        settings.StorageAreas.Add(new MediaStorageArea
        {
            Id = "area-2",
            Name = "Second",
            MoviesRoot = LibManagarr("movies-two"),
            SeriesRoot = LibManagarr("series-two")
        });
        files.Directories.Add(LibManagarr("movies-two"));
        files.Directories.Add(LibManagarr("series-two"));

        SymlinkCapabilityCheck check = await CreateService(files, settings).ProbeAsync("area-2", CancellationToken.None);

        Assert.Equal(SymlinkCapabilityStatus.Available, check.Status);
        Assert.Contains(files.Created, path => path.StartsWith(LibManagarr("series-two"), StringComparison.OrdinalIgnoreCase));
        // The area that was not asked about must stay untouched, which a plain prefix test would not show:
        // "series-two" starts with "series".
        Assert.DoesNotContain(files.Created, path => ArrPathRules.IsWithinRoot(SeriesRoot(), path));
    }

    [Fact]
    public async Task Probe_ReportsADeniedRightAsDenialAndNotAsAMissingFeature()
    {
        FakeSymlinkFileSystem files = new(roots: Roots()) { CreateSymlinkThrows = new UnauthorizedAccessException("SeCreateSymbolicLinkPrivilege") };

        SymlinkCapabilityCheck check = await CreateService(files, Settings(revision: 2)).ProbeAsync(AreaId, CancellationToken.None);

        Assert.Equal(SymlinkCapabilityStatus.PermissionDenied, check.Status);
        Assert.Contains("denied", check.Detail);
    }

    [Fact]
    public async Task Probe_KeepsAnIoErrorDistinctFromAnUnsupportedPlatform()
    {
        FakeSymlinkFileSystem files = new(roots: Roots()) { CreateSymlinkThrows = new IOException("Read-only file system") };

        SymlinkCapabilityCheck check = await CreateService(files, Settings(revision: 2)).ProbeAsync(AreaId, CancellationToken.None);

        // The distinction is what tells the operator to fix a mount instead of giving up on the feature.
        Assert.Equal(SymlinkCapabilityStatus.Failed, check.Status);
        Assert.Contains("refused the operation", check.Detail);
    }

    [Fact]
    public async Task Probe_ReportsTheWindowsPrivilegeRefusalAsADeniedRight()
    {
        // Windows reports ERROR_PRIVILEGE_NOT_HELD as an ordinary IOException, so the code has to be what
        // keeps a grantable right out of the "broken file system" bucket.
        FakeSymlinkFileSystem files = new(roots: Roots())
        {
            CreateSymlinkThrows = new IOException("A required privilege is not held by the client.", unchecked((int)0x80070522))
        };

        SymlinkCapabilityCheck check = await CreateService(files, Settings(revision: 2)).ProbeAsync(AreaId, CancellationToken.None);

        Assert.Equal(SymlinkCapabilityStatus.PermissionDenied, check.Status);
        Assert.Contains("privilege", check.Detail);
    }

    [Fact]
    public async Task Probe_ReportsUnsupportedOnlyWhenTheRuntimeSaysSo()
    {
        FakeSymlinkFileSystem files = new(roots: Roots()) { CreateSymlinkThrows = new PlatformNotSupportedException("symlinks are not available on this runtime") };

        SymlinkCapabilityCheck check = await CreateService(files, Settings(revision: 2)).ProbeAsync(AreaId, CancellationToken.None);

        Assert.Equal(SymlinkCapabilityStatus.Unsupported, check.Status);
    }

    [Fact]
    public async Task Probe_ReportsAnUnreachableSeriesRootAsUnavailableAndWritesNothing()
    {
        FakeSymlinkFileSystem files = new(roots: [MoviesRoot()]);

        SymlinkCapabilityCheck check = await CreateService(files, Settings(revision: 3)).ProbeAsync(AreaId, CancellationToken.None);

        Assert.Equal(SymlinkCapabilityStatus.StorageUnavailable, check.Status);
        Assert.Contains(SeriesRoot(), check.Detail);
        Assert.Empty(files.Created);
    }

    [Fact]
    public async Task Probe_StillReportsAnUnreadableMovieRootAfterTheLinkTestPassed()
    {
        FakeSymlinkFileSystem files = new(roots: Roots()) { UnreadableDirectories = [MoviesRoot()] };

        SymlinkCapabilityCheck check = await CreateService(files, Settings(revision: 3)).ProbeAsync(AreaId, CancellationToken.None);

        // Saying only "links work" would hide that half the media is not mounted here at all.
        Assert.Equal(SymlinkCapabilityStatus.StorageUnavailable, check.Status);
        Assert.Contains(MoviesRoot(), check.Detail);
        Assert.Contains("link test passed", check.Detail);
    }

    [Fact]
    public async Task Probe_ReportsFailureWhenTheCreatedEntryIsNotALink()
    {
        FakeSymlinkFileSystem files = new(roots: Roots()) { CreatesPlainFilesInsteadOfLinks = true };

        SymlinkCapabilityCheck check = await CreateService(files, Settings(revision: 1)).ProbeAsync(AreaId, CancellationToken.None);

        Assert.Equal(SymlinkCapabilityStatus.Failed, check.Status);
        Assert.Contains("not a symbolic link", check.Detail);
    }

    [Fact]
    public async Task Probe_ReportsFailureWhenReadingThroughTheLinkMissesTheSource()
    {
        FakeSymlinkFileSystem files = new(roots: Roots()) { ResolvesLinksToTheWrongContent = true };

        SymlinkCapabilityCheck check = await CreateService(files, Settings(revision: 1)).ProbeAsync(AreaId, CancellationToken.None);

        Assert.Equal(SymlinkCapabilityStatus.Failed, check.Status);
        Assert.Contains("did not return the source content", check.Detail);
    }

    [Fact]
    public async Task Probe_ReportsALeftoverArtifactWithoutDowngradingAProvenResult()
    {
        FakeSymlinkFileSystem files = new(roots: Roots())
        {
            DeleteDirectoryThrows = new UnauthorizedAccessException("The probe directory is held by another process.")
        };

        SymlinkCapabilityCheck check = await CreateService(files, Settings(revision: 1)).ProbeAsync(AreaId, CancellationToken.None);

        // The feature demonstrably works; the leftover is a visible nuisance that has to be named, not a
        // reason to report the area as broken.
        Assert.Equal(SymlinkCapabilityStatus.Available, check.Status);
        Assert.Contains("may be left behind", check.Detail);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("not-absolute")]
    public async Task Probe_ReportsAnAreaThatCannotBeUsedInThisInstallation(string seriesRoot)
    {
        SymlinkSettings settings = Settings(revision: 4);
        settings.StorageAreas[0].SeriesRoot = seriesRoot;
        FakeSymlinkFileSystem files = new(roots: Roots());

        SymlinkCapabilityCheck check = await CreateService(files, settings).ProbeAsync(AreaId, CancellationToken.None);

        Assert.Equal(SymlinkCapabilityStatus.Failed, check.Status);
        Assert.Empty(files.Created);
    }

    [Fact]
    public async Task Probe_ReportsAnUnknownAreaWithoutInventingAFileError()
    {
        FakeSymlinkFileSystem files = new(roots: Roots());

        SymlinkCapabilityCheck check = await CreateService(files, Settings(revision: 4)).ProbeAsync("area-9", CancellationToken.None);

        Assert.Equal(SymlinkCapabilityStatus.Failed, check.Status);
        Assert.Contains("No storage area", check.Detail);
        Assert.Equal(4, check.ConfigRevision);
    }

    [Fact]
    public async Task GetStatus_ReportsNotTestedUntilAProbeRan()
    {
        SymlinkCapabilityCheck check = await CreateService(new FakeSymlinkFileSystem(roots: Roots()), Settings(revision: 1))
            .GetStatusAsync(AreaId, CancellationToken.None);

        Assert.Equal(SymlinkCapabilityStatus.NotTested, check.Status);
        Assert.Null(check.CheckedAt);
        Assert.False(check.IsCurrent(1));
        Assert.Equal(AreaId, check.StorageAreaId);
    }

    [Fact]
    public async Task GetStatus_HandsBackTheStoredAnswerForTheCallerToJudge()
    {
        await CreateService(new FakeSymlinkFileSystem(roots: Roots()), Settings(revision: 5)).ProbeAsync(AreaId, CancellationToken.None);

        SymlinkCapabilityCheck stored = await CreateService(new FakeSymlinkFileSystem(roots: Roots()), Settings(revision: 6))
            .GetStatusAsync(AreaId, CancellationToken.None);

        // Reading the state never probes again, and the stored revision is what makes it stale later.
        Assert.Equal(SymlinkCapabilityStatus.Available, stored.Status);
        Assert.Equal(5, stored.ConfigRevision);
        Assert.False(stored.IsCurrent(6));
    }

    [Fact]
    public async Task StartupFailurePreservesActivationAndProcessingResumesAfterMountRecovery()
    {
        SymlinkSettings settings = Settings(revision: 5);
        settings.Enabled = true;
        SymlinkCapabilityService unavailable = CreateService(new FakeSymlinkFileSystem(roots: []), settings);

        Assert.False(await unavailable.IsReadyAsync(settings, CancellationToken.None));
        AppSettings afterFailure = await unavailable.CheckAndUpdateAsync(CancellationToken.None);
        Assert.True(afterFailure.Symlinks.Enabled);
        Assert.False(await unavailable.IsReadyAsync(settings, CancellationToken.None));
        Assert.Equal(SymlinkCapabilityStatus.StorageUnavailable,
            Assert.Single(await store.GetCapabilityChecksAsync(CancellationToken.None)).Status);

        SymlinkCapabilityService recovered = CreateService(new FakeSymlinkFileSystem(roots: Roots()), settings);
        AppSettings afterRecovery = await recovered.CheckAndUpdateAsync(CancellationToken.None);
        Assert.True(afterRecovery.Symlinks.Enabled);
        Assert.True(await recovered.IsReadyAsync(settings, CancellationToken.None));
        settings.Revision++;
        Assert.False(await recovered.IsReadyAsync(settings, CancellationToken.None));
    }

    private SymlinkCapabilityService CreateService(ISymlinkFileSystem files, SymlinkSettings settings) =>
        new(store, new FixedSettings(new AppSettings { Symlinks = settings }), files, clock,
            NullLogger<SymlinkCapabilityService>.Instance);

    private static SymlinkSettings Settings(int revision) => new()
    {
        Revision = revision,
        StorageAreas =
        [
            new MediaStorageArea
            {
                Id = AreaId,
                Name = "Main",
                MoviesRoot = MoviesRoot(),
                SeriesRoot = SeriesRoot()
            }
        ]
    };

    private static string MoviesRoot() => LibManagarr("movies");
    private static string SeriesRoot() => LibManagarr("series");

    private static List<string> Roots() => [MoviesRoot(), SeriesRoot()];

    private static string LibManagarr(string relative) =>
        ArrPathRules.RuntimeSyntax == ArrPathSyntax.Windows
            ? @"C:\media\" + relative.Replace('/', '\\')
            : "/media/" + relative;

    private sealed class FixedSettings(AppSettings settings) : ISettingsService
    {
        public Task<AppSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
        public Task<AppSettings> SaveAsync(AppSettings value, CancellationToken cancellationToken) => Task.FromResult(value);
    }

    /// <summary>
    /// An in-memory file system with the properties the probe depends on: a link is a distinct entry
    /// whose content is resolved from the directory the link itself sits in, and a missing directory is
    /// an error rather than an empty listing.
    /// </summary>
    internal sealed class FakeSymlinkFileSystem(IEnumerable<string> roots) : ISymlinkFileSystem
    {
        /// <summary>The directories that exist here; a root the settings name but this set lacks is unmounted.</summary>
        public HashSet<string> Directories { get; } = new(roots, StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Links { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every path this file system was asked to create, in order.</summary>
        public List<string> Created { get; } = [];

        public Exception? CreateSymlinkThrows { get; init; }
        public Exception? DeleteDirectoryThrows { get; init; }
        public IEnumerable<string> UnreadableDirectories { get; init; } = [];
        public bool CreatesPlainFilesInsteadOfLinks { get; init; }
        public bool ResolvesLinksToTheWrongContent { get; init; }

        public void CreateFileSymlink(string linkPath, string targetPath)
        {
            if (CreateSymlinkThrows is not null) throw CreateSymlinkThrows;
            if (Links.ContainsKey(linkPath) || Files.ContainsKey(linkPath))
            {
                throw new IOException($"'{linkPath}' already exists.");
            }

            Created.Add(linkPath);
            if (CreatesPlainFilesInsteadOfLinks)
            {
                Files[linkPath] = "not a link"u8.ToArray();
                return;
            }

            Links[linkPath] = targetPath;
        }

        public bool EntryExists(string path) =>
            Directories.Contains(path) || Files.ContainsKey(path) || Links.ContainsKey(path);

        public bool IsSymlink(string path) => Links.ContainsKey(path);

        public string? ReadLinkTarget(string path) => Links.TryGetValue(path, out string? target) ? target : null;

        public byte[] ReadAllBytes(string path)
        {
            if (Links.TryGetValue(path, out string? target))
            {
                string resolved = Path.IsPathRooted(target) ? target : Path.Combine(Path.GetDirectoryName(path)!, target);
                if (ResolvesLinksToTheWrongContent) resolved = Path.Combine(Path.GetDirectoryName(path)!, "wrong-target.bin");
                if (!Files.TryGetValue(resolved, out byte[]? content))
                {
                    throw new IOException($"The link target '{resolved}' does not exist.");
                }

                return content;
            }

            if (Files.TryGetValue(path, out byte[]? bytes)) return bytes;
            throw new IOException($"'{path}' does not exist.");
        }

        /// <summary>
        /// Opening is this file system's read without the content, so an entry it cannot read is one it
        /// cannot open either. The interface asks a caller for one of the two, never for both.
        /// </summary>
        public void OpenForRead(string path)
        {
            _ = ReadAllBytes(path);
        }

        public void WriteAllBytes(string path, byte[] content)
        {
            if (Files.ContainsKey(path) || Links.ContainsKey(path)) throw new IOException($"'{path}' already exists.");
            Created.Add(path);
            Files[path] = content;
        }

        public void DeleteEntry(string path)
        {
            if (Directories.Contains(path))
            {
                if (DeleteDirectoryThrows is not null) throw DeleteDirectoryThrows;
                Directories.Remove(path);
                return;
            }

            if (!Links.Remove(path) && !Files.Remove(path)) throw new IOException($"'{path}' does not exist.");
        }

        public void MoveEntry(string sourcePath, string destinationPath)
        {
            if (Links.Remove(sourcePath, out string? target))
            {
                Links[destinationPath] = target;
                return;
            }

            if (Files.Remove(sourcePath, out byte[]? content))
            {
                Files[destinationPath] = content;
                return;
            }

            throw new IOException($"'{sourcePath}' does not exist.");
        }

        public void CreateDirectory(string path)
        {
            if (!Directories.Add(path)) throw new IOException($"'{path}' already exists.");
            Created.Add(path);
        }

        public IReadOnlyList<string> ListEntries(string directoryPath)
        {
            if (UnreadableDirectories.Any(dir => string.Equals(dir, directoryPath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new UnauthorizedAccessException($"'{directoryPath}' could not be read.");
            }

            if (!Directories.Contains(directoryPath)) throw new DirectoryNotFoundException($"'{directoryPath}' does not exist.");
            string prefix = directoryPath.EndsWith(directoryPath.Contains('\\') ? "\\" : "/", StringComparison.Ordinal)
                ? directoryPath
                : directoryPath + (directoryPath.Contains('\\') ? "\\" : "/");
            return Directories.Where(dir => dir.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && dir != directoryPath)
                .Concat(Files.Keys.Concat(Links.Keys).Where(entry => entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }
}
