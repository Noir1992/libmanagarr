using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public static class LibraryFilterCountBuilder
{
    public const string All = "__all__";

    public static Dictionary<string, Dictionary<string, int>> Build(RadarrInstanceSnapshot snapshot)
    {
        List<RadarrMovieSnapshot> movies = snapshot.Movies;
        Dictionary<string, Dictionary<string, int>> counts = Metadata(
            movies.Select(movie => movie.Genres), movies.Select(movie => movie.Tags),
            movies.Select(movie => movie.Status), movies.Select(movie => movie.Profile.Name),
            movies.Select(movie => movie.Monitored));
        counts.Remove("monitored-filter");
        counts["episode-monitored-filter"] = Count(movies.Select(movie => new[] { movie.Monitored ? "monitored" : "notMonitored" }));
        counts["episode-type-filter"] = Count(movies.Select(_ => new[] { "movie" }));
        AddMedia(counts, movies.Where(movie => movie.HasFile).Select(movie => LibraryMediaDetailsResponse.From(movie.File, true)!));
        counts["upgrade-target-filter"] = Count(movies.Select(movie => UpgradeValues(movie.File, movie.Profile, movie.HasFile)));
        counts["file-status-filter"] = Count(movies.Select(movie => new[] { movie.HasFile ? "present" : "missing" }));
        counts["aired-filter"] = Count(movies.Select(movie => new[] { movie.HasAired ? "aired" : "notAired" }));
        return counts;
    }

    public static Dictionary<string, Dictionary<string, int>> Build(SonarrInstanceSnapshot snapshot)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<SonarrSeriesSnapshot> series = snapshot.Series
            .Where(item => item.Seasons.Any(season => season.Episodes.Count > 0)).ToList();
        List<SonarrEpisodeSnapshot> episodes = series.SelectMany(item => item.Seasons)
            .SelectMany(season => season.Episodes).ToList();
        Dictionary<string, Dictionary<string, int>> counts = Metadata(
            series.Select(item => item.Genres), series.Select(item => item.Tags),
            series.Select(item => item.Status), series.Select(item => item.Profile.Name),
            series.Select(item => item.Monitored));
        AddMedia(counts, episodes.Where(episode => episode.HasFile).Select(episode => LibraryMediaDetailsResponse.From(episode.File, true)!));
        counts["upgrade-target-filter"] = Count(series.SelectMany(item => item.Seasons
            .SelectMany(season => season.Episodes).Select(episode => UpgradeValues(episode.File, item.Profile, episode.HasFile))));
        counts["file-status-filter"] = Count(series.Select(item => new[] { LibraryAvailability.IsAvailable(item, now) ? "present" : "missing" }));
        counts["episode-monitored-filter"] = Count(episodes.Select(episode => new[] { episode.Monitored ? "monitored" : "notMonitored" }));
        counts["episode-type-filter"] = Count(series.SelectMany(item => item.Seasons.SelectMany(season => season.Episodes.Select(_ => new[] { season.SeasonNumber > 0 ? "regular" : "special" }))));
        counts["aired-filter"] = Count(episodes.Select(episode => new[]
        {
            episode.AirDateUtc.HasValue && episode.AirDateUtc.Value <= now ? "aired" : "notAired"
        }));
        return counts;
    }

    public static Dictionary<string, Dictionary<string, int>> BuildRadarrResults(
        IReadOnlyList<RadarrLibraryInstanceResult> instances, IEnumerable<string> instanceIds)
    {
        List<RadarrLibraryEntryResponse> movies = instances.SelectMany(instance => instance.Rows).ToList();
        Dictionary<string, Dictionary<string, int>> counts = Metadata(
            movies.Select(movie => movie.Genres), movies.Select(movie => movie.Tags),
            movies.Select(movie => movie.Status), movies.Select(movie => movie.QualityProfileName),
            movies.Select(movie => movie.Monitored));
        counts.Remove("monitored-filter");
        counts["episode-monitored-filter"] = Count(movies.Select(movie => new[] { movie.Monitored ? "monitored" : "notMonitored" }));
        counts["episode-type-filter"] = Count(movies.Select(_ => new[] { "movie" }));
        counts["file-status-filter"] = Count(movies.Select(movie => new[] { movie.HasFile ? "present" : "missing" }));
        counts["aired-filter"] = Count(movies.Select(movie => new[] { movie.HasAired ? "aired" : "notAired" }));
        counts["upgrade-target-filter"] = Count(movies.Select(movie => UpgradeValues(movie.CustomFormatScore, movie.UpgradeUntilCustomFormatScore)));
        AddMedia(counts, movies.Where(movie => movie.HasFile).Select(movie => movie.MediaDetails ?? new()));
        Dictionary<string, int> instanceCounts = instanceIds.ToDictionary(id => id,
            id => instances.FirstOrDefault(instance => instance.InstanceId == id)?.Rows.Count ?? 0, StringComparer.OrdinalIgnoreCase);
        instanceCounts[All] = movies.Count;
        counts["radarr-instance-filter"] = instanceCounts;
        return counts;
    }

    public static Dictionary<string, Dictionary<string, int>> BuildSonarrResults(
        IReadOnlyList<SonarrLibraryInstanceResult> instances, IEnumerable<string> instanceIds)
    {
        List<SonarrLibraryEntryResponse> series = instances.SelectMany(instance => instance.Rows).ToList();
        List<SonarrEpisodeResponse> episodes = series.SelectMany(item => item.Seasons).SelectMany(season => season.Episodes).ToList();
        Dictionary<string, Dictionary<string, int>> counts = Metadata(
            series.Select(item => item.Genres), series.Select(item => item.Tags),
            series.Select(item => item.Status), series.Select(item => item.QualityProfileName),
            series.Select(item => item.Monitored));
        counts["episode-monitored-filter"] = Count(episodes.Select(episode => new[] { episode.Monitored ? "monitored" : "notMonitored" }));
        counts["episode-type-filter"] = Count(series.SelectMany(item => item.Seasons.SelectMany(season =>
            season.Episodes.Select(_ => new[] { season.Number > 0 ? "regular" : "special" }))));
        counts["file-status-filter"] = Count(series.Select(item => new[] { item.IsAvailable ? "present" : "missing" }));
        counts["aired-filter"] = Count(episodes.Select(episode => new[] { episode.HasAired ? "aired" : "notAired" }));
        counts["upgrade-target-filter"] = Count(series.SelectMany(item => item.Seasons.SelectMany(season =>
            season.Episodes.Select(episode => UpgradeValues(episode.CustomFormatScore, item.UpgradeUntilCustomFormatScore)))));
        AddMedia(counts, episodes.Where(episode => episode.HasFile).Select(episode => episode.MediaDetails ?? new()));
        Dictionary<string, int> instanceCounts = instanceIds.ToDictionary(id => id,
            id => instances.FirstOrDefault(instance => instance.InstanceId == id)?.Rows.Count ?? 0, StringComparer.OrdinalIgnoreCase);
        instanceCounts[All] = series.Count;
        counts["sonarr-instance-filter"] = instanceCounts;
        return counts;
    }

    public static void MergeInto(Dictionary<string, Dictionary<string, int>> target,
        IReadOnlyDictionary<string, Dictionary<string, int>> source)
    {
        foreach ((string filterId, Dictionary<string, int> options) in source)
        {
            if (!target.TryGetValue(filterId, out Dictionary<string, int>? merged))
            {
                merged = new(StringComparer.OrdinalIgnoreCase);
                target[filterId] = merged;
            }
            foreach ((string value, int count) in options)
                merged[value] = merged.GetValueOrDefault(value) + count;
        }
    }

    private static Dictionary<string, Dictionary<string, int>> Metadata(
        IEnumerable<IEnumerable<string>> genres, IEnumerable<IEnumerable<string>> tags,
        IEnumerable<string> statuses, IEnumerable<string> profiles, IEnumerable<bool> monitored) => new()
    {
        ["genre-filter"] = Count(genres),
        ["tag-filter"] = Count(tags),
        ["status-filter"] = Count(statuses.Select(value => new[] { value })),
        ["profile-filter"] = Count(profiles.Select(value => new[] { value })),
        ["monitored-filter"] = Count(monitored.Select(value => new[] { value ? "monitored" : "notMonitored" }))
    };

    private static void AddMedia(Dictionary<string, Dictionary<string, int>> counts, IEnumerable<LibraryMediaDetailsResponse> files)
    {
        List<LibraryMediaDetailsResponse> media = files.ToList();
        counts["resolution-filter"] = Count(media.Select(file => new[] { MediaFileValueNormalizer.Resolution(file.Resolution) }));
        counts["video-codec-filter"] = Count(media.Select(file => new[] { MediaFileValueNormalizer.Value(file.VideoCodec) }));
        counts["audio-codec-filter"] = Count(media.Select(file => new[] { MediaFileValueNormalizer.Value(file.AudioCodec) }));
        counts["dynamic-range-filter"] = Count(media.Select(file => new[] { MediaFileValueNormalizer.Value(file.DynamicRange) }));
        counts["container-format-filter"] = Count(media.Select(file => new[] { MediaFileValueNormalizer.Container(file.ContainerFormat) }));
        counts["audio-language-filter"] = Count(media.Select(file => file.AudioLanguages));
        counts["subtitle-language-filter"] = Count(media.Select(file => file.SubtitleLanguages));
    }

    private static IEnumerable<string> UpgradeValues(MediaFileSnapshot file, MediaProfileSnapshot profile, bool hasFile)
        => UpgradeValues(hasFile ? file.CustomFormatScore : null, profile.UpgradeUntilCustomFormatScore);

    private static IEnumerable<string> UpgradeValues(int? score, int? target)
    {
        string? status = LibraryUpgradeTarget.Status(score, target);
        return status is null ? [] : [status];
    }

    private static Dictionary<string, int> Count(IEnumerable<IEnumerable<string>> items)
    {
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase) { [All] = 0 };
        foreach (IEnumerable<string> values in items)
        {
            counts[All]++;
            foreach (string value in values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
                counts[value] = counts.GetValueOrDefault(value) + 1;
        }
        return counts;
    }
}
