using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>
/// Accepts or refuses a symlink configuration before anything can act on it. A path that merely
/// parses is not enough here: the rules below are what stop a mapping set that would silently pick the
/// wrong shelf, because the first link written under a wrong mapping is already a real file change.
/// </summary>
public static class SymlinkSettingsRules
{
    public const int MaxNameLength = 255;
    public const int MaxStorageAreas = 50;
    public const int MaxPathMappings = 200;
    public const int MaxReaderViews = 10;

    public static SymlinkSettings Normalize(SymlinkSettings? settings)
    {
        SymlinkSettings input = settings ?? new SymlinkSettings();
        if (input.MediaRoot is not null || ((input.StorageAreas?.Count ?? 0) == 0
            && (input.PathMappings?.Count ?? 0) == 0 && (input.ReaderViews?.Count ?? 0) == 0))
        {
            string root = FormatRoot(input.MediaRoot ?? (OperatingSystem.IsWindows()
                ? Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "media") : "/media"));
            return new SymlinkSettings
            {
                Enabled = input.Enabled,
                MediaRoot = root,
                Revision = Math.Max(0, input.Revision),
                StorageAreas = [new MediaStorageArea
                {
                    Id = SymlinkSettings.SharedStorageAreaId, Name = root, MoviesRoot = root, SeriesRoot = root
                }]
            };
        }
        return new SymlinkSettings
        {
            Enabled = input.Enabled,
            StorageAreas = (input.StorageAreas ?? []).Select(area => new MediaStorageArea
            {
                Id = UuidV7.IsUuidV7(area.Id) ? area.Id.Trim() : UuidV7.Create(),
                Name = area.Name?.Trim() ?? string.Empty,
                MoviesRoot = FormatRoot(area.MoviesRoot),
                SeriesRoot = FormatRoot(area.SeriesRoot)
            }).ToList(),
            PathMappings = (input.PathMappings ?? []).Select(mapping => new ArrPathMapping
            {
                Id = UuidV7.IsUuidV7(mapping.Id) ? mapping.Id.Trim() : UuidV7.Create(),
                InstanceId = mapping.InstanceId?.Trim() ?? string.Empty,
                Kind = mapping.Kind,
                InstanceRoot = FormatRoot(mapping.InstanceRoot),
                LibManagarrRoot = FormatRoot(mapping.LibManagarrRoot)
            }).ToList(),
            ReaderViews = (input.ReaderViews ?? []).Select(view => new PathReaderView
            {
                Id = UuidV7.IsUuidV7(view.Id) ? view.Id.Trim() : UuidV7.Create(),
                Kind = view.Kind,
                Root = view.Root?.Trim() ?? string.Empty,
                Verification = view.Verification
            }).ToList(),
            Revision = Math.Max(0, input.Revision)
        };
    }

    public static Dictionary<string, string[]> Validate(
        SymlinkSettings? settings,
        IEnumerable<ArrInstanceSettings>? sonarrInstances,
        IEnumerable<ArrInstanceSettings>? radarrInstances)
    {
        Dictionary<string, string[]> errors = [];
        if (settings is null)
        {
            errors["symlinks"] = ["The symlink settings are required."];
            return errors;
        }

        if (settings.MediaRoot is not null)
        {
            string root = AppendRootErrors(errors, "symlinks.mediaRoot", settings.MediaRoot, "media", PathRole.LibManagarrSide);
            if (root.Length > 0 && ArrPathRules.TryParse(root, out ParsedPath? parsed, out _)
                && parsed!.Segments.Count == 0)
                errors["symlinks.mediaRoot"] = ["Choose a dedicated media directory, not the filesystem root."];
            if (settings.Revision < 0) errors["symlinks.revision"] = ["The revision must not be negative."];
            return errors;
        }
        AppendStorageAreaErrors(errors, settings.StorageAreas);
        AppendPathMappingErrors(errors, settings, sonarrInstances, radarrInstances);
        AppendReaderViewErrors(errors, settings.ReaderViews);
        if (settings.Revision < 0) errors["symlinks.revision"] = ["The revision must not be negative."];
        return errors;
    }

    /// <summary>
    /// True when the candidate would change what an existing path means, which is the only case in which
    /// the stored capability answers stop being usable. A save that changes nothing must not invalidate
    /// a proven test, or every unrelated settings write would make the setup look untested.
    /// </summary>
    public static bool RequiresNewRevision(SymlinkSettings stored, SymlinkSettings candidate) =>
        !string.Equals(PathsSignature(stored), PathsSignature(candidate), StringComparison.Ordinal);

    private static string PathsSignature(SymlinkSettings settings)
    {
        SymlinkSettings normalized = Normalize(settings);
        if (normalized.MediaRoot is not null) return $"shared:{normalized.MediaRoot}";
        // Only what a path resolves to. The display name of an area and the verification state of a
        // reader view are not paths, so neither may age out a probe that still describes the shelves.
        // One flattened sequence on purpose: three arguments would bind to Join(string, params object[])
        // and the signature would be the same text for every configuration.
        return string.Join('|',
            normalized.StorageAreas.Select(area => $"{area.MoviesRoot}\u0000{area.SeriesRoot}")
                .Concat(normalized.PathMappings.Select(mapping => $"{mapping.Kind}\u0000{mapping.InstanceId}\u0000{mapping.InstanceRoot}\u0000{mapping.LibManagarrRoot}"))
                .Concat(normalized.ReaderViews.Select(view => $"{view.Kind}\u0000{view.Root}")));
    }

    private static void AppendStorageAreaErrors(Dictionary<string, string[]> errors, List<MediaStorageArea>? areas)
    {
        List<MediaStorageArea> list = areas ?? [];
        if (list.Count > MaxStorageAreas)
        {
            errors["symlinks.storageAreas"] = [$"At most {MaxStorageAreas} storage areas are allowed."];
            return;
        }

        Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
        // Roots are collected per grammar because a Windows and a POSIX root string can never name the
        // same shelf, so comparing those two against each other would only produce noise.
        Dictionary<ArrPathSyntax, List<(string Root, string Label, string Kind)>> seenRoots = [];

        for (int index = 0; index < list.Count; index += 1)
        {
            MediaStorageArea area = list[index];
            string path = $"symlinks.storageAreas[{index}]";
            string label = string.IsNullOrWhiteSpace(area.Name) ? $"#{index}" : $"'{area.Name.Trim()}'";

            if (string.IsNullOrWhiteSpace(area.Name))
            {
                errors[$"{path}.name"] = ["The name is required."];
            }
            else if (area.Name.Trim().Length > MaxNameLength)
            {
                errors[$"{path}.name"] = [$"The name must not exceed {MaxNameLength} characters."];
            }
            else if (names.TryGetValue(area.Name.Trim(), out string? otherLabel))
            {
                errors[$"{path}.name"] = [$"The name is already used by storage area {otherLabel}."];
            }
            else
            {
                names[area.Name.Trim()] = label;
            }

            string moviesRoot = AppendRootErrors(errors, $"{path}.moviesRoot", area.MoviesRoot, "movie", PathRole.LibManagarrSide);
            string seriesRoot = AppendRootErrors(errors, $"{path}.seriesRoot", area.SeriesRoot, "series", PathRole.LibManagarrSide);
            if (moviesRoot.Length == 0 || seriesRoot.Length == 0) continue;

            if (ArrPathRules.IsWithinRoot(moviesRoot, seriesRoot) || ArrPathRules.IsWithinRoot(seriesRoot, moviesRoot))
            {
                errors[$"{path}.seriesRoot"] = ["The series root must not be the movie root or inside it, and vice versa."];
            }

            // Roots of the same kind have to stay apart, otherwise the area a path belongs to would be a
            // guess and the allow list would no longer be one.
            foreach ((string root, string kind, string errorPath) in new[]
            {
                (moviesRoot, "movie", $"{path}.moviesRoot"),
                (seriesRoot, "series", $"{path}.seriesRoot")
            })
            {
                ArrPathSyntax syntax = ArrPathRules.Detect(root);
                if (!seenRoots.TryGetValue(syntax, out List<(string Root, string Label, string Kind)>? sameSyntax))
                {
                    sameSyntax = [];
                    seenRoots[syntax] = sameSyntax;
                }

                string? overlap = sameSyntax
                    .Where(existing => existing.Kind == kind)
                    .Where(existing => ArrPathRules.IsWithinRoot(existing.Root, root) || ArrPathRules.IsWithinRoot(root, existing.Root))
                    .Select(existing => existing.Label)
                    .FirstOrDefault();
                if (overlap is not null)
                {
                    errors[errorPath] = [$"The {kind} root overlaps the {kind} root of storage area {overlap}."];
                }

                sameSyntax.Add((root, label, kind));
            }
        }
    }

    private static void AppendPathMappingErrors(
        Dictionary<string, string[]> errors,
        SymlinkSettings settings,
        IEnumerable<ArrInstanceSettings>? sonarrInstances,
        IEnumerable<ArrInstanceSettings>? radarrInstances)
    {
        List<ArrPathMapping> mappings = settings.PathMappings ?? [];
        if (mappings.Count > MaxPathMappings)
        {
            errors["symlinks.pathMappings"] = [$"At most {MaxPathMappings} path mappings are allowed."];
            return;
        }

        Dictionary<string, ArrInstanceSettings> sonarr = InstanceLookup(sonarrInstances);
        Dictionary<string, ArrInstanceSettings> radarr = InstanceLookup(radarrInstances);

        for (int index = 0; index < mappings.Count; index += 1)
        {
            ArrPathMapping mapping = mappings[index];
            string path = $"symlinks.pathMappings[{index}]";
            if (!Enum.IsDefined(mapping.Kind) || mapping.Kind == ArrApplicationKind.Seerr)
            {
                errors[$"{path}.kind"] = ["The kind must be 'Sonarr' or 'Radarr'."];
            }

            Dictionary<string, ArrInstanceSettings> instances = mapping.Kind == ArrApplicationKind.Radarr ? radarr : sonarr;
            if (string.IsNullOrWhiteSpace(mapping.InstanceId))
            {
                errors[$"{path}.instanceId"] = ["The instance id is required."];
            }
            else if (!instances.ContainsKey(mapping.InstanceId.Trim()))
            {
                errors[$"{path}.instanceId"] = [$"No {mapping.Kind} instance with this id is configured."];
            }

            string instanceRoot = AppendRootErrors(errors, $"{path}.instanceRoot", mapping.InstanceRoot, mapping.Kind.ToString(), PathRole.InstanceSide);
            string libManagarrRoot = AppendRootErrors(errors, $"{path}.libManagarrRoot", mapping.LibManagarrRoot, "LibManagarr", PathRole.LibManagarrSide);
            if (instanceRoot.Length == 0 || libManagarrRoot.Length == 0) continue;

            for (int other = 0; other < index; other += 1)
            {
                ArrPathMapping earlier = mappings[other];
                if (!string.Equals(earlier.InstanceId, mapping.InstanceId, StringComparison.OrdinalIgnoreCase) || earlier.Kind != mapping.Kind)
                {
                    continue;
                }

                // Two rules that start the same way and disagree about the target are the one case the
                // longest-prefix rule cannot resolve, so the configuration itself has to refuse it.
                if (ArrPathRules.TryParse(earlier.InstanceRoot, out ParsedPath? earlierRoot, out _) &&
                    ArrPathRules.TryParse(mapping.InstanceRoot, out ParsedPath? currentRoot, out _) &&
                    earlierRoot!.Segments.Count == currentRoot!.Segments.Count &&
                    earlierRoot.Syntax == currentRoot.Syntax &&
                    earlierRoot.Segments.Zip(currentRoot.Segments).All(pair =>
                        ArrPathRules.SegmentEquals(pair.First, pair.Second, earlierRoot.Syntax)) &&
                    !string.Equals(earlier.LibManagarrRoot, mapping.LibManagarrRoot, StringComparison.Ordinal))
                {
                    errors[$"{path}.instanceRoot"] =
                        [$"Mapping {other} already maps this instance root to '{earlier.LibManagarrRoot}', so the target is ambiguous."];
                }
            }

            // A mapping whose target no area covers could translate a path but never write a link under it.
            List<string> allowedRoots = mapping.Kind == ArrApplicationKind.Radarr
                ? (settings.StorageAreas ?? []).Select(area => area.MoviesRoot).ToList()
                : (settings.StorageAreas ?? []).Select(area => area.SeriesRoot).ToList();
            if (allowedRoots.Count == 0)
            {
                errors[$"{path}.libManagarrRoot"] = [$"A storage area with a {mapping.Kind} root is required."];
            }
            else if (!allowedRoots.Any(root => ArrPathRules.IsWithinRoot(root, libManagarrRoot)))
            {
                errors[$"{path}.libManagarrRoot"] = [$"'{libManagarrRoot}' is outside every configured {mapping.Kind} root."];
            }
        }
    }

    private static void AppendReaderViewErrors(Dictionary<string, string[]> errors, List<PathReaderView>? views)
    {
        List<PathReaderView> list = views ?? [];
        if (list.Count > MaxReaderViews)
        {
            errors["symlinks.readerViews"] = [$"At most {MaxReaderViews} reader views are allowed."];
            return;
        }

        List<string> kinds = [];
        for (int index = 0; index < list.Count; index += 1)
        {
            PathReaderView view = list[index];
            string path = $"symlinks.readerViews[{index}]";
            if (!Enum.IsDefined(view.Kind))
            {
                errors[$"{path}.kind"] = ["The kind must be 'Sonarr' or 'MediaPlayer'."];
                continue;
            }

            string kind = view.Kind.ToString();
            if (kinds.Contains(kind, StringComparer.OrdinalIgnoreCase))
            {
                errors[$"{path}.kind"] = [$"A {kind} reader view is already configured."];
            }
            else
            {
                kinds.Add(kind);
            }

            if (string.IsNullOrWhiteSpace(view.Root))
            {
                errors[$"{path}.root"] = ["The expected root is required."];
            }
            else if (!ArrPathRules.TryParse(view.Root, out ParsedPath? _, out string? failure))
            {
                errors[$"{path}.root"] = [failure!];
            }
        }
    }

    /// <summary>
    /// Checks one root and hands back the value to keep working with, or an empty string when it was
    /// refused. Deriving the expected grammar from the role instead of from the field name keeps the
    /// check honest when a new caller reuses it.
    /// </summary>
    private static string AppendRootErrors(
        Dictionary<string, string[]> errors, string path, string? value, string role, PathRole expectedRole)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[path] = [$"The {role} root is required."];
            return string.Empty;
        }

        if (!ArrPathRules.TryParse(value, out ParsedPath? parsed, out string? failure))
        {
            errors[path] = [failure!];
            return string.Empty;
        }

        ArrPathSyntax expected = ArrPathRules.ExpectedSyntax(expectedRole, value!);
        if (parsed!.Syntax != expected)
        {
            errors[path] = [$"The {role} root is written in {parsed.Syntax} syntax while this installation needs {expected} syntax."];
            return string.Empty;
        }

        if (ArrPathRules.IsFileSystemRoot(parsed))
        {
            errors[path] = [$"The {role} root must point below the file system root, not at the root itself."];
            return string.Empty;
        }

        return parsed.Format();
    }

    private static Dictionary<string, ArrInstanceSettings> InstanceLookup(IEnumerable<ArrInstanceSettings>? instances)
    {
        // Duplicate instance ids are refused by the instance validator, but they reach this lookup on the
        // way there, so the first one wins here instead of throwing and losing that report.
        Dictionary<string, ArrInstanceSettings> lookup = new(StringComparer.OrdinalIgnoreCase);
        foreach (ArrInstanceSettings instance in instances ?? [])
        {
            if (string.IsNullOrWhiteSpace(instance.Id)) continue;
            lookup.TryAdd(instance.Id.Trim(), instance);
        }

        return lookup;
    }

    private static string FormatRoot(string? value) =>
        ArrPathRules.TryParse(value, out ParsedPath? parsed, out _) ? parsed!.Format() : (value?.Trim() ?? string.Empty);
}
