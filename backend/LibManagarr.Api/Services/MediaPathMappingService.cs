using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>
/// Outcome of one path translation. The failure text names the root or mapping that refused, which is
/// what the UI shows instead of a generic "path not allowed".
/// </summary>
public sealed record PathMappingResult(string? Path, string? Failure, string? MappingId)
{
    public bool Success => Failure is null && Path is not null;

    internal static PathMappingResult Failed(string failure) => new(null, failure, null);
}

/// <summary>
/// Translates between the paths an Arr instance reports and the paths this process can open, and
/// refuses anything that would leave a configured storage area. Nothing here touches the file system:
/// a translation is a claim about the configuration, and only a probe turns it into a fact.
/// </summary>
public sealed class MediaPathMappingService(ISettingsService settings, ISymlinkFileSystem? fileSystem = null,
    SharedMediaPathService? sharedPaths = null)
{
    /// <summary>Instance view to LibManagarr view, the direction every source and target path arrives in.</summary>
    public async Task<PathMappingResult> TranslateToLibManagarrPathAsync(
        string instanceId, ArrApplicationKind kind, string instancePath, CancellationToken cancellationToken)
    {
        AppSettings snapshot = await settings.GetSnapshotAsync(cancellationToken);
        SymlinkSettings configuration = snapshot.Symlinks;
        if (configuration.MediaRoot is not null)
            return sharedPaths is null ? PathMappingResult.Failed("Root-folder discovery is unavailable.")
                : await sharedPaths.ValidatePathAsync(snapshot, kind, instanceId, instancePath, cancellationToken);
        if (!ArrPathRules.TryMapPrefix(
            configuration.PathMappings, instanceId, kind, instancePath, PathRole.InstanceSide,
            mapping => mapping.InstanceRoot, mapping => mapping.LibManagarrRoot, PathRole.LibManagarrSide,
            out string mappedPath, out string? failure, out string? mappingId))
        {
            return PathMappingResult.Failed(failure!);
        }

        return new PathMappingResult(mappedPath, null, mappingId);
    }

    /// <summary>
    /// LibManagarr view back to the instance view. Needed to tell another process about a file, which
    /// is why the target grammar comes from that instance's own mapping instead of from this process.
    /// </summary>
    public async Task<PathMappingResult> TranslateToInstancePathAsync(
        string instanceId, ArrApplicationKind kind, string libManagarrPath, CancellationToken cancellationToken)
    {
        AppSettings snapshot = await settings.GetSnapshotAsync(cancellationToken);
        SymlinkSettings configuration = snapshot.Symlinks;
        if (configuration.MediaRoot is not null)
            return sharedPaths is null ? PathMappingResult.Failed("Root-folder discovery is unavailable.")
                : await sharedPaths.ValidatePathAsync(snapshot, kind, instanceId, libManagarrPath, cancellationToken);
        if (!ArrPathRules.TryMapPrefix(
            configuration.PathMappings, instanceId, kind, libManagarrPath, PathRole.LibManagarrSide,
            mapping => mapping.LibManagarrRoot, mapping => mapping.InstanceRoot, PathRole.InstanceSide,
            out string mappedPath, out string? failure, out string? mappingId))
        {
            return PathMappingResult.Failed(failure!);
        }

        return new PathMappingResult(mappedPath, null, mappingId);
    }

    /// <summary>
    /// Guards the one line a link must never cross: the source has to stay inside a configured movie
    /// root and the target inside a series root. Equal looking strings are not the check — the area
    /// roots are the allow list, so a mapping that points somewhere else fails here.
    /// </summary>
    public async Task<PathMappingResult> EnsureWithinStorageAreaAsync(
        ArrApplicationKind kind, string libManagarrPath, CancellationToken cancellationToken)
    {
        SymlinkSettings configuration = (await settings.GetSnapshotAsync(cancellationToken)).Symlinks;
        if (!ArrPathRules.TryParse(libManagarrPath, out ParsedPath? parsed, out string? failure))
        {
            return PathMappingResult.Failed(failure!);
        }

        if (parsed!.Syntax != ArrPathRules.RuntimeSyntax)
        {
            return PathMappingResult.Failed($"The path is written in {parsed.Syntax} syntax while this process needs {ArrPathRules.RuntimeSyntax} syntax.");
        }

        string property = kind == ArrApplicationKind.Radarr ? "movie" : "series";
        List<string> roots = kind == ArrApplicationKind.Radarr
            ? configuration.StorageAreas.Select(area => area.MoviesRoot).ToList()
            : configuration.StorageAreas.Select(area => area.SeriesRoot).ToList();

        if (roots.Any(root => ArrPathRules.IsWithinRoot(root, libManagarrPath)))
        {
            try
            {
                SymlinkPathGuard.CheckParents(fileSystem ?? new RealSymlinkFileSystem(), libManagarrPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return PathMappingResult.Failed(exception.Message);
            }
            return new PathMappingResult(libManagarrPath, null, null);
        }

        return PathMappingResult.Failed(roots.Count == 0
            ? $"No storage area with a {property} root is configured."
            : $"'{parsed.Format()}' is outside every configured {property} root.");
    }
}
