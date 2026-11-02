using System.Globalization;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

// Aggregates the calendar endpoints of every configured Sonarr and Radarr instance into one
// normalized entry list. Failing instances are reported per instance instead of failing the
// whole request, so entries from healthy instances stay visible.
public sealed class CalendarService(
    ISettingsService settingsService,
    SonarrClient sonarrClient,
    RadarrClient radarrClient,
    CalendarCache cache,
    TimeProvider timeProvider,
    ILogger<CalendarService> logger)
{
    public const int MaxRangeDays = 62;

    private static readonly TimeSpan InstanceTimeout = TimeSpan.FromSeconds(15);
    private static readonly SemaphoreSlim InstanceGate = new(4, 4);

    public async Task<CalendarResponse> GetCalendarAsync(
        DateOnly startDate,
        DateOnly endDate,
        TimeZoneInfo timeZone,
        bool refresh,
        CancellationToken cancellationToken)
    {
        AppSettings settings = await settingsService.GetSnapshotAsync(cancellationToken);

        List<CalendarInstanceTarget> targets =
        [
            .. settings.SonarrInstances.Select(instance => new CalendarInstanceTarget(instance, ArrApplicationKind.Sonarr)),
            .. settings.RadarrInstances.Select(instance => new CalendarInstanceTarget(instance, ArrApplicationKind.Radarr))
        ];

        DateTimeOffset startUtc = ToUtcBoundary(startDate, timeZone);
        DateTimeOffset endUtc = ToUtcBoundary(endDate, timeZone);

        List<CalendarEntry> entries = [];
        List<CalendarInstanceStatus> instanceStatuses = [];

        await Parallel.ForEachAsync(
            targets,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = cancellationToken
            },
            async (target, outerToken) =>
            {
                (IReadOnlyList<CalendarEntry> instanceEntries, CalendarInstanceStatus status) =
                    await FetchInstanceAsync(target, startDate, endDate, startUtc, endUtc, timeZone, refresh, outerToken);

                lock (entries)
                {
                    entries.AddRange(instanceEntries);
                    instanceStatuses.Add(status);
                }
            });

        List<CalendarEntry> orderedEntries = entries
            .OrderBy(entry => entry.ReleaseDateTimeUtc ?? ToSortableInstant(entry.ReleaseDate!.Value))
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id, StringComparer.Ordinal)
            .ToList();

        return new CalendarResponse
        {
            FetchedAt = timeProvider.GetUtcNow().ToOffset(TimeSpan.Zero),
            Entries = orderedEntries,
            Instances = instanceStatuses
                .OrderBy(status => status.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static DateTimeOffset ToUtcBoundary(DateOnly date, TimeZoneInfo timeZone)
    {
        DateTime localMidnight = date.ToDateTime(TimeOnly.MinValue);
        DateTimeOffset boundary = new(localMidnight, timeZone.GetUtcOffset(localMidnight));
        return boundary.ToUniversalTime();
    }

    private static DateTimeOffset ToSortableInstant(DateOnly date)
    {
        return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    private async Task<(IReadOnlyList<CalendarEntry> Entries, CalendarInstanceStatus Status)> FetchInstanceAsync(
        CalendarInstanceTarget target,
        DateOnly startDate,
        DateOnly endDate,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        TimeZoneInfo timeZone,
        bool refresh,
        CancellationToken cancellationToken)
    {
        CalendarInstanceStatus BuildFailureStatus(string error) => new()
        {
            InstanceId = target.Instance.Id,
            Name = target.Instance.Name,
            Application = target.Kind,
            Loaded = false,
            Error = error
        };

        string cacheKey = $"{target.Kind}:{target.Instance.Id}:{timeZone.Id}:{startDate:O}:{endDate:O}";
        if (!refresh && cache.TryGet(cacheKey, out IReadOnlyList<CalendarEntry> cachedEntries))
        {
            return (cachedEntries, BuildSuccessStatus(target));
        }

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(InstanceTimeout);

        IReadOnlyList<CalendarEntry> instanceEntries;
        try
        {
            await InstanceGate.WaitAsync(timeoutSource.Token);
            try
            {
                instanceEntries = target.Kind switch
                {
                    ArrApplicationKind.Sonarr => MapSonarrEpisodes(
                        target.Instance,
                        await sonarrClient.FetchCalendarEpisodesAsync(target.Instance, startUtc, endUtc, timeoutSource.Token),
                        startUtc,
                        endUtc,
                        startDate,
                        endDate),
                    ArrApplicationKind.Radarr => MapRadarrMovies(
                        target.Instance,
                        await radarrClient.FetchCalendarMoviesAsync(target.Instance, startUtc, endUtc, timeoutSource.Token),
                        startDate,
                        endDate),
                    _ => []
                };
            }
            finally
            {
                InstanceGate.Release();
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Calendar fetch timed out for {ApplicationKind} instance {InstanceName}.",
                target.Kind,
                target.Instance.Name);
            return ([], BuildFailureStatus("The request timed out."));
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Calendar fetch failed for {ApplicationKind} instance {InstanceName}.",
                target.Kind,
                target.Instance.Name);
            return ([], BuildFailureStatus("The instance could not be reached."));
        }

        cache.Set(cacheKey, instanceEntries);
        return (instanceEntries, BuildSuccessStatus(target));
    }

    private static CalendarInstanceStatus BuildSuccessStatus(CalendarInstanceTarget target)
    {
        return new CalendarInstanceStatus
        {
            InstanceId = target.Instance.Id,
            Name = target.Instance.Name,
            Application = target.Kind,
            Loaded = true
        };
    }

    private static List<CalendarEntry> MapSonarrEpisodes(
        ArrInstanceSettings instance,
        IReadOnlyList<SonarrEpisodeResource> episodes,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        DateOnly startDate,
        DateOnly endDate)
    {
        List<CalendarEntry> entries = [];

        foreach (SonarrEpisodeResource episode in episodes)
        {
            if (episode.Id is not int episodeId)
            {
                continue;
            }

            int? seriesId = episode.Series?.Id ?? episode.SeriesId;
            DateTimeOffset? instant = episode.AirDateUtc;
            DateOnly? pureDate = instant is null && episode.AirDate is DateTimeOffset airDate
                ? DateOnly.FromDateTime(airDate.DateTime)
                : null;

            if (instant is null && pureDate is null)
            {
                continue;
            }

            if (!IsWithinRange(instant, pureDate, startUtc, endUtc, startDate, endDate))
            {
                continue;
            }

            entries.Add(new CalendarEntry
            {
                Id = $"sonarr:{instance.Id}:{episodeId}:episode",
                Application = ArrApplicationKind.Sonarr,
                InstanceId = instance.Id,
                InstanceName = instance.Name,
                MediaId = episodeId,
                SeriesId = seriesId,
                Title = episode.Series?.Title ?? "Unknown series",
                EpisodeTitle = episode.Title,
                SeasonNumber = episode.SeasonNumber,
                EpisodeNumber = episode.EpisodeNumber,
                ReleaseType = CalendarReleaseType.Episode,
                ReleaseDate = pureDate,
                ReleaseDateTimeUtc = instant,
                HasFile = episode.HasFile ?? false
            });
        }

        return entries;
    }

    private static List<CalendarEntry> MapRadarrMovies(
        ArrInstanceSettings instance,
        IReadOnlyList<RadarrMovieResource> movies,
        DateOnly startDate,
        DateOnly endDate)
    {
        List<CalendarEntry> entries = [];

        foreach (RadarrMovieResource movie in movies)
        {
            if (movie.Id is not int movieId)
            {
                continue;
            }

            AddMovieRelease(entries, instance, movie, movieId, CalendarReleaseType.Cinema, movie.InCinemas, startDate, endDate);
            AddMovieRelease(entries, instance, movie, movieId, CalendarReleaseType.Digital, movie.DigitalRelease, startDate, endDate);
            AddMovieRelease(entries, instance, movie, movieId, CalendarReleaseType.Physical, movie.PhysicalRelease, startDate, endDate);
        }

        return entries;
    }

    private static void AddMovieRelease(
        List<CalendarEntry> entries,
        ArrInstanceSettings instance,
        RadarrMovieResource movie,
        int movieId,
        CalendarReleaseType releaseType,
        DateTimeOffset? releaseDate,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (releaseDate is not DateTimeOffset value)
        {
            return;
        }

        // Movie release dates carry no clock time; keep them as pure calendar days so no
        // timezone conversion can move them to a neighbouring day.
        DateOnly day = DateOnly.FromDateTime(value.DateTime);
        if (day < startDate || day >= endDate)
        {
            return;
        }

        entries.Add(new CalendarEntry
        {
            Id = $"radarr:{instance.Id}:{movieId}:{releaseType.ToString().ToLower(CultureInfo.InvariantCulture)}",
            Application = ArrApplicationKind.Radarr,
            InstanceId = instance.Id,
            InstanceName = instance.Name,
            MediaId = movieId,
            Title = movie.Title,
            ReleaseType = releaseType,
            ReleaseDate = day,
            HasFile = movie.HasFile ?? movie.MovieFileId.HasValue
        });
    }

    private static bool IsWithinRange(
        DateTimeOffset? instant,
        DateOnly? pureDate,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (instant is DateTimeOffset instantValue)
        {
            return instantValue >= startUtc && instantValue < endUtc;
        }

        return pureDate >= startDate && pureDate < endDate;
    }

    private sealed record CalendarInstanceTarget(ArrInstanceSettings Instance, ArrApplicationKind Kind);
}
