using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Clients;

public sealed class SonarrClient(HttpClient httpClient)
{
    public async Task TestConnectionAsync(
        ArrInstanceSettings instance,
        CancellationToken cancellationToken)
    {
        _ = await GetAsync<List<SonarrQualityProfileResource>>(
            instance,
            "/api/v3/qualityprofile",
            cancellationToken);
    }

    public async Task<IReadOnlyList<SonarrEpisodeResource>> FetchCalendarEpisodesAsync(
        ArrInstanceSettings instance,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken)
    {
        string startValue = Uri.EscapeDataString(startUtc.ToString("O"));
        string endValue = Uri.EscapeDataString(endUtc.ToString("O"));
        return await GetAsync<List<SonarrEpisodeResource>>(
                   instance,
                   $"/api/v3/calendar?start={startValue}&end={endValue}&unmonitored=true&includeSeries=true",
                   cancellationToken)
               ?? [];
    }

    public async Task<IReadOnlyList<SonarrHistoryResource>> FetchHistorySinceAsync(
        ArrInstanceSettings instance,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        string sinceValue = Uri.EscapeDataString(since.ToString("O"));
        return await GetAsync<List<SonarrHistoryResource>>(
                   instance,
                   $"/api/v3/history/since?date={sinceValue}&includeSeries=true&includeEpisode=true",
                   cancellationToken)
               ?? [];
    }

    public async Task<SonarrInstanceSnapshot> FetchSnapshotAsync(
        ArrInstanceSettings instance,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SonarrQualityProfileResource> profiles = await GetAsync<List<SonarrQualityProfileResource>>(
            instance,
            "/api/v3/qualityprofile",
            cancellationToken) ?? [];
        IReadOnlyList<SonarrSeriesResource> series = await GetAsync<List<SonarrSeriesResource>>(
            instance,
            "/api/v3/series",
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

        // Fetch tags to map IDs to labels
        IReadOnlyList<SonarrTagResource> tags = await GetAsync<List<SonarrTagResource>>(
            instance,
            "/api/v3/tag",
            cancellationToken) ?? [];
        Dictionary<int, string> tagMap = tags
            .Where(t => t is { Id: not null, Label: not null })
            .ToDictionary(
                t => t.Id!.Value,
                t => t.Label!);

        List<SonarrSeriesSnapshot> snapshots = [];

        foreach (SonarrSeriesResource seriesResource in series)
        {
            List<SonarrEpisodeResource> episodes = await GetAsync<List<SonarrEpisodeResource>>(
                instance,
                $"/api/v3/episode?seriesId={seriesResource.Id}",
                cancellationToken) ?? [];
            List<SonarrEpisodeFileResource> episodeFiles = await GetAsync<List<SonarrEpisodeFileResource>>(
                instance,
                $"/api/v3/episodefile?seriesId={seriesResource.Id}",
                cancellationToken) ?? [];
            Dictionary<int, SonarrEpisodeFileResource> episodeFileMap = episodeFiles
                .Where(episodeFile => episodeFile.Id.HasValue)
                .ToDictionary(episodeFile => episodeFile.Id!.Value);

            SonarrProfileSummary? profile = seriesResource.QualityProfileId.HasValue
                ? profileMap.GetValueOrDefault(seriesResource.QualityProfileId.Value)
                : null;
            List<SonarrSeasonSnapshot> seasons = episodes
                .GroupBy(episode => episode.SeasonNumber ?? 0)
                .OrderBy(group => group.Key)
                .Select(group => new SonarrSeasonSnapshot
                {
                    SeasonNumber = group.Key,
                    Name = $"Season {group.Key}",
                    Monitored = group.All(episode => episode.Monitored ?? true),
                    Episodes = group
                        .OrderBy(episode => episode.EpisodeNumber ?? 0)
                        .Select(episode => MapEpisodeSnapshot(episode, episodeFileMap))
                        .ToList()
                })
                .Where(season => season.Episodes.Count > 0)
                .ToList();

            snapshots.Add(
                new SonarrSeriesSnapshot
                {
                    SeriesId = seriesResource.Id,
                    Title = seriesResource.Title,
                    TitleSlug = seriesResource.TitleSlug,
                    PosterRemoteUrl = seriesResource.GetPosterRemoteUrl(),
                    AddedAt = seriesResource.Added,
                    Status = seriesResource.Status ?? "Unknown",
                    Genres = (seriesResource.Genres ?? []).ToArray(),
                    Tags = (seriesResource.Tags ?? [])
                        .Select(tagId => tagMap.GetValueOrDefault(tagId, $"Tag #{tagId}"))
                        .ToArray(),
                    Monitored = seriesResource.Monitored ?? false,
                    NextEpisodeAirDate = seasons
                        .SelectMany(s => s.Episodes)
                        .Where(e => e.AirDateUtc.HasValue && e.AirDateUtc.Value > DateTimeOffset.UtcNow)
                        .MinBy(e => e.AirDateUtc)?.AirDateUtc,
                    HasAiredEpisodes = seasons
                        .SelectMany(s => s.Episodes)
                        .Any(e => e.AirDateUtc.HasValue && e.AirDateUtc.Value <= DateTimeOffset.UtcNow),
                    Profile = new MediaProfileSnapshot
                    {
                        Name = profile?.Name ?? "Unknown profile",
                        UpgradeUntilCustomFormatScore = profile?.UpgradeUntilCustomFormatScore
                    },
                    Seasons = seasons
                });
        }

        Dictionary<int, string?> posterUrls = series.ToDictionary(
            s => s.Id,
            s => s.GetPosterRemoteUrl());

        return new SonarrInstanceSnapshot
        {
            InstanceId = instance.Id,
            InstanceName = instance.Name,
            SyncedAt = DateTimeOffset.UtcNow,
            Series = snapshots
                .OrderBy(snapshot => snapshot.Title, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PosterUrls = posterUrls
        };
    }

    // Every method below this line acts on one instance now, not on the last snapshot. A snapshot is a
    // claim about a past moment, so the pairing is read again before a link is created and every write
    // here is checked by reading the same thing back: Sonarr accepting a request is not evidence that
    // the field changed.
    public async Task<SonarrSeriesResource?> FetchSeriesAsync(
        ArrInstanceSettings instance,
        int seriesId,
        CancellationToken cancellationToken) =>
        await GetAsync<SonarrSeriesResource>(instance, $"/api/v3/series/{seriesId}", cancellationToken);

    public async Task<SonarrEpisodeResource?> FetchEpisodeAsync(
        ArrInstanceSettings instance,
        int episodeId,
        CancellationToken cancellationToken) =>
        await GetAsync<SonarrEpisodeResource>(instance, $"/api/v3/episode/{episodeId}", cancellationToken);

    public async Task<IReadOnlyList<SonarrEpisodeResource>> FetchEpisodesAsync(
        ArrInstanceSettings instance,
        int seriesId,
        CancellationToken cancellationToken) =>
        await GetAsync<List<SonarrEpisodeResource>>(instance, $"/api/v3/episode?seriesId={seriesId}", cancellationToken) ?? [];

    public async Task<SonarrEpisodeFileResource?> FetchEpisodeFileAsync(
        ArrInstanceSettings instance,
        int episodeFileId,
        CancellationToken cancellationToken) =>
        await GetAsync<SonarrEpisodeFileResource>(instance, $"/api/v3/episodefile/{episodeFileId}", cancellationToken);

    public async Task<IReadOnlyList<SonarrEpisodeFileResource>> FetchEpisodeFilesAsync(
        ArrInstanceSettings instance,
        int seriesId,
        CancellationToken cancellationToken) =>
        await GetAsync<List<SonarrEpisodeFileResource>>(instance, $"/api/v3/episodefile?seriesId={seriesId}", cancellationToken) ?? [];

    public async Task<SonarrNamingConfigResource?> FetchNamingConfigAsync(
        ArrInstanceSettings instance,
        CancellationToken cancellationToken) =>
        await GetAsync<SonarrNamingConfigResource>(instance, "/api/v3/config/naming", cancellationToken);

    /// <summary>The names Sonarr's own rules would give the files of a series, per existing episode file.</summary>
    public async Task<IReadOnlyList<SonarrRenameResource>> FetchRenameProposalsAsync(
        ArrInstanceSettings instance,
        int seriesId,
        CancellationToken cancellationToken) =>
        await GetAsync<List<SonarrRenameResource>>(instance, $"/api/v3/rename?seriesId={seriesId}", cancellationToken) ?? [];

    /// <summary>
    /// Read-modify-write on one episode: Sonarr's own document is fetched, only the named field is
    /// replaced, and the complete document goes back. Sending a partial object instead would let Sonarr
    /// treat every field this client does not model as absent, so switching monitoring off could blank
    /// something nobody asked about. The caller reads the episode again to see the change actually stuck.
    /// </summary>
    public async Task<SonarrEpisodeResource?> SetEpisodeMonitoredAsync(
        ArrInstanceSettings instance,
        int episodeId,
        bool monitored,
        CancellationToken cancellationToken)
    {
        JsonNode episode = await GetNodeAsync(instance, $"/api/v3/episode/{episodeId}", cancellationToken)
            ?? throw new InvalidOperationException($"Sonarr answered without an episode document for {episodeId}.");
        episode["monitored"] = monitored;

        return await PutNodeAsync<SonarrEpisodeResource>(instance, "/api/v3/episode", episode, cancellationToken);
    }

    /// <summary>
    /// Same procedure for one episode file, with the fields the caller named. A file's Release Group is
    /// set without touching the quality, and reading the document first is what keeps that true.
    /// </summary>
    public async Task<SonarrEpisodeFileResource?> SetEpisodeFileFieldsAsync(
        ArrInstanceSettings instance,
        int episodeFileId,
        IReadOnlyDictionary<string, object?> fields,
        CancellationToken cancellationToken)
    {
        JsonNode episodeFile = await GetNodeAsync(instance, $"/api/v3/episodefile/{episodeFileId}", cancellationToken)
            ?? throw new InvalidOperationException($"Sonarr answered without an episode file document for {episodeFileId}.");
        foreach ((string name, object? value) in fields)
        {
            episodeFile[name] = value is null ? null : JsonSerializer.SerializeToNode(value);
        }

        return await PutNodeAsync<SonarrEpisodeFileResource>(instance, "/api/v3/episodefile", episodeFile, cancellationToken);
    }

    private async Task<JsonNode?> GetNodeAsync(
        ArrInstanceSettings instance,
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"{NormalizeBaseUrl(instance.BaseUrl)}{relativeUrl}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Api-Key", instance.ApiKey);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);
    }

    private async Task<T?> PutNodeAsync<T>(
        ArrInstanceSettings instance,
        string relativeUrl,
        JsonNode node,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, $"{NormalizeBaseUrl(instance.BaseUrl)}{relativeUrl}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Api-Key", instance.ApiKey);
        request.Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    /// <summary>
    /// Queues a rescan of one series. The scan reads the files that are already there, which is what a
    /// newly created link needs; it is not a download and moves nothing.
    /// </summary>
    public async Task<SonarrCommandResource?> StartRescanSeriesAsync(
        ArrInstanceSettings instance,
        int seriesId,
        CancellationToken cancellationToken) =>
        await SendAndReadAsync<SonarrCommandResource>(
            HttpMethod.Post, instance, "/api/v3/command", new CommandRequest { Name = "RescanSeries", SeriesId = seriesId }, cancellationToken);

    /// <summary>Rename limited to one file, so no other episode of the series is ever touched.</summary>
    public async Task<SonarrCommandResource?> StartRenameFilesAsync(
        ArrInstanceSettings instance,
        int seriesId,
        IReadOnlyList<int> episodeFileIds,
        CancellationToken cancellationToken) =>
        await SendAndReadAsync<SonarrCommandResource>(
            HttpMethod.Post,
            instance,
            "/api/v3/command",
            new CommandRequest { Name = "RenameFiles", SeriesId = seriesId, FileIds = episodeFileIds },
            cancellationToken);

    public async Task<SonarrCommandResource?> FetchCommandAsync(
        ArrInstanceSettings instance,
        int commandId,
        CancellationToken cancellationToken) =>
        await GetAsync<SonarrCommandResource>(instance, $"/api/v3/command/{commandId}", cancellationToken);

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

    private async Task<T?> SendAndReadAsync<T>(
        HttpMethod method,
        ArrInstanceSettings instance,
        string relativeUrl,
        object body,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, $"{NormalizeBaseUrl(instance.BaseUrl)}{relativeUrl}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Api-Key", instance.ApiKey);
        request.Content = JsonContent.Create(body);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    /// <summary>Wire shape of Sonarr's command endpoint; fields it does not take stay absent.</summary>
    private sealed class CommandRequest
    {
        public string Name { get; init; } = string.Empty;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? SeriesId { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<int>? FileIds { get; init; }
    }

    private static SonarrEpisodeSnapshot MapEpisodeSnapshot(
        SonarrEpisodeResource episode,
        IReadOnlyDictionary<int, SonarrEpisodeFileResource> episodeFileMap)
    {
        SonarrEpisodeFileResource? episodeFile = episode.EpisodeFileId.HasValue
            ? episodeFileMap.GetValueOrDefault(episode.EpisodeFileId.Value)
            : null;

        return new SonarrEpisodeSnapshot
        {
            EpisodeId = episode.Id ?? 0,
            EpisodeNumber = episode.EpisodeNumber ?? 0,
            Title = episode.Title ?? "Unknown episode",
            AirDateUtc = episode.AirDateUtc,
            Monitored = episode.Monitored ?? false,
            HasFile = episode.HasFile ?? episodeFile is not null,
            File = MapMediaFileSnapshot(
                episodeFile,
                episode.Runtime.HasValue ? TimeSpan.FromMinutes(episode.Runtime.Value) : null)
        };
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

    private static MediaFileSnapshot MapMediaFileSnapshot(
        SonarrEpisodeFileResource? episodeFile,
        TimeSpan? fallbackDuration)
    {
        List<string> audioLanguages = MediaFileLanguageHelper.CollectLanguageNames(
                episodeFile?.Languages)
            .Concat(MediaFileLanguageHelper.CollectLanguageNames(episodeFile?.MediaInfo?.AudioLanguages))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        List<string> subtitleLanguages = MediaFileLanguageHelper.CollectLanguageNames(
                episodeFile?.SubtitleLanguages)
            .Concat(MediaFileLanguageHelper.CollectLanguageNames(episodeFile?.MediaInfo?.Subtitles))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MediaFileSnapshot
        {
            Duration = episodeFile?.MediaInfo?.RunTime ?? fallbackDuration,
            CustomFormatScore = episodeFile?.CustomFormatScore ?? 0,
            AudioLanguages = audioLanguages,
            SubtitleLanguages = subtitleLanguages,
            VideoBitrate = episodeFile?.MediaInfo?.VideoBitrate is > 0 ? episodeFile.MediaInfo.VideoBitrate : null,
            VideoCodec = episodeFile?.MediaInfo?.VideoCodec,
            Size = episodeFile?.Size,
            ReleaseGroup = episodeFile?.ReleaseGroup,
            AudioCodec = episodeFile?.MediaInfo?.AudioCodec,
            Container = ResolveContainer(episodeFile),
            VideoFormat = ResolveDynamicRange(episodeFile),
            Resolution = episodeFile?.MediaInfo?.Resolution,
            CreatedAt = episodeFile?.DateAdded,
            RelativePath = episodeFile?.RelativePath ?? episodeFile?.Path,
            Id = episodeFile?.Id,
            Path = episodeFile?.Path
        };
    }

    private static string? ResolveContainer(SonarrEpisodeFileResource? episodeFile)
    {
        string? mediaInfoValue = episodeFile?.MediaInfo?.ContainerFormat
                                 ?? episodeFile?.MediaInfo?.ReadString("container", "containerType", "format",
                                     "videoContainer");
        if (!string.IsNullOrWhiteSpace(mediaInfoValue))
        {
            return mediaInfoValue;
        }

        return ReadContainerFromPath(episodeFile?.RelativePath)
               ?? ReadContainerFromPath(episodeFile?.Path);
    }

    private static string? ResolveDynamicRange(SonarrEpisodeFileResource? episodeFile)
    {
        MediaInfoResource? mediaInfo = episodeFile?.MediaInfo;
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