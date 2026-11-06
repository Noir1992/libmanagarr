using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>
/// Answers the one question the whole feature hangs on: can this process, with the rights it really
/// has, create and read a file symlink in the media tree that is configured right now. That is not
/// answerable from the operating system name, from a working Arr API or from a test in the temporary
/// directory, so the probe writes its own throwaway files into the configured series root and reads
/// the movie root through the configured topology.
/// </summary>
public sealed class SymlinkCapabilityService(
    ISymlinkStore store,
    ISettingsService settings,
    ISymlinkFileSystem files,
    TimeProvider clock,
    ILogger<SymlinkCapabilityService> logger)
{
    private const string ProbeDirectoryPrefix = ".libmanagarr-symlink-check-";

    /// <summary>Win32 ERROR_PRIVILEGE_NOT_HELD, the way Windows reports a missing symlink privilege.</summary>
    private const int PrivilegeNotHeldHResult = unchecked((int)0x80070522);

    private static readonly byte[] SourceV1 = "libmanagarr-symlink-probe-v1"u8.ToArray();
    private static readonly byte[] SourceV2 = "libmanagarr-symlink-probe-v2"u8.ToArray();

    /// <summary>
    /// Runs the probe and stores the outcome under the configuration revision it ran on. Every step
    /// reports where it stopped, so the operator sees the affected root instead of "symlinks unsupported".
    /// </summary>
    public async Task<SymlinkCapabilityCheck> ProbeAsync(string storageAreaId, CancellationToken cancellationToken)
    {
        SymlinkSettings configuration = (await settings.GetSnapshotAsync(cancellationToken)).Symlinks;
        MediaStorageArea? area = configuration.StorageAreas
            .FirstOrDefault(candidate => string.Equals(candidate.Id, storageAreaId, StringComparison.OrdinalIgnoreCase));
        if (area is null)
        {
            return await PersistAsync(storageAreaId, SymlinkCapabilityStatus.Failed,
                "No storage area with this id is configured.", configuration.Revision, cancellationToken);
        }

        if (!ArrPathRules.TryParse(area.SeriesRoot, out ParsedPath? seriesRoot, out string? seriesFailure) ||
            seriesRoot!.Syntax != ArrPathRules.RuntimeSyntax)
        {
            return await PersistAsync(area.Id, SymlinkCapabilityStatus.Failed,
                seriesFailure ?? $"The series root '{area.SeriesRoot}' is not usable in this installation.",
                configuration.Revision, cancellationToken);
        }

        if (!files.EntryExists(area.SeriesRoot))
        {
            return await PersistAsync(area.Id, SymlinkCapabilityStatus.StorageUnavailable,
                $"The series root '{area.SeriesRoot}' is not reachable from this process.",
                configuration.Revision, cancellationToken);
        }

        string probeDirectory = Path.Combine(area.SeriesRoot, ProbeDirectoryPrefix + Guid.NewGuid().ToString("N")[..12]);
        ProbeResult result = RunProbe(probeDirectory, area);

        // The movie side is a separate fact: a link test that only ever touched the series tree says
        // nothing about whether the films are mounted here at all.
        string? moviesFailure = ProbeMoviesRoot(area);
        if (result.Status == SymlinkCapabilityStatus.Available && moviesFailure is not null)
        {
            result = new ProbeResult(SymlinkCapabilityStatus.StorageUnavailable, moviesFailure);
        }

        string detail = result.Status == SymlinkCapabilityStatus.Available
            ? $"{result.Detail} The movie root '{area.MoviesRoot}' is readable."
            : result.Detail;
        return await PersistAsync(area.Id, result.Status, detail, configuration.Revision, cancellationToken);
    }

    /// <summary>
    /// The stored answer for an area, or an explicit not-tested row. A result from a previous
    /// configuration revision is handed back unchanged; <see cref="SymlinkCapabilityCheck.IsCurrent"/>
    /// is what turns it into "stale" for the caller.
    /// </summary>
    public async Task<SymlinkCapabilityCheck> GetStatusAsync(string storageAreaId, CancellationToken cancellationToken)
    {
        SymlinkCapabilityCheck? stored = await store.GetCapabilityCheckAsync(storageAreaId, cancellationToken);
        return stored ?? new SymlinkCapabilityCheck
        {
            StorageAreaId = storageAreaId,
            Status = SymlinkCapabilityStatus.NotTested,
            Detail = "The symlink capability test has not run for this storage area.",
            CheckedAt = null,
            ConfigRevision = 0
        };
    }

    private async Task<SymlinkCapabilityCheck> PersistAsync(
        string storageAreaId, SymlinkCapabilityStatus status, string? detail, int configRevision, CancellationToken cancellationToken)
    {
        SymlinkCapabilityCheck check = new()
        {
            StorageAreaId = storageAreaId,
            Status = status,
            Detail = detail,
            CheckedAt = clock.GetUtcNow(),
            ConfigRevision = configRevision
        };
        await store.SaveCapabilityCheckAsync(check, cancellationToken);
        logger.LogInformation("Symlink capability probe for storage area {AreaId} ended as {Status}: {Detail}",
            storageAreaId, status, detail);
        return check;
    }

    private ProbeResult RunProbe(string probeDirectory, MediaStorageArea area)
    {
        ProbeResult result;
        try
        {
            result = ProbeSteps(probeDirectory, area);
        }
        catch (PlatformNotSupportedException exception)
        {
            result = new ProbeResult(SymlinkCapabilityStatus.Unsupported,
                $"This runtime refuses to create file symlinks: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            result = new ProbeResult(SymlinkCapabilityStatus.PermissionDenied,
                $"Creating a link in '{probeDirectory}' was denied: {exception.Message}");
        }
        catch (IOException exception) when (exception.HResult == PrivilegeNotHeldHResult)
        {
            // Windows reports a missing symlink privilege as an ordinary I/O error, and that is the one
            // common case an operator can actually grant. Reporting it as a broken file system would
            // point at the mount instead of at the right.
            result = new ProbeResult(SymlinkCapabilityStatus.PermissionDenied,
                $"Creating a link in '{probeDirectory}' was refused for lack of a privilege: {exception.Message}");
        }
        catch (IOException exception)
        {
            // Deliberately not folded into Unsupported: a read-only or damaged mount has to stay
            // recognisable as such, and only the runtime itself may declare the feature missing.
            result = new ProbeResult(SymlinkCapabilityStatus.Failed,
                $"The file system in '{probeDirectory}' refused the operation: {exception.Message}");
        }

        string? leftover = Cleanup(probeDirectory);
        if (leftover is not null)
        {
            // A leftover probe directory is visible in the media tree, so it is reported even when the
            // capability itself was proven. Downgrading a proven result would hide that the feature works.
            result = new ProbeResult(result.Status, $"{result.Detail} A probe artifact may be left behind: '{leftover}'.");
        }

        return result;
    }

    private ProbeResult ProbeSteps(string probeDirectory, MediaStorageArea area)
    {
        string sourceV1 = Path.Combine(probeDirectory, "probe-source-v1.bin");
        string sourceV2 = Path.Combine(probeDirectory, "probe-source-v2.bin");
        string link = Path.Combine(probeDirectory, "probe-link.bin");
        string renamed = Path.Combine(probeDirectory, "probe-link-renamed.bin");

        files.CreateDirectory(probeDirectory);
        files.WriteAllBytes(sourceV1, SourceV1);
        files.CreateFileSymlink(link, Path.GetFileName(sourceV1));

        string? linkText = files.ReadLinkTarget(link);
        if (linkText is null)
        {
            return Failed($"The entry created at '{link}' is not a symbolic link.");
        }

        if (!ContentMatches(link, SourceV1))
        {
            return Failed($"Reading through the link '{link}' did not return the source content.");
        }

        files.MoveEntry(link, renamed);
        if (files.EntryExists(link) || !files.IsSymlink(renamed) || !ContentMatches(renamed, SourceV1))
        {
            return Failed($"Renaming the link to '{renamed}' lost the link or its target.");
        }

        // Replacing the target is what a Radarr upgrade will do later, so it has to work the same way
        // here: the old entry goes away and the new link points at the new file.
        files.WriteAllBytes(sourceV2, SourceV2);
        files.DeleteEntry(renamed);
        files.CreateFileSymlink(link, Path.GetFileName(sourceV2));
        if (!ContentMatches(link, SourceV2))
        {
            return Failed($"Replacing the link target at '{link}' did not resolve to the new source.");
        }

        if (!ContentMatches(sourceV1, SourceV1) || !ContentMatches(sourceV2, SourceV2))
        {
            return Failed("A probe source file changed while the probe ran; refusing to report success.");
        }

        return new ProbeResult(SymlinkCapabilityStatus.Available,
            $"File symlinks work in the series root '{area.SeriesRoot}' of storage area '{area.Name}' (link text '{linkText}').");
    }

    private string? Cleanup(string probeDirectory)
    {
        try
        {
            if (files.EntryExists(probeDirectory))
            {
                foreach (string entry in files.ListEntries(probeDirectory)) files.DeleteEntry(entry);
                files.DeleteEntry(probeDirectory);
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Symlink capability probe could not clean up {ProbeDirectory}", probeDirectory);
            return probeDirectory;
        }
    }

    private string? ProbeMoviesRoot(MediaStorageArea area)
    {
        if (!ArrPathRules.TryParse(area.MoviesRoot, out ParsedPath? moviesRoot, out string? parseFailure) ||
            moviesRoot!.Syntax != ArrPathRules.RuntimeSyntax)
        {
            return parseFailure ?? $"The movie root '{area.MoviesRoot}' is not usable in this installation.";
        }

        try
        {
            files.ListEntries(area.MoviesRoot);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"The link test passed, but the movie root '{area.MoviesRoot}' could not be read: {exception.Message}";
        }
    }

    private bool ContentMatches(string path, byte[] expected)
    {
        try
        {
            return files.ReadAllBytes(path).AsSpan().SequenceEqual(expected);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static ProbeResult Failed(string detail) => new(SymlinkCapabilityStatus.Failed, detail);

    private readonly record struct ProbeResult(SymlinkCapabilityStatus Status, string Detail);
}
