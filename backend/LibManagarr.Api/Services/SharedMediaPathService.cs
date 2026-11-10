using System.Collections.Concurrent;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>Discovers the allowed library roots in the common mount, without translating paths.</summary>
public sealed class SharedMediaPathService(RadarrClient radarr, SonarrClient sonarr)
{
    private readonly ConcurrentDictionary<(ArrApplicationKind Kind, string InstanceId), Lazy<Task<IReadOnlyList<ArrRootFolderResource>>>> roots = new();

    public async Task<IReadOnlyList<string>> GetRootsAsync(
        AppSettings settings, ArrApplicationKind kind, string instanceId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ArrInstanceSettings? instance = (kind == ArrApplicationKind.Radarr ? settings.RadarrInstances : settings.SonarrInstances)
            .FirstOrDefault(value => value.Id == instanceId);
        if (instance is null) throw new HttpRequestException($"The {kind} instance '{instanceId}' is not configured.");
        Task<IReadOnlyList<ArrRootFolderResource>> request = roots.GetOrAdd((kind, instanceId),
            _ => new Lazy<Task<IReadOnlyList<ArrRootFolderResource>>>(() => kind == ArrApplicationKind.Radarr
                ? radarr.FetchRootFoldersAsync(instance, token) : sonarr.FetchRootFoldersAsync(instance, token))).Value;
        IReadOnlyList<ArrRootFolderResource> response = await request;
        List<string> paths = [];
        foreach (string? folderPath in response.Select(folder => folder.Path))
        {
            if (!ArrPathRules.TryParse(folderPath ?? string.Empty, out ParsedPath? parsed, out string? failure)
                || parsed!.Syntax != ArrPathRules.RuntimeSyntax
                || !ArrPathRules.IsWithinRoot(settings.Symlinks.MediaRoot!, parsed.Format()))
                throw new HttpRequestException($"{instance.Name} reports root folder '{folderPath}' outside the shared media directory "
                    + $"'{settings.Symlinks.MediaRoot}'. Mount the same media tree at the same path in every container. {failure}");
            paths.Add(parsed.Format());
        }
        return paths.Distinct(StringComparer.Ordinal).ToArray();
    }

    public async Task<PathMappingResult> ValidatePathAsync(
        AppSettings settings, ArrApplicationKind kind, string instanceId, string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!ArrPathRules.TryParse(path, out ParsedPath? parsed, out string? failure)
            || parsed!.Syntax != ArrPathRules.RuntimeSyntax
            || !ArrPathRules.IsWithinRoot(settings.Symlinks.MediaRoot!, parsed.Format()))
            return PathMappingResult.Failed(failure ?? $"'{path}' is outside the shared media directory '{settings.Symlinks.MediaRoot}'.");
        try
        {
            IReadOnlyList<string> allowed = await GetRootsAsync(settings, kind, instanceId, token);
            return allowed.Any(root => ArrPathRules.IsWithinRoot(root, parsed.Format()))
                ? new PathMappingResult(parsed.Format(), null, null)
                : PathMappingResult.Failed($"'{path}' is outside the root folders reported by {kind} instance '{instanceId}'.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            token.ThrowIfCancellationRequested();
            return PathMappingResult.Failed($"Could not verify the {kind} root folders: {exception.Message}");
        }
    }
}
