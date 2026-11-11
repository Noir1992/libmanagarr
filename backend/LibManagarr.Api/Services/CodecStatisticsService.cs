using System.Runtime.CompilerServices;
using LibManagarr.Api.Models;
using LibManagarr.Api.Clients;

namespace LibManagarr.Api.Services;

public sealed class CodecStatisticsService
{
    private static readonly ConditionalWeakTable<AppSettings, FileIndex> Indexes = new();

    public CodecStatisticsResponse Build(
        AppSettings settings,
        IReadOnlySet<string>? sonarrInstanceIds,
        IReadOnlySet<string>? radarrInstanceIds,
        LibraryRequestFilter? filter = null)
    {
        FileIndex index = Indexes.GetValue(settings, BuildIndex);
        LibraryFilterMatcher? matcher = filter is null ? null : new(filter);
        HashSet<string> sonarrIds = settings.SonarrSnapshots.Select(snapshot => snapshot.InstanceId)
            .Where(id => (sonarrInstanceIds is null || sonarrInstanceIds.Contains(id)) && (matcher is null || matcher.MatchesInstance(id, true)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> radarrIds = settings.RadarrSnapshots.Select(snapshot => snapshot.InstanceId)
            .Where(id => (radarrInstanceIds is null || radarrInstanceIds.Contains(id)) && (matcher is null || matcher.MatchesInstance(id, false)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<FileWithContext> allFiles = index.Files;
        List<FileWithContext> files = allFiles.Where(file => (file.Sonarr ? sonarrIds : radarrIds).Contains(file.InstanceId)
            && (matcher is null || matcher.MatchesNormalizedMediaFile(file.NormalizedFile))).ToList();
        LanguageStatisticsResponse languages = LanguageStatisticsService.BuildForFiles(files.Select(file => (file.NormalizedFile, file.LanguageMediaFile)).ToList(), normalized: true);

        return new CodecStatisticsResponse
        {
            ContainerFormats = BuildCategory(files, (file) => MediaFileValueNormalizer.Container(file.Container)),
            Resolutions = BuildCategory(files, (file) => file.Resolution),
            DynamicRanges = BuildCategory(files, (file) => file.VideoFormat),
            VideoCodecs = BuildCategory(files, (file) => file.VideoCodec),
            AudioCodecs = BuildCategory(files, (file) => file.AudioCodec),
            VideoBitrates = BuildCategory(files, file => BitrateLabel(file.VideoBitrate)),
            AudioLanguages = languages.AudioLanguages,
            SubtitleLanguages = languages.SubtitleLanguages,
            FilterValues = index.Options,
            Counts = FilterCounts(files, settings)
        };
    }

    // Published settings snapshots are immutable; a replacement snapshot receives a new index.
    private static FileIndex BuildIndex(AppSettings settings)
    {
        List<FileWithContext> sonarrFiles = settings.SonarrSnapshots
            .SelectMany((snapshot) => snapshot.Series.SelectMany((series) =>
                series.Seasons.SelectMany((season) =>
                    season.Episodes.Where(episode => episode.HasFile).Select((episode) => new FileWithContext(
                        episode.File,
                        new CodecMediaFileItem
                        {
                            Title = $"{series.Title} - S{season.SeasonNumber:D2}E{episode.EpisodeNumber:D2} - {episode.Title}",
                            Source = $"Sonarr ({snapshot.InstanceName})",
                            InstanceName = snapshot.InstanceName,
                            TitleSlug = series.TitleSlug,
                            SizeBytes = episode.File.Size,
                            RelativePath = episode.File.RelativePath,
                            RootTitle = series.Title,
                            SeasonNumber = season.SeasonNumber,
                            EpisodeNumber = episode.EpisodeNumber,
                            Monitored = episode.Monitored,
                            AirDateUtc = episode.AirDateUtc,
                            QualityProfileName = series.Profile.Name,
                            UpgradeUntilCustomFormatScore = series.Profile.UpgradeUntilCustomFormatScore,
                            CustomFormatScore = episode.File.CustomFormatScore,
                            MediaDetails = LibraryMediaDetailsResponse.From(episode.File, true)
                        }, snapshot.InstanceId, true)))))
            .ToList();

        List<FileWithContext> radarrFiles = settings.RadarrSnapshots
            .SelectMany((snapshot) => snapshot.Movies.Where(movie => movie.HasFile).Select((movie) => new FileWithContext(
                movie.File,
                new CodecMediaFileItem
                {
                    Title = movie.Title,
                    Source = $"Radarr ({snapshot.InstanceName})",
                    InstanceName = snapshot.InstanceName,
                    TitleSlug = movie.TitleSlug,
                    SizeBytes = movie.File.Size,
                    RelativePath = movie.File.RelativePath,
                    RootTitle = movie.Title,
                    Monitored = movie.Monitored,
                    AirDateUtc = movie.AirDateUtc,
                    Status = movie.Status,
                    AddedAt = movie.AddedAt,
                    QualityProfileName = movie.Profile.Name,
                    UpgradeUntilCustomFormatScore = movie.Profile.UpgradeUntilCustomFormatScore,
                    PosterUrl = movie.PosterRemoteUrl,
                    Genres = movie.Genres,
                    Tags = movie.Tags,
                    CustomFormatScore = movie.File.CustomFormatScore,
                    MediaDetails = LibraryMediaDetailsResponse.From(movie.File, true)
                }, snapshot.InstanceId, false)))
            .ToList();

        List<FileWithContext> files = [.. sonarrFiles, .. radarrFiles];
        return new(files, FilterValues(files));
    }

    private static LibraryFilterValues FilterValues(IReadOnlyList<FileWithContext> files) => new()
    {
        Resolutions = files.Select(file => file.NormalizedFile.Resolution!).ToHashSet(StringComparer.OrdinalIgnoreCase),
        ContainerFormats = files.Select(file => file.NormalizedFile.Container!).ToHashSet(StringComparer.OrdinalIgnoreCase),
        VideoCodecs = files.Select(file => file.NormalizedFile.VideoCodec!).ToHashSet(StringComparer.OrdinalIgnoreCase),
        AudioCodecs = files.Select(file => file.NormalizedFile.AudioCodec!).ToHashSet(StringComparer.OrdinalIgnoreCase),
        DynamicRanges = files.Select(file => file.NormalizedFile.VideoFormat!).ToHashSet(StringComparer.OrdinalIgnoreCase),
        AudioLanguages = files.SelectMany(file => file.NormalizedFile.AudioLanguages).ToHashSet(StringComparer.OrdinalIgnoreCase),
        SubtitleLanguages = files.SelectMany(file => file.NormalizedFile.SubtitleLanguages).ToHashSet(StringComparer.OrdinalIgnoreCase)
    };

    private static Dictionary<string, Dictionary<string, int>> FilterCounts(IReadOnlyList<FileWithContext> files, AppSettings settings)
    {
        Dictionary<string, Dictionary<string, int>> counts = new()
        {
            ["resolution-filter"] = CountValues(files.Select(file => new[] { file.NormalizedFile.Resolution! })),
            ["container-format-filter"] = CountValues(files.Select(file => new[] { file.NormalizedFile.Container! })),
            ["video-codec-filter"] = CountValues(files.Select(file => new[] { file.NormalizedFile.VideoCodec! })),
            ["audio-codec-filter"] = CountValues(files.Select(file => new[] { file.NormalizedFile.AudioCodec! })),
            ["dynamic-range-filter"] = CountValues(files.Select(file => new[] { file.NormalizedFile.VideoFormat! })),
            ["audio-language-filter"] = CountValues(files.Select(file => file.NormalizedFile.AudioLanguages)),
            ["subtitle-language-filter"] = CountValues(files.Select(file => file.NormalizedFile.SubtitleLanguages))
        };
        counts["sonarr-instance-filter"] = InstanceCounts(files.Where(file => file.Sonarr), settings.SonarrInstances.Select(instance => instance.Id).Concat(settings.SonarrSnapshots.Select(snapshot => snapshot.InstanceId)));
        counts["radarr-instance-filter"] = InstanceCounts(files.Where(file => !file.Sonarr), settings.RadarrInstances.Select(instance => instance.Id).Concat(settings.RadarrSnapshots.Select(snapshot => snapshot.InstanceId)));
        return counts;
    }

    private static Dictionary<string, int> InstanceCounts(IEnumerable<FileWithContext> files, IEnumerable<string> ids)
    {
        List<FileWithContext> entries = files.ToList();
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase) { ["__all__"] = entries.Count };
        foreach (string id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
            counts[id] = entries.Count(file => file.InstanceId.Equals(id, StringComparison.OrdinalIgnoreCase));
        return counts;
    }

    private static Dictionary<string, int> CountValues(IEnumerable<IEnumerable<string>> values)
    {
        List<IEnumerable<string>> entries = values.ToList();
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase) { ["__all__"] = entries.Count };
        foreach (IEnumerable<string> entry in entries)
            foreach (string value in entry.Distinct(StringComparer.OrdinalIgnoreCase))
                counts[value] = counts.GetValueOrDefault(value) + 1;
        return counts;
    }

    private static string BitrateLabel(long? bitrate) => bitrate switch
    {
        null or <= 0 => MediaFileValueNormalizer.Unknown,
        < 2_000_000 => "< 2 Mbit/s",
        < 5_000_000 => "2–5 Mbit/s",
        < 10_000_000 => "5–10 Mbit/s",
        < 20_000_000 => "10–20 Mbit/s",
        < 40_000_000 => "20–40 Mbit/s",
        _ => "≥ 40 Mbit/s"
    };

    private static IReadOnlyList<CodecStatisticItem> BuildCategory(
        IReadOnlyList<FileWithContext> files,
        Func<MediaFileSnapshot, string?> selector)
    {
        List<(string Label, CodecMediaFileItem MediaFile)> values = files
            .Select((file) => (Label: MediaFileValueNormalizer.Value(selector(file.File)), MediaFile: file.MediaFile))
            .ToList();

        if (values.Count == 0)
        {
            return [];
        }

        int total = values.Count;

        List<CodecStatisticItem> items = values
            .GroupBy((value) => value.Label, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending((group) => group.Count())
            .ThenBy((group) => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select((group) => new CodecStatisticItem
            {
                Label = group.Key,
                Count = group.Count(),
                Percentage = Math.Round(group.Count() * 100d / total, 2),
                MediaFiles = group
                    .Select((entry) => entry.MediaFile)
                    .OrderBy((file) => file.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                TotalSizeBytes = group
                    .Where((entry) => entry.MediaFile.SizeBytes.HasValue && entry.MediaFile.SizeBytes.Value > 0)
                    .Sum((entry) => entry.MediaFile.SizeBytes!.Value)
            })
            .ToList();

        return items;
    }

    private sealed record FileIndex(IReadOnlyList<FileWithContext> Files, LibraryFilterValues Options);

    private sealed record FileWithContext(MediaFileSnapshot File, CodecMediaFileItem MediaFile, string InstanceId, bool Sonarr)
    {
        public MediaFileSnapshot NormalizedFile { get; } = new()
        {
            Resolution = MediaFileValueNormalizer.Resolution(File.Resolution),
            Container = MediaFileValueNormalizer.Container(File.Container),
            VideoCodec = MediaFileValueNormalizer.Value(File.VideoCodec),
            AudioCodec = MediaFileValueNormalizer.Value(File.AudioCodec),
            VideoFormat = MediaFileValueNormalizer.Value(File.VideoFormat),
            VideoBitrate = File.VideoBitrate,
            AudioLanguages = Languages(File.AudioLanguages).ToList(),
            SubtitleLanguages = Languages(File.SubtitleLanguages).ToList()
        };
        public LanguageMediaFileItem LanguageMediaFile { get; } = new()
        {
            Title = MediaFile.Title, Source = MediaFile.Source, InstanceName = MediaFile.InstanceName,
            TitleSlug = MediaFile.TitleSlug, SizeBytes = MediaFile.SizeBytes,
            RelativePath = MediaFile.RelativePath, RootTitle = MediaFile.RootTitle
        };

        private static IReadOnlyList<string> Languages(IReadOnlyList<string> raw)
        {
            List<string> values = MediaFileLanguageHelper.CollectLanguageNames(raw.ToArray());
            return values.Count == 0 ? ["Unknown"] : values;
        }
    }
}
