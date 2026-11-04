using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services.Backup;

namespace LibManagarr.Api.Services;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions FileSerializerOptions = new(SerializerOptions)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { OmitStoreOwnedProperties }
        }
    };

    private readonly BackupWriteGate storageLock;
    private readonly string settingsPath;
    private readonly LogFileService logFileService;
    private readonly IHistoryStore historyStore;
    private AppSettings? snapshot;

    public JsonSettingsService(StoragePaths storagePaths, LogFileService logFileService, IHistoryStore historyStore,
        BackupOperationCoordinator writes)
    {
        this.logFileService = logFileService;
        this.historyStore = historyStore;
        storageLock = new BackupWriteGate(writes);
        Directory.CreateDirectory(storagePaths.ConfigDirectory);
        settingsPath = Path.Combine(storagePaths.ConfigDirectory, "settings.json");
    }

    public async Task<AppSettings> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref snapshot) is { } current) return current;
        await storageLock.WaitAsync(cancellationToken);
        try
        {
            if (snapshot is not null) return snapshot;
            AppSettings settings;
            if (File.Exists(settingsPath))
            {
                await using (FileStream stream = File.OpenRead(settingsPath))
                {
                    settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, FileSerializerOptions, cancellationToken)
                        ?? AppSettingsDefaults.Create();
                }
                byte[] original = SerializeForFile(settings);
                settings = AppSettingsNormalizer.Normalize(settings);
                byte[] normalized = SerializeForFile(settings);
                if (!original.AsSpan().SequenceEqual(normalized))
                    await WriteAsync(normalized, cancellationToken);
            }
            else
            {
                settings = AppSettingsDefaults.Create();
                await WriteAsync(SerializeForFile(settings), cancellationToken);
            }
            settings.SonarrSnapshots = [.. await historyStore.GetSonarrInstanceSnapshotsAsync(cancellationToken)];
            settings.RadarrSnapshots = [.. await historyStore.GetRadarrInstanceSnapshotsAsync(cancellationToken)];
            settings.LibraryFilterValues = [.. await historyStore.GetLibraryFilterValuesAsync(cancellationToken)];
            logFileService.UpdateSettings(settings.Logging);
            Volatile.Write(ref snapshot, settings);
            return settings;
        }
        finally
        {
            storageLock.Release();
        }
    }

    public async Task<AppSettings> GetAsync(CancellationToken cancellationToken)
    {
        AppSettings current = await GetSnapshotAsync(cancellationToken);
        return JsonSerializer.Deserialize<AppSettings>(JsonSerializer.SerializeToUtf8Bytes(current, SerializerOptions), SerializerOptions)!;
    }

    public async Task<AppSettings> SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await storageLock.WaitAsync(cancellationToken);
        try
        {
            AppSettings normalized = AppSettingsNormalizer.Normalize(settings);
            byte[] bytes = SerializeForFile(normalized);
            AppSettings published = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.SerializeToUtf8Bytes(normalized, SerializerOptions), SerializerOptions)!;
            await WriteAsync(bytes, cancellationToken);
            await historyStore.ReplaceSonarrInstanceSnapshotsAsync(published.SonarrSnapshots, cancellationToken);
            await historyStore.ReplaceRadarrInstanceSnapshotsAsync(published.RadarrSnapshots, cancellationToken);
            await historyStore.ReplaceLibraryFilterValuesAsync(published.LibraryFilterValues, cancellationToken);
            Volatile.Write(ref snapshot, published);
            logFileService.UpdateSettings(published.Logging);
            return normalized;
        }
        finally
        {
            storageLock.Release();
        }
    }

    private static byte[] SerializeForFile(AppSettings settings) => JsonSerializer.SerializeToUtf8Bytes(settings, FileSerializerOptions);

    private static void OmitStoreOwnedProperties(JsonTypeInfo typeInfo)
    {
        string[] omittedProperties;
        if (typeInfo.Type == typeof(AppSettings))
        {
            omittedProperties = [nameof(AppSettings.SonarrSnapshots), nameof(AppSettings.RadarrSnapshots), nameof(AppSettings.LibraryFilterValues)];
        }
        else
        {
            return;
        }

        for (int index = typeInfo.Properties.Count - 1; index >= 0; index--)
        {
            // Depending on resolver internals the naming policy may or may not be applied to
            // JsonPropertyInfo.Name when modifiers run, so compare case-insensitively.
            string name = typeInfo.Properties[index].Name;
            if (omittedProperties.Any((omitted) => name.Equals(omitted, StringComparison.OrdinalIgnoreCase)))
            {
                typeInfo.Properties.RemoveAt(index);
            }
        }
    }

    private Task WriteAsync(byte[] bytes, CancellationToken cancellationToken) =>
        AtomicSettingsFile.WriteAsync(settingsPath, bytes, cancellationToken);
}
