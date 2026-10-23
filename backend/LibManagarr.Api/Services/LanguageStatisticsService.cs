using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public sealed class LanguageStatisticsService
{
    public LanguageStatisticsResponse Build(
        AppSettings settings,
        IReadOnlySet<string>? sonarrInstanceIds,
        IReadOnlySet<string>? radarrInstanceIds,
        Func<MediaFileSnapshot, bool>? fileFilter = null)
    {
        List<FileWithContext> files = [];
        files.AddRange(settings.SonarrSnapshots
            .Where((snapshot) => sonarrInstanceIds is null || sonarrInstanceIds.Contains(snapshot.InstanceId))
            .SelectMany((snapshot) => snapshot.Series.SelectMany((series) =>
                series.Seasons.SelectMany((season) =>
                    season.Episodes.Where(episode => episode.HasFile).Select((episode) => new FileWithContext(
                        episode.File,
                        new LanguageMediaFileItem
                        {
                            Title = $"{series.Title} - S{season.SeasonNumber:D2}E{episode.EpisodeNumber:D2} - {episode.Title}",
                            Source = $"Sonarr ({snapshot.InstanceName})",
                            InstanceName = snapshot.InstanceName,
                            TitleSlug = series.TitleSlug,
                            SizeBytes = episode.File.Size,
                            RelativePath = episode.File.RelativePath,
                            RootTitle = series.Title
                        }))))));

        files.AddRange(settings.RadarrSnapshots
            .Where((snapshot) => radarrInstanceIds is null || radarrInstanceIds.Contains(snapshot.InstanceId))
            .SelectMany((snapshot) => snapshot.Movies.Where(movie => movie.HasFile).Select((movie) => new FileWithContext(
                movie.File,
                new LanguageMediaFileItem
                {
                    Title = movie.Title,
                    Source = $"Radarr ({snapshot.InstanceName})",
                    InstanceName = snapshot.InstanceName,
                    TitleSlug = movie.TitleSlug,
                    SizeBytes = movie.File.Size,
                    RelativePath = movie.File.RelativePath,
                    RootTitle = movie.Title
                }))));

        List<FileWithContext> matchingFiles = files.Where(file => fileFilter is null || fileFilter(file.File)).ToList();
        return BuildCategories(matchingFiles, normalized: false);
    }

    internal static LanguageStatisticsResponse BuildForFiles(
        IReadOnlyList<(MediaFileSnapshot File, LanguageMediaFileItem MediaFile)> files, bool normalized) =>
        BuildCategories(files.Select(file => new FileWithContext(file.File, file.MediaFile)).ToList(), normalized);

    private static LanguageStatisticsResponse BuildCategories(IReadOnlyList<FileWithContext> files, bool normalized) => new()
    {
        AudioLanguages = BuildCategory(files, file => file.AudioLanguages, normalized),
        SubtitleLanguages = BuildCategory(files, file => file.SubtitleLanguages, normalized)
    };

    private static IReadOnlyList<LanguageStatisticItem> BuildCategory(
        IReadOnlyList<FileWithContext> files,
        Func<MediaFileSnapshot, IReadOnlyList<string>> selector, bool normalized)
    {
        List<(string Label, double Weight, LanguageMediaFileItem MediaFile)> values = files
            .SelectMany((file) => BuildWeightedLanguageValues(selector(file.File), normalized)
                .Select((entry) => (Label: entry.Label, Weight: entry.Weight, MediaFile: file.MediaFile)))
            .ToList();

        if (values.Count == 0)
        {
            return [];
        }

        double totalWeight = files.Count;

        List<LanguageStatisticItem> items = values
            .GroupBy((value) => value.Label, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending((group) => group.Count())
            .ThenBy((group) => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select((group) => new LanguageStatisticItem
            {
                Label = group.Key,
                Count = group.Count(),
                Percentage = Math.Round(group.Sum((entry) => entry.Weight) * 100d / totalWeight, 2),
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

    private static IReadOnlyList<WeightedLanguageValue> BuildWeightedLanguageValues(IReadOnlyList<string> rawValues, bool normalized)
    {
        IReadOnlyList<string> values = normalized ? rawValues : MediaFileLanguageHelper.CollectLanguageNames(rawValues.ToArray());
        if (values.Count == 0)
        {
            values = ["Unknown"];
        }

        double weight = 1d / values.Count;
        return values
            .Select((label) => new WeightedLanguageValue(label, weight))
            .ToList();
    }

    private sealed record WeightedLanguageValue(string Label, double Weight);

    private sealed record FileWithContext(MediaFileSnapshot File, LanguageMediaFileItem MediaFile);
}
