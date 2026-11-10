using System.Net.Http.Headers;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Clients;

public sealed class RadarrClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<ArrRootFolderResource>> FetchRootFoldersAsync(
        ArrInstanceSettings instance, CancellationToken cancellationToken) =>
        await GetAsync<List<ArrRootFolderResource>>(instance, "/api/v3/rootfolder", cancellationToken)
        ?? throw new HttpRequestException("Radarr returned no root-folder response.");

    public async Task TestConnectionAsync(
        ArrInstanceSettings instance,
        CancellationToken cancellationToken)
    {
        _ = await GetAsync<List<SonarrQualityProfileResource>>(
            instance,
            "/api/v3/qualityprofile",
            cancellationToken);
    }

    public async Task<IReadOnlyList<RadarrMovieResource>> FetchCalendarMoviesAsync(
        ArrInstanceSettings instance,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken)
    {
        string startValue = Uri.EscapeDataString(startUtc.ToString("O"));
        string endValue = Uri.EscapeDataString(endUtc.ToString("O"));
        return await GetAsync<List<RadarrMovieResource>>(
                   instance,
                   $"/api/v3/calendar?start={startValue}&end={endValue}&unmonitored=true",
                   cancellationToken)
               ?? [];
    }

    public async Task<IReadOnlyList<RadarrHistoryResource>> FetchHistorySinceAsync(
        ArrInstanceSettings instance,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        string sinceValue = Uri.EscapeDataString(since.ToString("O"));
        return await GetAsync<List<RadarrHistoryResource>>(
                   instance,
                   $"/api/v3/history/since?date={sinceValue}&includeMovie=true",
                   cancellationToken)
               ?? [];
    }

    public async Task<RadarrInstanceSnapshot> FetchSnapshotAsync(
        ArrInstanceSettings instance,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SonarrQualityProfileResource> profiles = await GetAsync<List<SonarrQualityProfileResource>>(
            instance,
            "/api/v3/qualityprofile",
            cancellationToken) ?? [];
        IReadOnlyList<RadarrMovieResource> movies = await GetAsync<List<RadarrMovieResource>>(
            instance,
            "/api/v3/movie",
            cancellationToken) ?? [];

        Dictionary<int, SonarrProfileSummary> profileMap = profiles
            .Where(profile => profile.Id.HasValue)
            .ToDictionary(
                profile => profile.Id!.Value,
                profile => new SonarrProfileSummary
                {
                    Id = profile.Id!.Value,
                    Name = profile.Name ?? "Unknown profile",
                    UpgradeUntilCustomFormatScore = ReadUpgradeUntilScore(profile)
                });

        IReadOnlyList<SonarrTagResource> tags = movies.Any(movie => movie.Tags is { Count: > 0 })
            ? await GetAsync<List<SonarrTagResource>>(instance, "/api/v3/tag", cancellationToken) ?? []
            : [];
        Dictionary<int, string> tagMap = tags.Where(tag => tag is { Id: not null, Label: not null })
            .ToDictionary(tag => tag.Id!.Value, tag => tag.Label!);
        List<RadarrMovieSnapshot> snapshots = [];
        foreach (RadarrMovieResource movie in movies.Where(currentMovie =>
                     currentMovie.Id.HasValue))
        {
            List<RadarrMovieFileResource> movieFiles = movie.HasFile == false ? [] : await GetAsync<List<RadarrMovieFileResource>>(
                instance,
                $"/api/v3/moviefile?movieId={movie.Id!.Value}",
                cancellationToken) ?? [];
            SonarrProfileSummary? profile = movie.QualityProfileId.HasValue
                ? profileMap.GetValueOrDefault(movie.QualityProfileId.Value)
                : null;
            RadarrMovieFileResource? movieFile = ReadMovieFile(movie, movieFiles);
            DateTimeOffset? releaseDate = movie.DigitalRelease ?? movie.PhysicalRelease ?? movie.InCinemas;
            snapshots.Add(new RadarrMovieSnapshot
            {
                MovieId = movie.Id!.Value,
                Title = movie.Title,
                TitleSlug = movie.TitleSlug,
                AlternativeTitles = (movie.AlternativeTitles ?? [])
                    .Select(alternative => alternative.Title?.Trim())
                    .Where(title => !string.IsNullOrWhiteSpace(title))
                    .Select(title => title!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                PosterRemoteUrl = movie.GetPosterRemoteUrl(),
                AddedAt = movie.Added,
                Status = movie.Status ?? "Unknown",
                Genres = (movie.Genres ?? []).ToArray(),
                Tags = (movie.Tags ?? []).Select(id => tagMap.GetValueOrDefault(id, $"Tag #{id}")).ToArray(),
                Monitored = movie.Monitored ?? false,
                AirDateUtc = releaseDate,
                HasAired = releaseDate.HasValue && releaseDate.Value <= DateTimeOffset.UtcNow,
                HasFile = movie.HasFile ?? movieFile is not null,
                Profile = new MediaProfileSnapshot
                {
                    Name = profile?.Name ?? "Unknown profile",
                    UpgradeUntilCustomFormatScore = profile?.UpgradeUntilCustomFormatScore
                },
                File = MapMediaFileSnapshot(movieFile)
            });
        }

        Dictionary<int, string?> posterUrls = movies
            .Where(m => m.Id.HasValue)
            .ToDictionary(
                m => m.Id!.Value,
                m => m.GetPosterRemoteUrl());

        return new RadarrInstanceSnapshot
        {
            InstanceId = instance.Id,
            InstanceName = instance.Name,
            SyncedAt = DateTimeOffset.UtcNow,
            Movies = snapshots
                .OrderBy(snapshot => snapshot.Title, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PosterUrls = posterUrls
        };
    }

    // The two live reads a confirmation needs: whether the chosen film still exists and which file it
    // points at right now. A snapshot cannot answer that, and a link must never be built onto a file
    // that a Radarr upgrade already replaced.
    public async Task<RadarrMovieResource?> FetchMovieAsync(
        ArrInstanceSettings instance,
        int movieId,
        CancellationToken cancellationToken) =>
        await GetAsync<RadarrMovieResource>(instance, $"/api/v3/movie/{movieId}", cancellationToken);

    public async Task<IReadOnlyList<RadarrMovieFileResource>> FetchMovieFilesAsync(
        ArrInstanceSettings instance,
        int movieId,
        CancellationToken cancellationToken) =>
        await GetAsync<List<RadarrMovieFileResource>>(instance, $"/api/v3/moviefile?movieId={movieId}", cancellationToken) ?? [];

    private async Task<T?> GetAsync<T>(
        ArrInstanceSettings instance,
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"{NormalizeBaseUrl(instance.BaseUrl)}{relativeUrl}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Api-Key", instance.ApiKey);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private static RadarrMovieFileResource? ReadMovieFile(
        RadarrMovieResource movie,
        IReadOnlyList<RadarrMovieFileResource> movieFiles)
    {
        if (movie.MovieFileId.HasValue)
        {
            RadarrMovieFileResource? matchingMovieFile =
                movieFiles.FirstOrDefault(movieFile => movieFile.Id == movie.MovieFileId);

            if (matchingMovieFile is not null)
            {
                return matchingMovieFile;
            }
        }

        return movieFiles.FirstOrDefault();
    }

    private static int? ReadUpgradeUntilScore(SonarrQualityProfileResource profile)
    {
        return profile.UpgradeUntilCustomFormatScore
               ?? profile.UpgradeUntilScore
               ?? profile.CutoffFormatScore
               ?? profile.FormatCutoff;
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        return baseUrl.Trim().TrimEnd('/');
    }

    private static MediaFileSnapshot MapMediaFileSnapshot(RadarrMovieFileResource? movieFile)
    {
        List<string> audioLanguages = MediaFileLanguageHelper.CollectLanguageNames(
                movieFile?.Languages)
            .Concat(MediaFileLanguageHelper.CollectLanguageNames(movieFile?.MediaInfo?.AudioLanguages))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        List<string> subtitleLanguages = MediaFileLanguageHelper.CollectLanguageNames(
                movieFile?.SubtitleLanguages)
            .Concat(MediaFileLanguageHelper.CollectLanguageNames(movieFile?.MediaInfo?.Subtitles))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MediaFileSnapshot
        {
            Duration = movieFile?.MediaInfo?.RunTime,
            CustomFormatScore = movieFile?.CustomFormatScore ?? 0,
            AudioLanguages = audioLanguages,
            SubtitleLanguages = subtitleLanguages,
            VideoBitrate = movieFile?.MediaInfo?.VideoBitrate is > 0 ? movieFile.MediaInfo.VideoBitrate : null,
            VideoCodec = movieFile?.MediaInfo?.VideoCodec,
            Size = movieFile?.Size,
            ReleaseGroup = movieFile?.ReleaseGroup,
            AudioCodec = movieFile?.MediaInfo?.AudioCodec,
            Container = ResolveContainer(movieFile),
            VideoFormat = ResolveDynamicRange(movieFile),
            Resolution = movieFile?.MediaInfo?.Resolution,
            CreatedAt = movieFile?.DateAdded,
            RelativePath = movieFile?.RelativePath ?? movieFile?.Path,
            Id = movieFile?.Id,
            Path = movieFile?.Path,
            SourceVersion = movieFile is null ? null : Services.SymlinkService.SourceVersionOf(movieFile),
            Quality = movieFile?.Quality?.Quality?.Name,
            QualityRevision = movieFile?.Quality?.Revision
        };
    }

    private static string? ResolveContainer(RadarrMovieFileResource? movieFile)
    {
        string? mediaInfoValue = movieFile?.MediaInfo?.ContainerFormat
                                 ?? movieFile?.MediaInfo?.ReadString("container", "containerType", "format",
                                     "videoContainer");
        if (!string.IsNullOrWhiteSpace(mediaInfoValue))
        {
            return mediaInfoValue;
        }

        return ReadContainerFromPath(movieFile?.RelativePath)
               ?? ReadContainerFromPath(movieFile?.Path);
    }

    private static string? ResolveDynamicRange(RadarrMovieFileResource? movieFile)
    {
        MediaInfoResource? mediaInfo = movieFile?.MediaInfo;
        string? videoDynamicRangeType = mediaInfo?.ReadString("videoDynamicRangeType");
        if (!string.IsNullOrWhiteSpace(videoDynamicRangeType))
        {
            return videoDynamicRangeType;
        }

        if (!string.IsNullOrWhiteSpace(mediaInfo?.VideoDynamicRange))
        {
            return mediaInfo.VideoDynamicRange;
        }

        return null;
    }

    private static string? ReadContainerFromPath(string? pathValue)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        string extension = Path.GetExtension(pathValue);
        return string.IsNullOrWhiteSpace(extension)
            ? null
            : extension.TrimStart('.');
    }
}
