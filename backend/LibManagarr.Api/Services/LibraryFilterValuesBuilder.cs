using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public static class LibraryFilterValuesBuilder
{
    public static LibraryFilterValues Build(string instanceId, RadarrInstanceSnapshot snapshot) =>
        Build(instanceId,
            snapshot.Movies.SelectMany(movie => movie.Genres),
            snapshot.Movies.SelectMany(movie => movie.Tags),
            snapshot.Movies.Select(movie => movie.Status),
            snapshot.Movies.Select(movie => movie.Profile.Name),
            snapshot.Movies.Where(movie => movie.HasFile).Select(movie => movie.File), LibraryFilterCountBuilder.Build(snapshot));

    public static LibraryFilterValues Build(string instanceId, SonarrInstanceSnapshot snapshot) =>
        Build(instanceId,
            snapshot.Series.SelectMany(series => series.Genres),
            snapshot.Series.SelectMany(series => series.Tags),
            snapshot.Series.Select(series => series.Status),
            snapshot.Series.Select(series => series.Profile.Name),
            snapshot.Series.SelectMany(series => series.Seasons).SelectMany(season => season.Episodes).Where(episode => episode.HasFile).Select(episode => episode.File),
            LibraryFilterCountBuilder.Build(snapshot));

    private static LibraryFilterValues Build(string instanceId, IEnumerable<string> genres, IEnumerable<string> tags,
        IEnumerable<string> statuses, IEnumerable<string> profiles, IEnumerable<MediaFileSnapshot> files,
        Dictionary<string, Dictionary<string, int>> counts)
    {
        List<MediaFileSnapshot> mediaFiles = files.ToList();
        return new LibraryFilterValues
        {
            InstanceId = instanceId,
            Version = LibraryFilterValues.CurrentVersion,
            Counts = counts,
            Resolutions = ToSet(mediaFiles.Select(file => MediaFileValueNormalizer.Resolution(file.Resolution))),
            VideoCodecs = ToSet(mediaFiles.Select(file => MediaFileValueNormalizer.Value(file.VideoCodec))),
            AudioCodecs = ToSet(mediaFiles.Select(file => MediaFileValueNormalizer.Value(file.AudioCodec))),
            DynamicRanges = ToSet(mediaFiles.Select(file => MediaFileValueNormalizer.Value(file.VideoFormat))),
            ContainerFormats = ToSet(mediaFiles.Select(file => MediaFileValueNormalizer.Container(file.Container))),

            Genres = ToSet(genres),
            Tags = ToSet(tags),
            Statuses = ToSet(statuses),
            QualityProfiles = ToSet(profiles),
            AudioLanguages = ToSet(mediaFiles.SelectMany(file => file.AudioLanguages)),
            SubtitleLanguages = ToSet(mediaFiles.SelectMany(file => file.SubtitleLanguages))
        };
    }

    private static HashSet<string> ToSet(IEnumerable<string> values) =>
        new(values.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase);
}
