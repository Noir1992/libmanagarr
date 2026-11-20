using System.Text.Json;
using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

/// <summary>Batch title reads only; never changes Arr settings or source/matching titles.</summary>
public sealed class MediaTitleRefreshService(ISettingsService settingsService, RadarrClient radarr,
    TimeProvider clock, ILogger<MediaTitleRefreshService> logger, SymlinkMatchingRequests? matching = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly (string Code, string Name)[] Languages = [("en", "English"), ("de", "German"), ("fr", "French")];

    public async Task<bool> RefreshAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            AppSettings snapshot = await settingsService.GetSnapshotAsync(token);
            bool changed = false;
            foreach (ArrInstanceSettings instance in snapshot.RadarrInstances)
            {
                RadarrMovieSnapshot[] movies = snapshot.RadarrSnapshots.Where(value => value.InstanceId == instance.Id)
                    .SelectMany(value => value.Movies).ToArray();
                DateTimeOffset now = clock.GetUtcNow();
                if (!movies.Any(movie => Languages.Any(language => Due(movie, language.Code, now)))) continue;
                Dictionary<(int MovieId, string Code), MediaTitleTranslation> results = [];
                try
                {
                    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(45));
                    IReadOnlyList<ArrTitleLanguage> available = await radarr.FetchTitleLanguagesAsync(instance, timeout.Token);
                    // English is the documented fallback, so it must be fetched before other languages.
                    Dictionary<int, RadarrMovieResource> english = [];
                    foreach ((string code, string name) in Languages)
                    {
                        ArrTitleLanguage? language = available.FirstOrDefault(value => value.Id > 0 && string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase));
                        if (language is null)
                        {
                            foreach (RadarrMovieSnapshot movie in movies.Where(movie => Due(movie, code, now)))
                                results[(movie.MovieId, code)] = Result(null, MediaTitleFetchStatus.Missing, now);
                            continue;
                        }
                        if (code != "en" && !movies.Any(movie => Due(movie, code, now))) continue;
                        IReadOnlyList<RadarrMovieResource> response = await radarr.FetchTitlesAsync(instance, language.Id, timeout.Token);
                        Dictionary<int, RadarrMovieResource> byId = response.Where(value => value.Id.HasValue)
                            .GroupBy(value => value.Id!.Value).ToDictionary(group => group.Key, group => group.First());
                        if (code == "en") english = byId;
                        foreach (RadarrMovieSnapshot movie in movies.Where(movie => Due(movie, code, now)))
                        {
                            RadarrMovieResource? value = byId.GetValueOrDefault(movie.MovieId);
                            string? title = SameIdentity(movie, value) ? value?.Title?.Trim() : null;
                            if (code != "en")
                            {
                                RadarrMovieResource? reference = english.GetValueOrDefault(movie.MovieId);
                                // Equal answers cannot prove a translation. An English fallback stays English.
                                if (!SameIdentity(movie, reference) || string.IsNullOrWhiteSpace(reference?.Title)
                                    || string.Equals(title, reference.Title.Trim(), StringComparison.OrdinalIgnoreCase)) title = null;
                            }
                            results[(movie.MovieId, code)] = Result(title,
                                string.IsNullOrWhiteSpace(title) ? MediaTitleFetchStatus.Missing : MediaTitleFetchStatus.Available, now);
                        }
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException && !token.IsCancellationRequested)
                {
                    logger.LogWarning("Localized title refresh failed for Radarr instance {InstanceId}: {ErrorType}.", instance.Id, exception.GetType().Name);
                    foreach (RadarrMovieSnapshot movie in movies)
                        foreach ((string code, _) in Languages)
                            if (Due(movie, code, now) && !results.ContainsKey((movie.MovieId, code)))
                                results[(movie.MovieId, code)] = Result(null, MediaTitleFetchStatus.Failed, now);
                }
                token.ThrowIfCancellationRequested();
                if (results.Count == 0) continue;
                Dictionary<int, RadarrMovieSnapshot> observed = movies.ToDictionary(value => value.MovieId);
                await settingsService.UpdateAsync(current =>
                {
                    ArrInstanceSettings? configured = current.RadarrInstances.FirstOrDefault(value => value.Id == instance.Id);
                    if (configured?.BaseUrl != instance.BaseUrl || configured.ApiKey != instance.ApiKey) return;
                    foreach (RadarrMovieSnapshot movie in current.RadarrSnapshots.Where(value => value.InstanceId == instance.Id).SelectMany(value => value.Movies))
                    {
                        RadarrMovieSnapshot? original = observed.GetValueOrDefault(movie.MovieId);
                        if (original is null || original.TmdbId != movie.TmdbId || original.Title != movie.Title) continue;
                        foreach ((string code, _) in Languages)
                        {
                            if (!results.TryGetValue((movie.MovieId, code), out MediaTitleTranslation? result)) continue;
                            // A temporary failure/empty response never erases a known title.
                            result.Title ??= movie.Titles.Translations.GetValueOrDefault(code)?.Title;
                            movie.Titles.Translations[code] = result;
                            changed = true;
                        }
                    }
                }, token);
            }
            if (changed) matching?.Request();
            return changed;
        }
        finally { gate.Release(); }
    }

    private static bool SameIdentity(RadarrMovieSnapshot movie, RadarrMovieResource? value) =>
        value is not null && value.Id == movie.MovieId && (movie.TmdbId is null || value.TmdbId == movie.TmdbId);

    private static bool Due(RadarrMovieSnapshot movie, string code, DateTimeOffset now) =>
        !movie.Titles.Translations.TryGetValue(code, out MediaTitleTranslation? translation) || translation.RetryAt <= now;

    private static MediaTitleTranslation Result(string? title, MediaTitleFetchStatus status, DateTimeOffset now) => new()
    {
        Title = string.IsNullOrWhiteSpace(title) ? null : title,
        Source = "radarr:movie:languageId", Status = status, CheckedAt = now,
        RetryAt = now.Add(status switch
        {
            MediaTitleFetchStatus.Available => TimeSpan.FromDays(7),
            MediaTitleFetchStatus.Missing => TimeSpan.FromDays(1),
            _ => TimeSpan.FromMinutes(5)
        })
    };
}

public sealed class MediaTitleRefreshBackgroundService(MediaTitleRefreshService titles,
    ILogger<MediaTitleRefreshBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await titles.RefreshAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception) { logger.LogError(exception, "Could not refresh media titles."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
