using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using LibManagarr.Api.Models;
using LibManagarr.Api.Services;

namespace LibManagarr.Api.Endpoints;

public static class SonarrEndpoints
{
    public static IEndpointRouteBuilder MapSonarrEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/sonarr/instances");
        group.RequireAuthorization();

        group.MapGet("/{instanceId}/library", GetLibraryAsync);
        group.MapPost("/library", PostLibraryAsync);
        group.MapPost("/library/filter-values", PostFilterValuesAsync);
        group.MapGet("/{instanceId}/poster/{seriesId:int}", GetPosterAsync);

        return endpoints;
    }

    private static async Task<Results<Ok<SonarrLibraryFilterValuesResponse>, NotFound<ProblemDetails>>>
        PostFilterValuesAsync(
            FilterValuesRequest request,
            ISettingsService settingsService = null!)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken: CancellationToken.None);

        IEnumerable<ArrInstanceSettings> instances = settings.SonarrInstances
            .Where(i => request.InstanceIds.Contains("__all__") || request.InstanceIds.Contains(i.Id));

        if (!instances.Any())
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Sonarr instance not found",
                Detail = $"No Sonarr instances found for the requested instance IDs.",
                Status = StatusCodes.Status404NotFound
            });
        }

        Dictionary<string, Dictionary<string, int>> allCounts = [];
        HashSet<string> allResolutions = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allVideoCodecs = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allAudioCodecs = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allDynamicRanges = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allContainerFormats = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allGenres = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allTags = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allStatuses = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allProfiles = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allAudioLangs = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> allSubtitleLangs = new(StringComparer.OrdinalIgnoreCase);

        foreach (string instanceId in instances.Select(instance => instance.Id))
        {
            LibraryFilterValues? fv = settings.LibraryFilterValues.FirstOrDefault(f => f.InstanceId == instanceId);
            if ((fv is null || fv.Version < LibraryFilterValues.CurrentVersion) && settings.SonarrSnapshots.FirstOrDefault(snapshot => snapshot.InstanceId == instanceId) is { } snapshot)
            {
                fv = LibraryFilterValuesBuilder.Build(instanceId, snapshot);
            }
            if (fv is not null)
            {
                LibraryFilterCountBuilder.MergeInto(allCounts, fv.Counts);
                allResolutions.UnionWith(fv.Resolutions);
                allVideoCodecs.UnionWith(fv.VideoCodecs);
                allAudioCodecs.UnionWith(fv.AudioCodecs);
                allDynamicRanges.UnionWith(fv.DynamicRanges);
                allContainerFormats.UnionWith(fv.ContainerFormats);
                foreach (string g in fv.Genres) allGenres.Add(g);
                foreach (string t in fv.Tags) allTags.Add(t);
                foreach (string s in fv.Statuses) allStatuses.Add(s);
                foreach (string p in fv.QualityProfiles) allProfiles.Add(p);
                foreach (string a in fv.AudioLanguages) allAudioLangs.Add(a);
                foreach (string sl in fv.SubtitleLanguages) allSubtitleLangs.Add(sl);
            }
        }

        Dictionary<string, int> instanceCounts = new(StringComparer.OrdinalIgnoreCase);
        foreach (string instanceId in settings.SonarrInstances.Select(instance => instance.Id))
        {
            SonarrInstanceSnapshot? instanceSnapshot = settings.SonarrSnapshots.FirstOrDefault(snapshot => snapshot.InstanceId == instanceId);
            if (instanceSnapshot is not null)
                instanceCounts[instanceId] = instanceSnapshot.Series.Count(series => series.Seasons.Any(season => season.Episodes.Count > 0));
        }
        instanceCounts[LibraryFilterCountBuilder.All] = instanceCounts.Values.Sum();
        allCounts["sonarr-instance-filter"] = instanceCounts;

        return TypedResults.Ok(new SonarrLibraryFilterValuesResponse
        {
            Counts = allCounts,
            Resolutions = allResolutions.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            VideoCodecs = allVideoCodecs.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            AudioCodecs = allAudioCodecs.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            DynamicRanges = allDynamicRanges.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            ContainerFormats = allContainerFormats.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            Genres = allGenres.ToList(),
            Tags = allTags.ToList(),
            Statuses = allStatuses.ToList(),
            QualityProfiles = allProfiles.ToList(),
            AudioLanguages = allAudioLangs.ToList(),
            SubtitleLanguages = allSubtitleLangs.ToList(),
        });
    }

    private sealed class FilterValuesRequest
    {
        public IReadOnlyList<string> InstanceIds { get; set; } = [];
    }

    private static async Task<Results<Ok<SonarrLibraryBatchResponse>, NotFound<ProblemDetails>>> PostLibraryAsync(
        LibraryRequestFilter filter,
        ISettingsService settingsService = null!,
        CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);

        LibraryFilterMatcher matcher = new(filter);

        IEnumerable<ArrInstanceSettings> instances = settings.SonarrInstances
            .Where(i => matcher.MatchesInstance(i.Id, true));

        if (!instances.Any())
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Sonarr instance not found",
                Detail = "No Sonarr instances found for the requested instance IDs.",
                Status = StatusCodes.Status404NotFound
            });
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Process each instance
        DateTimeOffset? syncedAt = null;
        List<SonarrLibraryInstanceResult> instanceResults = [];

        foreach (ArrInstanceSettings instance in instances)
        {
            SonarrInstanceSnapshot? snapshot =
                settings.SonarrSnapshots.FirstOrDefault(s => s.InstanceId == instance.Id);

            if (snapshot is not null && syncedAt is null)
                syncedAt = snapshot.SyncedAt;

            IReadOnlyList<SonarrLibraryEntryResponse> rows = snapshot is null
                ? []
                : LibrarySorter.SortSonarr(snapshot.Series
                    .Where(series => matcher.MatchesSeries(series, now))
                    .Select(s =>
                    {
                        bool seriesHasNoFiles = !s.Seasons.SelectMany(season => season.Episodes).Any(episode => episode.HasFile);
                        IReadOnlyList<SonarrSeasonResponse> filteredSeasons = s.Seasons
                            .Select(season =>
                            {
                                SonarrEpisodeResponse[] filteredEpisodes = season.Episodes
                                    .Where(e => matcher.MatchesEpisode(e, now, s.Profile.UpgradeUntilCustomFormatScore, season.SeasonNumber, seriesHasNoFiles))
                                    .Select(e => new SonarrEpisodeResponse
                                    {
                                        Number = e.EpisodeNumber,
                                        EpisodeId = e.EpisodeId,
                                        HasAired = e.AirDateUtc.HasValue && e.AirDateUtc.Value <= now,
                                        MediaDetails = LibraryMediaDetailsResponse.From(e.File, e.HasFile),
                                        Title = e.Title,
                                        AirDateUtc = e.AirDateUtc,
                                        HasFile = e.HasFile,
                                        Monitored = e.Monitored,
                                        CustomFormatScore = e.HasFile ? e.File.CustomFormatScore : null,
                                    })
                                    .OrderBy(ep => ep.Number)
                                    .ToArray();

                                return new SonarrSeasonResponse
                                {
                                    Number = season.SeasonNumber,
                                    Monitored = season.Monitored,
                                    Episodes = filteredEpisodes,
                                };
                            })
                            .Where(season => season.Episodes.Any())
                            .OrderBy(season => season.Number)
                            .ToArray();

                        if (!filteredSeasons.Any())
                            return null;

                        return new SonarrLibraryEntryResponse
                        {
                            InstanceName = snapshot.InstanceName,
                            SeriesId = s.SeriesId,
                            SeriesTitle = s.Title,
                            SeriesTitleSlug = s.TitleSlug,
                            QualityProfileName = s.Profile.Name,
                            UpgradeUntilCustomFormatScore = s.Profile.UpgradeUntilCustomFormatScore,
                            Status = s.Status,
                            AddedAt = s.AddedAt,
                            LibraryStatus = LibraryAvailability.GetStatus(s).ToWireValue(),
                            Genres = s.Genres,
                            Tags = s.Tags,
                            LibraryEpisodeCount = s.Seasons.Sum(season => season.Episodes.Count),
                            FileCount = s.Seasons.Sum(season => season.Episodes.Count(episode => episode.HasFile)),
                            Monitored = s.Monitored,
                            Seasons = filteredSeasons,
                            TotalSize = LibrarySizeCalculator.SeriesSize(s),
                            TotalEpisodes = filteredSeasons.Sum(season => season.Episodes.Count),
                            AiredEpisodes = filteredSeasons.Sum(season => season.Episodes.Count(e => e.AirDateUtc.HasValue && e.AirDateUtc.Value <= now)),
                        };
                    })
                    .Where(entry => entry is not null)
                    .Cast<SonarrLibraryEntryResponse>(), filter, settings.TitleSorting);

            instanceResults.Add(new SonarrLibraryInstanceResult
            {
                InstanceId = instance.Id,
                InstanceName = instance.Name,
                Rows = rows
            });
        }

        return TypedResults.Ok(new SonarrLibraryBatchResponse
        {
            SyncedAt = syncedAt,
            Counts = LibraryFilterCountBuilder.BuildSonarrResults(instanceResults, settings.SonarrInstances.Select(instance => instance.Id)),
            Instances = instanceResults
        });
    }

    private static async Task<Results<Ok<SonarrLibraryResponse>, NotFound<ProblemDetails>>> GetLibraryAsync(
        string instanceId,
        string? airedFilter = null,
        string? monitoredFilter = null,
        string? statuses = null,
        string? genres = null,
        string? tags = null,
        string? profiles = null,
        string? audioLanguages = null,
        string? subtitleLanguages = null,
        int? scoreMin = null,
        int? scoreMax = null,
        string? airedFrom = null,
        string? airedTo = null,
        ISettingsService settingsService = null!,
        CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        ArrInstanceSettings? instance =
            settings.SonarrInstances.FirstOrDefault((currentInstance) => currentInstance.Id == instanceId);

        if (instance is null)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Sonarr instance not found",
                Detail = $"No Sonarr instance exists for id '{instanceId}'.",
                Status = StatusCodes.Status404NotFound
            });
        }

        SonarrInstanceSnapshot? snapshot =
            settings.SonarrSnapshots.FirstOrDefault((currentSnapshot) => currentSnapshot.InstanceId == instanceId);

        // Parse comma-separated filter values into case-insensitive sets
        static HashSet<string>? ParseSet(string? value)
        {
            if (value == null)
                return null;

            string[] parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            // Empty string or empty array → return empty set (filters to nothing)
            return parts.Length == 0
                ? new HashSet<string>()
                : new HashSet<string>(parts, StringComparer.OrdinalIgnoreCase);
        }

        HashSet<string>? statusSet = ParseSet(statuses);
        HashSet<string>? genreSet = ParseSet(genres);
        HashSet<string>? tagSet = ParseSet(tags);
        HashSet<string>? profileSet = ParseSet(profiles);
        HashSet<string>? audioLangSet = ParseSet(audioLanguages);
        HashSet<string>? subtitleLangSet = ParseSet(subtitleLanguages);

        // Include the complete calendar days in the supplied offset, defaulting to UTC.
        DateTimeOffset? airedFromDt = null;
        DateTimeOffset? airedToDt = null;

        if (!string.IsNullOrWhiteSpace(airedFrom)
            && DateTimeOffset.TryParse(airedFrom, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset parsedFrom))
        {
            airedFromDt = new DateTimeOffset(parsedFrom.Date, parsedFrom.Offset);
        }

        if (!string.IsNullOrWhiteSpace(airedTo)
            && DateTimeOffset.TryParse(airedTo, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset parsedTo))
        {
            airedToDt = new DateTimeOffset(parsedTo.Date.AddDays(1).AddTicks(-1), parsedTo.Offset);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Series-level filter: status, genres, tags, profiles, monitored
        static bool MatchesSeries(
            SonarrSeriesSnapshot s,
            string? monitoredF,
            HashSet<string>? statusS,
            HashSet<string>? genreS,
            HashSet<string>? tagS,
            HashSet<string>? profileS)
        {
            // Monitored
            if (monitoredF == "monitored" && !s.Monitored)
                return false;
            if (monitoredF == "notMonitored" && s.Monitored)
                return false;

            // Status
            if (statusS is not null && !statusS.Contains(s.Status))
                return false;

            // Genres (at least one must match)
            if (genreS is not null && !s.Genres.Any(genreS.Contains))
                return false;

            // Tags (at least one must match)
            if (tagS is not null && !s.Tags.Any(tagS.Contains))
                return false;

            // Profiles (at least one must match)
            if (profileS is not null && !profileS.Contains(s.Profile.Name))
                return false;

            return true;
        }

        // Episode-level filter: aired, languages, score, date range
        static bool MatchesEpisode(
            SonarrEpisodeSnapshot e,
            string? airedF,
            HashSet<string>? audioLs,
            HashSet<string>? subtitleLs,
            int? sMin,
            int? sMax,
            DateTimeOffset? fromDt,
            DateTimeOffset? toDt,
            DateTimeOffset now)
        {
            // Aired
            bool hasAired = e.AirDateUtc is not null && e.AirDateUtc.Value <= now;
            if (airedF == "aired" && !hasAired)
                return false;
            if (airedF == "notAired" && hasAired)
                return false;

            // Audio languages (at least one must match)
            if (audioLs is not null && !e.File.AudioLanguages.Any(audioLs.Contains))
                return false;

            // Subtitle languages (at least one must match)
            if (subtitleLs is not null && !e.File.SubtitleLanguages.Any(subtitleLs.Contains))
                return false;

            // Score range
            if ((sMin.HasValue || sMax.HasValue) && !e.HasFile) return false;

            if (sMin.HasValue && e.File.CustomFormatScore < sMin.Value)
                return false;
            if (sMax.HasValue && e.File.CustomFormatScore > sMax.Value)
                return false;

            // Date range
            if (fromDt is not null && (e.AirDateUtc is null || e.AirDateUtc.Value < fromDt.Value))
                return false;
            if (toDt is not null && (e.AirDateUtc is null || e.AirDateUtc.Value > toDt.Value))
                return false;

            return true;
        }

        // Build filtered response: series -> seasons -> episodes
        IReadOnlyList<SonarrLibraryEntryResponse> series = snapshot is null
            ? []
            : LibrarySorter.SortSonarr(snapshot.Series
                .Where(s => MatchesSeries(s, monitoredFilter, statusSet, genreSet, tagSet, profileSet))
                .Select(s =>
                {
                    IReadOnlyList<SonarrSeasonResponse> filteredSeasons = s.Seasons
                        .Select(season =>
                        {
                            SonarrEpisodeResponse[] filteredEpisodes = season.Episodes
                                .Where(e => MatchesEpisode(e, airedFilter, audioLangSet, subtitleLangSet, scoreMin,
                                    scoreMax, airedFromDt, airedToDt, now))
                                .Select(e => new SonarrEpisodeResponse
                                {
                                    Number = e.EpisodeNumber,
                                    EpisodeId = e.EpisodeId,
                                    HasAired = e.AirDateUtc.HasValue && e.AirDateUtc.Value <= now,
                                    MediaDetails = LibraryMediaDetailsResponse.From(e.File, e.HasFile),
                                    Title = e.Title,
                                    AirDateUtc = e.AirDateUtc,
                                    HasFile = e.HasFile,
                                    Monitored = e.Monitored,
                                    CustomFormatScore = e.HasFile ? e.File.CustomFormatScore : null,
                                })
                                .OrderBy(ep => ep.Number)
                                .ToArray();

                            return new SonarrSeasonResponse
                            {
                                Number = season.SeasonNumber,
                                Monitored = season.Monitored,
                                Episodes = filteredEpisodes,
                            };
                        })
                        .Where(season => season.Episodes.Any())
                        .OrderBy(season => season.Number)
                        .ToArray();

                    // Skip series with no matching seasons
                    if (!filteredSeasons.Any())
                        return null;

                    return new SonarrLibraryEntryResponse
                    {
                        InstanceName = snapshot.InstanceName,
                        SeriesId = s.SeriesId,
                        SeriesTitle = s.Title,
                        SeriesTitleSlug = s.TitleSlug,
                        QualityProfileName = s.Profile.Name,
                        UpgradeUntilCustomFormatScore = s.Profile.UpgradeUntilCustomFormatScore,
                        Status = s.Status,
                        AddedAt = s.AddedAt,
                        LibraryStatus = LibraryAvailability.GetStatus(s).ToWireValue(),
                        Genres = s.Genres,
                        Tags = s.Tags,
                        LibraryEpisodeCount = s.Seasons.Sum(season => season.Episodes.Count),
                        FileCount = s.Seasons.Sum(season => season.Episodes.Count(episode => episode.HasFile)),
                        Monitored = s.Monitored,
                        Seasons = filteredSeasons,
                        TotalSize = LibrarySizeCalculator.SeriesSize(s),
                        TotalEpisodes = filteredSeasons.Sum(season => season.Episodes.Count),
                        AiredEpisodes = filteredSeasons.Sum(season => season.Episodes.Count(e => e.AirDateUtc.HasValue && e.AirDateUtc.Value <= now)),
                    };
                })
                .Where(entry => entry is not null)
                .Cast<SonarrLibraryEntryResponse>(), new LibraryRequestFilter(), settings.TitleSorting);

        return TypedResults.Ok(new SonarrLibraryResponse
        {
            SyncedAt = snapshot?.SyncedAt,
            Rows = series
        });
    }

    private static async Task<IResult> GetPosterAsync(
        string instanceId,
        int seriesId,
        [FromQuery] int width = 342,
        HttpResponse httpResponse = default!,
        ISettingsService settingsService = default!,
        IPosterCacheService posterCache = default!,
        CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);
        ArrInstanceSettings? instance =
            settings.SonarrInstances.FirstOrDefault((currentInstance) => currentInstance.Id == instanceId);

        if (instance is null)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Sonarr instance not found",
                Detail = $"No Sonarr instance exists for id '{instanceId}'.",
                Status = StatusCodes.Status404NotFound
            });
        }

        SonarrInstanceSnapshot? snapshot =
            settings.SonarrSnapshots.FirstOrDefault((currentSnapshot) => currentSnapshot.InstanceId == instanceId);

        string? remoteUrl = null;
        if (snapshot is not null && snapshot.PosterUrls.TryGetValue(seriesId, out string? url))
        {
            remoteUrl = url;
        }

        PosterImage? poster = await posterCache.GetOrDownloadPosterAsync(
            $"sonarr_{seriesId}", remoteUrl, width, cancellationToken);

        if (poster is null)
        {
            httpResponse.StatusCode = 404;
            httpResponse.ContentType = "application/json";
            await httpResponse.WriteAsync("{\"error\":\"Poster not found\"}", cancellationToken);
            return Results.Empty;
        }

        httpResponse.ContentType = poster.ContentType;
        httpResponse.Headers.CacheControl = "no-store";
        await httpResponse.Body.WriteAsync(poster.Bytes, 0, poster.Bytes.Length, cancellationToken);
        return Results.Empty;
    }
}
