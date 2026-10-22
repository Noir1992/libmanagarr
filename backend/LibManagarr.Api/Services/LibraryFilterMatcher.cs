using System.Globalization;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>Applies each library condition independently, then combines them with AND.</summary>
public sealed class LibraryFilterMatcher
{
    private readonly LibraryRequestFilter filter;
    private readonly HashSet<string> excludedFilters;
    private readonly bool missingEpisodesOnly;
    private readonly HashSet<string>? types;
    private readonly HashSet<string>? statuses;
    private readonly HashSet<string>? genres;
    private readonly HashSet<string>? tags;
    private readonly HashSet<string>? profiles;
    private readonly HashSet<string>? audioLanguages;
    private readonly HashSet<string>? subtitleLanguages;
    private readonly HashSet<string>? resolutions;
    private readonly HashSet<string>? videoCodecs;
    private readonly HashSet<string>? audioCodecs;
    private readonly HashSet<string>? dynamicRanges;
    private readonly HashSet<string>? containerFormats;
    private readonly DateTimeOffset? addedFrom;
    private readonly DateTimeOffset? addedTo;
    private readonly DateTimeOffset? airedFrom;
    private readonly DateTimeOffset? airedTo;

    public LibraryFilterMatcher(LibraryRequestFilter filter)
    {
        this.filter = filter;
        excludedFilters = new HashSet<string>(filter.ExcludedFilters, StringComparer.Ordinal);
        missingEpisodesOnly =
            (filter.FileStatusFilter == "missing" && !excludedFilters.Contains("file-status-filter"))
            || (filter.FileStatusFilter == "present" && excludedFilters.Contains("file-status-filter"));
        types = filter.Types is { Count: > 0 } ? ToSet(filter.Types) : null;
        statuses = ToSet(filter.Statuses);
        genres = ToSet(filter.Genres);
        tags = ToSet(filter.Tags);
        profiles = ToSet(filter.Profiles);
        audioLanguages = ToSet(filter.AudioLanguages);
        subtitleLanguages = ToSet(filter.SubtitleLanguages);
        HashSet<string>? selectedResolutions = ToSet(filter.Resolutions);
        resolutions = selectedResolutions is null ? null
            : new(selectedResolutions.Select(MediaFileValueNormalizer.Resolution), StringComparer.OrdinalIgnoreCase);
        videoCodecs = ToSet(filter.VideoCodecs);
        audioCodecs = ToSet(filter.AudioCodecs);
        dynamicRanges = ToSet(filter.DynamicRanges);
        containerFormats = ToSet(filter.ContainerFormats);
        addedFrom = ParseDate(filter.AddedFrom, false);
        addedTo = ParseDate(filter.AddedTo, true);
        airedFrom = ParseDate(filter.AiredFrom, false);
        airedTo = ParseDate(filter.AiredTo, true);
    }

    public bool MatchesInstance(string id, bool sonarr)
    {
        IReadOnlyList<string> selected = sonarr ? filter.SonarrInstanceFilter : filter.RadarrInstanceFilter;
        string filterId = sonarr ? "sonarr-instance-filter" : "radarr-instance-filter";
        bool selectedAll = selected.Contains("__all__");
        return Apply(filterId, !selectedAll || excludedFilters.Contains(filterId), selectedAll || selected.Contains(id));
    }

    public bool MatchesMediaFile(MediaFileSnapshot file) =>
        MatchList("audio-language-filter", audioLanguages, LanguageValues(file.AudioLanguages))
        && MatchList("subtitle-language-filter", subtitleLanguages, LanguageValues(file.SubtitleLanguages))
        && MatchesTechnicalMedia(file, true);

    internal bool MatchesNormalizedMediaFile(MediaFileSnapshot file) =>
        MatchList("audio-language-filter", audioLanguages, file.AudioLanguages)
        && MatchList("subtitle-language-filter", subtitleLanguages, file.SubtitleLanguages)
        && MatchesTechnicalMedia(file, true, normalized: true);

    private static IReadOnlyList<string> LanguageValues(IReadOnlyList<string> values)
    {
        List<string> languages = MediaFileLanguageHelper.CollectLanguageNames(values.ToArray());
        return languages.Count == 0 ? ["Unknown"] : languages;
    }

    public bool MatchesMovie(RadarrMovieSnapshot movie) =>
        MatchesAdded(movie.AddedAt)
        && MatchesMetadata(movie.Status, movie.Genres, movie.Tags, movie.Profile.Name)
        && MatchesMonitored(movie.Monitored)
        && MatchList("episode-type-filter", types, ["movie"])
        && MatchesAvailability(movie.HasFile)
        && MatchesMedia(movie.HasAired, movie.AirDateUtc, movie.File, movie.HasFile)
        && MatchesUpgradeTarget(movie.HasFile ? movie.File.CustomFormatScore : null, movie.Profile.UpgradeUntilCustomFormatScore);

    public bool MatchesSeries(SonarrSeriesSnapshot series, DateTimeOffset now) =>
        MatchesAdded(series.AddedAt)
        && MatchesMetadata(series.Status, series.Genres, series.Tags, series.Profile.Name)
        && MatchBoolean("monitored-filter", filter.MonitoredFilter, "monitored", "notMonitored", series.Monitored)
        && MatchesAvailability(LibraryAvailability.IsAvailable(series, now));

    public bool MatchesEpisode(SonarrEpisodeSnapshot episode, DateTimeOffset now, int? targetScore = null, int seasonNumber = 0,
        bool seriesHasNoFiles = false) =>
        (!missingEpisodesOnly || LibraryAvailability.IsMissing(episode, now) || (seriesHasNoFiles && !episode.HasFile))
        && MatchesMedia(episode.AirDateUtc.HasValue && episode.AirDateUtc.Value <= now, episode.AirDateUtc, episode.File, episode.HasFile)
        && MatchesMonitored(episode.Monitored)
        && MatchList("episode-type-filter", types, [seasonNumber > 0 ? "regular" : "special"])
        && MatchesUpgradeTarget(episode.HasFile ? episode.File.CustomFormatScore : null, targetScore);

    private bool MatchesAdded(DateTimeOffset? addedAt) =>
        Apply("added-range", addedFrom.HasValue || addedTo.HasValue,
            addedAt.HasValue
            && (!addedFrom.HasValue || addedAt.Value >= addedFrom.Value)
            && (!addedTo.HasValue || addedAt.Value <= addedTo.Value));

    private bool MatchesMonitored(bool monitored) =>
        MatchBoolean("episode-monitored-filter", filter.EpisodeMonitoredFilter, "monitored", "notMonitored", monitored);

    private bool MatchesAvailability(bool available) =>
        MatchBoolean("file-status-filter", filter.FileStatusFilter, "present", "missing", available);

    private bool MatchesUpgradeTarget(int? score, int? targetScore) =>
        Apply("upgrade-target-filter", filter.UpgradeTargetFilter is LibraryUpgradeTarget.Below or LibraryUpgradeTarget.Reached,
            LibraryUpgradeTarget.Status(score, targetScore) == filter.UpgradeTargetFilter);

    private bool MatchesMetadata(string status, IEnumerable<string> itemGenres, IEnumerable<string> itemTags, string profile) =>
        MatchList("status-filter", statuses, [status])
        && MatchList("genre-filter", genres, itemGenres)
        && MatchList("tag-filter", tags, itemTags)
        && MatchList("profile-filter", profiles, [profile]);

    private bool MatchesMedia(bool hasAired, DateTimeOffset? airDate, MediaFileSnapshot file, bool hasFile) =>
        MatchBoolean("aired-filter", filter.AiredFilter, "aired", "notAired", hasAired)
        && MatchList("audio-language-filter", audioLanguages, hasFile ? file.AudioLanguages : [])
        && MatchList("subtitle-language-filter", subtitleLanguages, hasFile ? file.SubtitleLanguages : [])
        && MatchesTechnicalMedia(file, hasFile)
        && Apply("score-range", filter.ScoreMin.HasValue || filter.ScoreMax.HasValue,
            hasFile && (!filter.ScoreMin.HasValue || file.CustomFormatScore >= filter.ScoreMin.Value)
            && (!filter.ScoreMax.HasValue || file.CustomFormatScore <= filter.ScoreMax.Value))
        && Apply("aired-range", airedFrom.HasValue || airedTo.HasValue,
            (!airedFrom.HasValue || (airDate.HasValue && airDate.Value >= airedFrom.Value))
            && (!airedTo.HasValue || (airDate.HasValue && airDate.Value <= airedTo.Value)));

    private bool MatchesTechnicalMedia(MediaFileSnapshot file, bool hasFile, bool normalized = false)
    {
        string resolution = normalized ? file.Resolution! : MediaFileValueNormalizer.Resolution(file.Resolution);
        string videoCodec = normalized ? file.VideoCodec! : MediaFileValueNormalizer.Value(file.VideoCodec);
        string audioCodec = normalized ? file.AudioCodec! : MediaFileValueNormalizer.Value(file.AudioCodec);
        string dynamicRange = normalized ? file.VideoFormat! : MediaFileValueNormalizer.Value(file.VideoFormat);
        string container = normalized ? file.Container! : MediaFileValueNormalizer.Container(file.Container);
        return MatchList("resolution-filter", resolutions, hasFile ? [resolution] : [])
        && MatchList("video-codec-filter", videoCodecs, hasFile ? [videoCodec] : [])
        && MatchList("audio-codec-filter", audioCodecs, hasFile ? [audioCodec] : [])
        && MatchList("dynamic-range-filter", dynamicRanges, hasFile ? [dynamicRange] : [])
        && MatchList("container-format-filter", containerFormats, hasFile ? [container] : [])
        && Apply("video-bitrate-range", filter.VideoBitrateMin.HasValue || filter.VideoBitrateMax.HasValue,
            hasFile && file.VideoBitrate is > 0
            && (!filter.VideoBitrateMin.HasValue || file.VideoBitrate.Value >= filter.VideoBitrateMin.Value)
            && (!filter.VideoBitrateMax.HasValue || file.VideoBitrate.Value <= filter.VideoBitrateMax.Value));
    }

    private bool MatchBoolean(string id, string? selection, string positive, string negative, bool value) =>
        Apply(id, selection == positive || selection == negative, selection == positive ? value : !value);

    private bool MatchList(string id, HashSet<string>? selection, IEnumerable<string> values) =>
        Apply(id, selection is not null, selection is not null && values.Any(selection.Contains));

    private bool Apply(string id, bool active, bool matches) =>
        !active || (excludedFilters.Contains(id) ? !matches : matches);

    private static HashSet<string>? ToSet(IReadOnlyList<string>? values) =>
        values is null || values.Contains("__all__") ? null : new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);

    private static DateTimeOffset? ParseDate(string? value, bool endOfDay)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
            return null;
        DateTimeOffset start = new(parsed.Date, parsed.Offset);
        return endOfDay ? start.AddDays(1).AddTicks(-1) : start;
    }
}
