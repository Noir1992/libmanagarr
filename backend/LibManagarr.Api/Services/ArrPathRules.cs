using System.Text;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>
/// Which path grammar a string is written in. Decided per string instead of per host, because a
/// Linux container commonly has to read paths reported by a Sonarr or Radarr running elsewhere.
/// </summary>
public enum ArrPathSyntax
{
    Windows,
    Unix
}

/// <summary>
/// Which side of a path mapping a path belongs to. The side decides the grammar it has to be written
/// in, so a Linux process still compares the paths of a Windows instance with Windows rules while its
/// own paths keep the grammar it can actually open.
/// </summary>
public enum PathRole
{
    InstanceSide,
    LibManagarrSide
}

/// <summary>
/// A path split into the parts a mapping may be matched on. Comparison never looks at the raw
/// string, so a prefix match cannot land in the middle of a segment.
/// </summary>
public sealed record ParsedPath(ArrPathSyntax Syntax, string Root, IReadOnlyList<string> Segments)
{
    /// <summary>Root plus segments, rendered with the separator that syntax uses.</summary>
    public string Format()
    {
        char separator = Syntax == ArrPathSyntax.Windows ? '\\' : '/';
        // A bare drive root has to keep its separator, because "C:" alone names a place relative to the
        // current directory. A bare UNC share is already complete and should read back as written.
        if (Segments.Count == 0) return Root.StartsWith(@"\\", StringComparison.Ordinal) ? Root.TrimEnd(separator) : Root;
        StringBuilder text = new(Root);
        for (int index = 0; index < Segments.Count; index += 1)
        {
            if (index > 0) text.Append(separator);
            text.Append(Segments[index]);
        }

        return text.ToString();
    }
}

/// <summary>
/// Path grammar and prefix mapping rules shared by validation, translation and the probe. Pure on
/// purpose: the same decisions have to hold in a unit test, on the developer's Windows machine and
/// inside the Linux container, where the operating system alone answers none of them.
/// </summary>
public static class ArrPathRules
{
    private const int MaxPathLength = 4000;
    private static readonly char[] WindowsInvalidSegmentCharacters = ['"', '*', '<', '>', '|', '?'];
    private static readonly string[] ReservedWindowsNames =
    [
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9"
    ];

    /// <summary>Grammar this process has to work in. A Windows host behind a Linux container is Linux here.</summary>
    public static ArrPathSyntax RuntimeSyntax => OperatingSystem.IsWindows() ? ArrPathSyntax.Windows : ArrPathSyntax.Unix;

    /// <summary>
    /// Recognises a drive letter, a UNC prefix or a POSIX absolute path and refuses anything relative,
    /// so no rule below ever has to guess about a root.
    /// </summary>
    public static bool TryParse(string? value, out ParsedPath? parsed, out string? failure)
    {
        parsed = null;
        failure = null;
        string path = (value ?? string.Empty).Trim();
        if (path.Length == 0)
        {
            failure = "A path is required.";
            return false;
        }

        if (path.Length > MaxPathLength)
        {
            failure = $"The path must not exceed {MaxPathLength} characters.";
            return false;
        }

        ArrPathSyntax syntax = Detect(path);
        string unified = path.Replace('\\', '/');
        string root;
        string body;
        if (IsDrivePath(unified))
        {
            root = unified[..2].ToUpperInvariant();
            body = unified[2..];
            // "C:media" names a place relative to the current directory on that drive, so accepting it
            // here would silently turn it into a different location than the one that was written down.
            if (body.Length > 0 && !body.StartsWith("/", StringComparison.Ordinal))
            {
                failure = "A drive path must be absolute; write 'C:\\media' rather than 'C:media'.";
                return false;
            }
        }
        else if (unified.StartsWith("//", StringComparison.Ordinal))
        {
            // UNC: host and share are the root and never a mappable segment.
            string[] parts = unified.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                failure = "A UNC path must name a host and a share.";
                return false;
            }

            root = "//" + parts[0] + "/" + parts[1];
            body = unified.Length > root.Length + 1 ? unified[(root.Length + 1)..] : string.Empty;
        }
        else if (unified.StartsWith("/", StringComparison.Ordinal))
        {
            syntax = ArrPathSyntax.Unix;
            root = "/";
            body = unified[1..];
        }
        else
        {
            failure = "The path must be absolute.";
            return false;
        }

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            failure = "The path contains characters that are not allowed in a path.";
            return false;
        }

        List<string> segments = [];
        foreach (string raw in body.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IsSegmentAllowed(raw, syntax, out string? segmentFailure))
            {
                failure = segmentFailure;
                return false;
            }

            segments.Add(raw);
        }

        if (syntax == ArrPathSyntax.Windows)
        {
            root = root.Replace('/', '\\');
            if (!root.EndsWith("\\", StringComparison.Ordinal)) root += "\\";
        }

        parsed = new ParsedPath(syntax, root, segments);
        return true;
    }

    public static ArrPathSyntax Detect(string path)
    {
        string unified = path.Trim().Replace('\\', '/');
        return IsDrivePath(unified) || unified.StartsWith("//", StringComparison.Ordinal)
            ? ArrPathSyntax.Windows
            : ArrPathSyntax.Unix;
    }

    /// <summary>True when <paramref name="path"/> is the root itself or sits below it on complete segments.</summary>
    public static bool IsWithinRoot(string root, string path)
    {
        if (!TryParse(root, out ParsedPath? parsedRoot, out _) || !TryParse(path, out ParsedPath? parsedPath, out _))
        {
            return false;
        }

        if (parsedRoot!.Syntax != parsedPath!.Syntax ||
            !SegmentEquals(parsedRoot.Root, parsedPath.Root, parsedRoot.Syntax))
        {
            return false;
        }

        if (parsedPath.Segments.Count < parsedRoot.Segments.Count) return false;
        for (int index = 0; index < parsedRoot.Segments.Count; index += 1)
        {
            if (!SegmentEquals(parsedRoot.Segments[index], parsedPath.Segments[index], parsedRoot.Syntax)) return false;
        }

        return true;
    }

    public static bool SegmentEquals(string left, string right, ArrPathSyntax syntax) =>
        string.Equals(left, right, syntax == ArrPathSyntax.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// True when the path names nothing but a whole file system: a drive root or POSIX '/'. A bare UNC
    /// share is not one of those, because host and share already single out one shelf on one machine.
    /// </summary>
    public static bool IsFileSystemRoot(ParsedPath path) =>
        path.Segments.Count == 0 && !path.Root.StartsWith(@"\\", StringComparison.Ordinal);

    /// <summary>
    /// Grammar a path belonging to <paramref name="role"/> has to be written in. The LibManagarr side is
    /// always this process' own grammar; the instance side keeps whatever grammar that string already uses.
    /// </summary>
    public static ArrPathSyntax ExpectedSyntax(PathRole role, string path) =>
        role == PathRole.LibManagarrSide ? RuntimeSyntax : Detect(path);

    internal static string RoleName(PathRole role) => role == PathRole.LibManagarrSide ? "LibManagarr" : "instance";

    /// <summary>
    /// Replaces the longest mapping prefix <paramref name="sourcePath"/> starts with, on complete
    /// segments only. Two equally long prefixes that disagree is the genuinely ambiguous case, so it is
    /// refused here as well as during validation.
    /// </summary>
    public static bool TryMapPrefix(
        IEnumerable<ArrPathMapping> mappings,
        string instanceId,
        ArrApplicationKind kind,
        string sourcePath,
        PathRole sourceRole,
        Func<ArrPathMapping, string> sourceRootSelector,
        Func<ArrPathMapping, string> targetRootSelector,
        PathRole targetRole,
        out string mappedPath,
        out string? failure,
        out string? mappingId)
    {
        mappedPath = string.Empty;
        failure = null;
        mappingId = null;
        if (!TryParse(sourcePath, out ParsedPath? source, out failure)) return false;
        ArrPathSyntax sourceSyntax = ExpectedSyntax(sourceRole, sourcePath);
        if (source!.Syntax != sourceSyntax)
        {
            failure = $"The path is written in {source.Syntax} syntax while the {kind} mapping for instance '{instanceId}' expects {sourceSyntax} syntax.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            failure = "The instance id is required.";
            return false;
        }

        List<ArrPathMapping> instanceMappings = mappings.Where(mapping =>
            string.Equals(mapping.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase) && mapping.Kind == kind).ToList();
        if (instanceMappings.Count == 0)
        {
            failure = $"No {kind} path mapping is configured for instance '{instanceId}'.";
            return false;
        }

        List<(ArrPathMapping Mapping, ParsedPath Target, int Length)> candidates = [];
        List<string> unusable = [];
        foreach (ArrPathMapping mapping in instanceMappings)
        {
            string sourceRoot = sourceRootSelector(mapping);
            string targetRoot = targetRootSelector(mapping);
            if (!TryParse(sourceRoot, out ParsedPath? root, out _) ||
                root!.Syntax != ExpectedSyntax(sourceRole, sourceRoot))
            {
                continue;
            }

            if (!StartsWith(source, root, source.Syntax)) continue;
            if (!TryParse(targetRoot, out ParsedPath? target, out string? targetFailure))
            {
                unusable.Add($"'{sourceRoot}' maps to an unusable target '{targetRoot}': {targetFailure}");
                continue;
            }

            ArrPathSyntax expectedTargetSyntax = ExpectedSyntax(targetRole, targetRoot);
            if (target!.Syntax != expectedTargetSyntax)
            {
                unusable.Add($"The target '{targetRoot}' is written in {target.Syntax} syntax while the {RoleName(targetRole)} side needs {expectedTargetSyntax} syntax.");
                continue;
            }

            candidates.Add((mapping, target!, root.Segments.Count));
        }

        if (candidates.Count == 0)
        {
            failure = unusable.Count > 0
                ? string.Join(' ', unusable)
                : $"No {kind} path mapping for instance '{instanceId}' covers '{source.Format()}'.";
            return false;
        }

        int longest = candidates.Max(candidate => candidate.Length);
        List<(ArrPathMapping Mapping, ParsedPath Target, int Length)> winners =
            candidates.Where(candidate => candidate.Length == longest).ToList();
        if (winners.Count > 1)
        {
            failure = $"'{source.Format()}' matches {winners.Count} equally long mappings for instance '{instanceId}', so its target is ambiguous.";
            return false;
        }

        (ArrPathMapping Mapping, ParsedPath Target, int Length) winner = winners[0];
        List<string> resultSegments = [.. winner.Target.Segments, .. source.Segments.Skip(winner.Length)];
        mappedPath = new ParsedPath(winner.Target.Syntax, winner.Target.Root, resultSegments).Format();
        mappingId = winner.Mapping.Id;
        return true;
    }

    private static bool StartsWith(ParsedPath path, ParsedPath prefix, ArrPathSyntax syntax)
    {
        if (path.Segments.Count < prefix.Segments.Count) return false;
        for (int index = 0; index < prefix.Segments.Count; index += 1)
        {
            if (!SegmentEquals(prefix.Segments[index], path.Segments[index], syntax)) return false;
        }

        return true;
    }

    private static bool IsDrivePath(string unifiedPath) =>
        unifiedPath.Length >= 2 && unifiedPath[1] == ':' && char.IsAsciiLetter(unifiedPath[0]);

    private static bool IsSegmentAllowed(string segment, ArrPathSyntax syntax, out string? failure)
    {
        failure = null;
        if (segment is "." or "..")
        {
            failure = "The path must not contain '.' or '..' segments.";
            return false;
        }

        if (segment.Any(char.IsControl) || segment.Contains(':'))
        {
            failure = "A path segment contains characters that are not allowed in a path.";
            return false;
        }

        if (segment.Trim() != segment)
        {
            failure = "A path segment must not start or end with whitespace.";
            return false;
        }

        if (syntax == ArrPathSyntax.Windows)
        {
            // A path written in Windows grammar is checked with Windows rules even when this process is
            // Linux, because that is the file system the name has to survive on.
            if (segment.IndexOfAny(WindowsInvalidSegmentCharacters) >= 0)
            {
                failure = "A Windows path segment contains characters that are not allowed in a file name.";
                return false;
            }

            if (segment.EndsWith('.') || segment.EndsWith(' '))
            {
                failure = "A Windows path segment must not end in a dot or a space.";
                return false;
            }

            if (ReservedWindowsNames.Contains(segment.Split('.')[0], StringComparer.OrdinalIgnoreCase))
            {
                failure = "A Windows path segment uses a name reserved by Windows.";
                return false;
            }
        }

        return true;
    }
}
