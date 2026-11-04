using System.Text.Json;

namespace LibManagarr.Api.Services.Backup;

/// <summary>
/// Reads the numbers a backup preview shows straight from the staged files. The live settings
/// service is deliberately not involved: its snapshot path normalizes and may rewrite
/// settings.json, and a preview must not change anything, least of all on the source system.
/// </summary>
internal static class BackupContentReader
{
    public static BackupSettingsSummary ReadSummary(string stagingRoot)
    {
        string settingsPath = Path.Combine(stagingRoot, BackupComponentNames.Settings);
        int sonarr = 0;
        int radarr = 0;
        bool seerr = false;
        if (File.Exists(settingsPath))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(settingsPath));
            JsonElement root = document.RootElement;
            sonarr = CountArray(root, "sonarrInstances");
            radarr = CountArray(root, "radarrInstances");
            seerr = root.TryGetProperty("seerr", out JsonElement seerrElement) && seerrElement.ValueKind == JsonValueKind.Object;
        }

        return new BackupSettingsSummary(sonarr, radarr, seerr);
    }

    public static BackupManifest ReadManifest(string stagingRoot)
    {
        string path = Path.Combine(stagingRoot, BackupManifestSerializer.EntryName);
        if (!File.Exists(path))
        {
            throw new BackupException(BackupErrorCodes.MissingComponent, "The backup carries no manifest.");
        }

        return BackupManifestSerializer.Deserialize(File.ReadAllBytes(path));
    }

    /// <summary>
    /// Checks the shape a settings document must have to be usable, on the staged copy only. The
    /// productive settings service is deliberately not asked: reading through it would normalize
    /// and could rewrite, and a preview must not change anything, least of all the live file.
    /// Returns a failure message, or null when the document can be stored as it is.
    /// </summary>
    public static string? ValidateSettings(string stagingRoot)
    {
        string settingsPath = Path.Combine(stagingRoot, BackupComponentNames.Settings);
        if (!File.Exists(settingsPath))
        {
            return "The backup carries no settings file, so no instance configuration could be restored.";
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllBytes(settingsPath));
        }
        catch (JsonException)
        {
            return "The settings file in the backup is not valid JSON.";
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return "The settings file in the backup is not a settings object.";
            }

            foreach (string arrayProperty in new[] { "sonarrInstances", "radarrInstances" })
            {
                if (root.TryGetProperty(arrayProperty, out JsonElement value) && value.ValueKind != JsonValueKind.Array)
                {
                    return $"The {arrayProperty} entry in the backup is not a list.";
                }
            }
        }

        return null;
    }

    private static int CountArray(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Array ? value.GetArrayLength() : 0;
}
