using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Tests.Services;

public sealed class ExistingEpisodeFileInspectorRealTests
{
    [RealSymlinkFact]
    public async Task RealRelativeAndAbsoluteLinksAreReadWithoutAdoptionOrChanges()
    {
        string root = Path.Combine(Path.GetTempPath(), "libmanagarr-existing-link-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "movies", "Film", "movie.mkv");
        string episode = Path.Combine(root, "series", "Show", "Specials", "s00e01.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(episode)!);
        try
        {
            await File.WriteAllTextAsync(source, "original movie");
            DateTime modified = File.GetLastWriteTimeUtc(source);
            AppSettings settings = ExistingEpisodeFileInspectorTests.Settings(root);
            foreach (string target in new[] { Path.GetRelativePath(Path.GetDirectoryName(episode)!, source), source })
            {
                File.CreateSymbolicLink(episode, target);
                ExistingEpisodeFileInspector inspector = ExistingEpisodeFileInspectorTests.Inspector(settings,
                    new RealSymlinkFileSystem(), ExistingEpisodeFileInspectorTests.Handler(episode, source));
                ExistingEpisodeFileCheck check = (await inspector.CheckAsync(settings, ExistingEpisodeFileInspectorTests.Suggestion(), CancellationToken.None))!;
                Assert.Equal(ExistingEpisodeFileStatus.Matches, check.Status);
                Assert.Equal(source, check.ActualTarget);
                Assert.Equal(target, new FileInfo(episode).LinkTarget);
                Assert.Equal("original movie", await File.ReadAllTextAsync(episode));
                Assert.Equal(modified, File.GetLastWriteTimeUtc(source));
                File.Delete(episode);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [RealSymlinkFact]
    public async Task RealFilesystemChangesBecomeMismatchBrokenAndRegularFileResults()
    {
        string root = Path.Combine(Path.GetTempPath(), "libmanagarr-existing-link-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "movies", "movie.mkv");
        string other = Path.Combine(root, "movies", "other.mkv");
        string episode = Path.Combine(root, "series", "special.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(episode)!);
        try
        {
            await File.WriteAllTextAsync(source, "expected movie");
            await File.WriteAllTextAsync(other, "different movie");
            File.CreateSymbolicLink(episode, other);
            AppSettings settings = ExistingEpisodeFileInspectorTests.Settings(root);
            ExistingEpisodeFileInspector inspector = ExistingEpisodeFileInspectorTests.Inspector(settings,
                new RealSymlinkFileSystem(), ExistingEpisodeFileInspectorTests.Handler(episode, source));
            async Task<ExistingEpisodeFileStatus> Status() => (await inspector.CheckAsync(settings,
                ExistingEpisodeFileInspectorTests.Suggestion(), CancellationToken.None))!.Status;
            Assert.Equal(ExistingEpisodeFileStatus.TargetMismatch, await Status());
            File.Delete(other);
            Assert.Equal(ExistingEpisodeFileStatus.BrokenSymlink, await Status());
            File.Delete(episode);
            await File.WriteAllTextAsync(episode, "regular episode");
            Assert.Equal(ExistingEpisodeFileStatus.RegularFile, await Status());
            Assert.Equal("regular episode", await File.ReadAllTextAsync(episode));
            Assert.Equal("expected movie", await File.ReadAllTextAsync(source));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
