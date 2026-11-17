using System.Globalization;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public sealed class LibraryChangeHistoryBuilder
{
    public static IReadOnlyList<LibraryChangeHistoryEntry> BuildSonarrEntries(
        ArrInstanceSettings instance, SonarrInstanceSnapshot currentSnapshot,
        SonarrInstanceSnapshot? previousSnapshot, IReadOnlyList<SonarrHistoryResource> historyResources)
    {
        Dictionary<int, FileInfo> current = SonarrFiles(currentSnapshot);
        Dictionary<int, FileInfo> previous = previousSnapshot is null ? [] : SonarrFiles(previousSnapshot);
        Dictionary<int, string> series = currentSnapshot.Series.ToDictionary(s => s.SeriesId, s => s.Title);
        return Build(instance, ArrApplicationKind.Sonarr, current, previous,
            historyResources.Select(h => new Event(h.Id, h.EpisodeId, h.Date, h.EventType, h.SourceTitle, h.Data,
                h.SeriesId.HasValue ? series.GetValueOrDefault(h.SeriesId.Value) : null)), previousSnapshot?.SyncedAt, previousSnapshot is null ? null : SonarrObservationTimes(previousSnapshot));
    }

    public static IReadOnlyList<LibraryChangeHistoryEntry> BuildRadarrEntries(
        ArrInstanceSettings instance, RadarrInstanceSnapshot currentSnapshot,
        RadarrInstanceSnapshot? previousSnapshot, IReadOnlyList<RadarrHistoryResource> historyResources) =>
        Build(instance, ArrApplicationKind.Radarr, RadarrFiles(currentSnapshot),
            previousSnapshot is null ? [] : RadarrFiles(previousSnapshot),
            historyResources.Select(h => new Event(h.Id, h.MovieId, h.Date, h.EventType, h.SourceTitle, h.Data, null)), previousSnapshot?.SyncedAt);

    private static IReadOnlyList<LibraryChangeHistoryEntry> Build(ArrInstanceSettings instance, ArrApplicationKind kind,
        Dictionary<int, FileInfo> current, Dictionary<int, FileInfo> previous, IEnumerable<Event> resources, DateTimeOffset? previousAt, Dictionary<int, DateTimeOffset>? previousTimes = null)
    {
        string prefix = kind == ArrApplicationKind.Sonarr ? "sonarr" : "radarr";
        Event[] events = resources.Where(h => h.Date.HasValue && h.EntityId is > 0 && Map(h.Type).HasValue)
            .OrderBy(h => h.Date).ThenBy(h => h.Id).ToArray();
        Dictionary<int, Event> lastImports = events.Where(h => Map(h.Type) == LibraryChangeType.Added)
            .GroupBy(h => h.EntityId!.Value).ToDictionary(g => g.Key, g => g.Last());
        List<LibraryChangeHistoryEntry> entries = [];
        Dictionary<int, long?> sizes = previous.ToDictionary(p => p.Key, p => p.Value.File.Size);
        HashSet<int> observedInBatch = [];
        foreach (Event history in events)
        {
            int id = history.EntityId!.Value;
            LibraryChangeType type = Map(history.Type)!.Value;
            FileInfo? now = current.GetValueOrDefault(id);
            FileInfo? before = previous.GetValueOrDefault(id);
            string? fileId = Value(history.Data, "FileId");
            string? path = Value(history.Data, "ImportedPath") ?? Value(history.Data, "Path");
            long? size = ParseSize(Value(history.Data, "Size"));
            if (type == LibraryChangeType.Deleted)
            {
                DateTimeOffset? observedAt = previousTimes?.GetValueOrDefault(id) ?? previousAt;
                if (observedInBatch.Contains(id) || !observedAt.HasValue || history.Date >= observedAt)
                    size ??= sizes.GetValueOrDefault(id);
                path = history.Title ?? before?.File.RelativePath;
            }
            else if (type == LibraryChangeType.Added && ReferenceEquals(lastImports.GetValueOrDefault(id), history)
                && now is not null && MatchesCurrentFile(now.File, fileId, path))
            {
                size ??= now.File.Size;
            }
            else if (type == LibraryChangeType.Changed)
            {
                size ??= sizes.GetValueOrDefault(id);
            }

            entries.Add(new LibraryChangeHistoryEntry
            {
                SourceKey = history.Id.HasValue ? $"{prefix}:{history.Id}" :
                    $"{prefix}:{history.Type}:{id}:{history.Date!.Value.ToUniversalTime():O}:{history.Title}",
                Kind = kind,
                InstanceId = instance.Id,
                InstanceName = instance.Name,
                OccurredAt = history.Date!.Value,
                MediaKey = $"{prefix}:{instance.Id}:{id}",
                ChangeType = type,
                EntityTitle = now?.Title ?? before?.Title ?? history.Title ?? "Unknown media",
                SeriesTitle = history.SeriesTitle ?? now?.SeriesTitle ?? before?.SeriesTitle,
                FilePath = path ?? (type == LibraryChangeType.Deleted ? before?.File.RelativePath : now?.File.RelativePath),
                SizeBytes = size,
                DeltaSizeBytes = type switch
                {
                    LibraryChangeType.Deleted => -(size ?? 0),
                    LibraryChangeType.Added => size ?? 0,
                    _ => 0
                },
                IsSizeDeltaKnown = type == LibraryChangeType.Changed || size.HasValue,
                HistoryId = history.Id,
                FileId = fileId,
                EventData = history.Data,
                SourceTitle = history.Title,
                EventType = history.Type,
                DeletionReason = Value(history.Data, "Reason")
            });
            observedInBatch.Add(id);
            if (type == LibraryChangeType.Deleted) sizes.Remove(id);
            else if (type == LibraryChangeType.Added) sizes[id] = size;
        }
        return entries;
    }

    private static bool MatchesCurrentFile(MediaFileSnapshot file, string? fileId, string? path)
    {
        if (fileId is not null) return file.Id?.ToString(CultureInfo.InvariantCulture) == fileId;
        return path is null || string.Equals(path, file.Path, StringComparison.Ordinal)
            || string.Equals(path, file.RelativePath, StringComparison.Ordinal);
    }

    private static string? Value(Dictionary<string, string>? data, string key) =>
        data?.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    private static long? ParseSize(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long size) && size >= 0 ? size : null;

    private static LibraryChangeType? Map(string? type) => type switch
    {
        "downloadFolderImported" or "seriesFolderImported" or "movieFolderImported" => LibraryChangeType.Added,
        "episodeFileDeleted" or "movieFileDeleted" => LibraryChangeType.Deleted,
        "episodeFileRenamed" or "movieFileRenamed" => LibraryChangeType.Changed,
        _ => null
    };

    internal static string BuildSonarrMediaKey(string instanceId, int episodeId) => $"sonarr:{instanceId}:{episodeId}";
    internal static string BuildRadarrMediaKey(string instanceId, int movieId) => $"radarr:{instanceId}:{movieId}";

    internal static Dictionary<int, DateTimeOffset> SonarrObservationTimes(SonarrInstanceSnapshot snapshot) => snapshot.Series
        .SelectMany(series => series.Seasons.SelectMany(season => season.Episodes).Where(episode => episode.EpisodeId > 0)
            .Select(episode => new
            {
                episode.EpisodeId,
                At = snapshot.SeriesSyncTimes.GetValueOrDefault(series.SeriesId,
                snapshot.LastFullSyncAt ?? snapshot.SyncedAt)
            })).ToDictionary(e => e.EpisodeId, e => e.At);

    internal static Dictionary<int, FileInfo> SonarrFiles(SonarrInstanceSnapshot snapshot) => snapshot.Series
        .SelectMany(s => s.Seasons.SelectMany(season => season.Episodes)
            .Where(e => e.HasFile && e.EpisodeId > 0).Select(e => new { e.EpisodeId, Info = new FileInfo(e.Title, s.Title, e.File) }))
        .ToDictionary(e => e.EpisodeId, e => e.Info);

    internal static Dictionary<int, FileInfo> RadarrFiles(RadarrInstanceSnapshot snapshot) => snapshot.Movies
        .Where(m => m.HasFile && m.MovieId > 0).ToDictionary(m => m.MovieId, m => new FileInfo(m.Title, null, m.File));

    internal sealed record FileInfo(string Title, string? SeriesTitle, MediaFileSnapshot File);
    private sealed record Event(int? Id, int? EntityId, DateTimeOffset? Date, string? Type, string? Title,
        Dictionary<string, string>? Data, string? SeriesTitle);
}
