using System.Reflection;
using LibManagarr.Api.Services.Backup;

namespace LibManagarr.Api.Tests.Services;

public sealed class BackupSizeLimitTests
{
    [Fact]
    public async Task UploadLimit_ReportsTheEnforcedByteLimit()
    {
        string destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using MemoryStream source = new(new byte[1501]);
            Task<long> copying = (Task<long>)typeof(BackupUploadReader)
                .GetMethod("CopyBoundedAsync", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [source, destination, 1500L, CancellationToken.None])!;
            BackupException exception = await Assert.ThrowsAsync<BackupException>(() => copying);
            Assert.Equal(1500L, exception.LimitBytes);
            Assert.Equal(413, exception.StatusCode);
        }
        finally { File.Delete(destination); }
    }

    [Fact]
    public async Task ArchiveLimit_ReportsTheEnforcedByteLimit()
    {
        string archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            // A sparse oversized file reaches the archive check without allocating a full backup.
            using (FileStream stream = File.Create(archive)) stream.SetLength(BackupArchiveFormat.MaxArchiveBytes + 1);
            Type codec = typeof(BackupUploadReader).Assembly.GetType("LibManagarr.Api.Services.Backup.BackupArchiveCodec")!;
            Task<long> reading = (Task<long>)codec.GetMethod("ReadAsync")!
                .Invoke(null, [archive, "unused", archive + ".zip", CancellationToken.None])!;
            BackupException exception = await Assert.ThrowsAsync<BackupException>(() => reading);
            Assert.Equal(BackupArchiveFormat.MaxArchiveBytes, exception.LimitBytes);
        }
        finally { File.Delete(archive); }
    }

    [Fact]
    public async Task ExtractionLimit_ReportsTheUncompressedByteLimit()
    {
        using MemoryStream input = new(new byte[1]);
        using MemoryStream output = new();
        Task<long> copying = (Task<long>)typeof(BackupImportService)
            .GetMethod("CopyCountedAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [input, output, BackupArchiveFormat.MaxUncompressedBytes, CancellationToken.None])!;
        BackupException exception = await Assert.ThrowsAsync<BackupException>(() => copying);
        Assert.Equal(BackupArchiveFormat.MaxUncompressedBytes, exception.LimitBytes);
    }

    [Fact]
    public void FreeSpaceFailure_ReportsTheRequiredByteCount()
    {
        long archiveBytes = 1L << 60;
        TargetInvocationException invocation = Assert.Throws<TargetInvocationException>(() =>
            typeof(BackupImportService).GetMethod("EnsureFreeSpace", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [Path.GetTempPath(), archiveBytes, archiveBytes]));
        BackupException exception = Assert.IsType<BackupException>(invocation.InnerException);
        Assert.Equal(4 * archiveBytes, exception.RequiredBytes);
        Assert.Equal(507, exception.StatusCode);
    }
}
