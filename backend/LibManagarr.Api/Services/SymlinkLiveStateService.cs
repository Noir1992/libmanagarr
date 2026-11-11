using LibManagarr.Api.Clients;
using LibManagarr.Api.Models;

namespace LibManagarr.Api.Services;

public sealed record SymlinkLiveEvidence(AppSettings Settings,
    Dictionary<string, List<SymlinkFinding>> Findings, Dictionary<string, string> NamingRevisions);

/// <summary>Reads only the movies and series used by this pass, once per resource, without changing
/// the library snapshots. Failed live reads never fall back to an older successful snapshot.</summary>
public sealed class SymlinkLiveStateService(RadarrClient radarr, SonarrClient sonarr, TimeProvider clock)
{
    public async Task<SymlinkLiveEvidence> ReadAsync(AppSettings settings, IReadOnlyList<SymlinkLink> rows, CancellationToken token)
    {
        AppSettings current = new()
        {
            Symlinks = settings.Symlinks, RadarrInstances = settings.RadarrInstances, SonarrInstances = settings.SonarrInstances
        };
        Dictionary<string, List<SymlinkFinding>> findings = rows.ToDictionary(row => row.Id, _ => new List<SymlinkFinding>());
        Dictionary<string, string> naming = [];
        foreach (IGrouping<string, SymlinkLink> group in rows.GroupBy(row => row.RadarrInstanceId))
        {
            ArrInstanceSettings? instance = settings.RadarrInstances.FirstOrDefault(value => value.Id == group.Key);
            try
            {
                if (instance is null) throw new HttpRequestException("The Radarr instance is no longer configured.");
                RadarrInstanceSnapshot snapshot = new() { InstanceId = instance.Id, InstanceName = instance.Name, SyncedAt = clock.GetUtcNow() };
                foreach (int movieId in group.Select(row => row.RadarrMovieId).Distinct())
                {
                    IReadOnlyList<RadarrMovieFileResource> files = await radarr.FetchMovieFilesAsync(instance, movieId, token);
                    RadarrMovieFileResource? file = files.Count == 1 ? files[0] : null;
                    snapshot.Movies.Add(new RadarrMovieSnapshot
                    {
                        MovieId = movieId, HasFile = file is not null,
                        File = new MediaFileSnapshot { Id = file?.Id, Size = file?.Size, Path = file?.Path,
                            SourceVersion = file is null ? null : SymlinkService.SourceVersionOf(file) }
                    });
                }
                current.RadarrSnapshots.Add(snapshot);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                token.ThrowIfCancellationRequested();
                foreach (SymlinkLink row in group) findings[row.Id].Add(new("arr-unreachable", exception.Message, clock.GetUtcNow()));
            }
        }
        foreach (IGrouping<string, SymlinkLink> group in rows.GroupBy(row => row.SonarrInstanceId))
        {
            ArrInstanceSettings? instance = settings.SonarrInstances.FirstOrDefault(value => value.Id == group.Key);
            try
            {
                if (instance is null) throw new HttpRequestException("The Sonarr instance is no longer configured.");
                SonarrNamingConfigResource? config = await sonarr.FetchNamingConfigAsync(instance, token);
                if (config?.RenameEpisodes is null) throw new HttpRequestException("Sonarr returned no naming configuration.");
                naming[instance.Id] = SonarrSpecialIntegrationService.NamingRevisionOf(config);
                SonarrInstanceSnapshot snapshot = new() { InstanceId = instance.Id, InstanceName = instance.Name, SyncedAt = clock.GetUtcNow() };
                foreach (int seriesId in group.Select(row => row.SonarrSeriesId).Distinct())
                {
                    IReadOnlyList<SonarrEpisodeResource> episodes = await sonarr.FetchEpisodesAsync(instance, seriesId, token);
                    IReadOnlyList<SonarrEpisodeFileResource> files = await sonarr.FetchEpisodeFilesAsync(instance, seriesId, token);
                    snapshot.Series.Add(new SonarrSeriesSnapshot
                    {
                        SeriesId = seriesId, Seasons = [new SonarrSeasonSnapshot { SeasonNumber = 0,
                            Episodes = episodes.Where(value => value.SeasonNumber == 0 && value.Id.HasValue).Select(episode =>
                            {
                                SonarrEpisodeFileResource? file = files.FirstOrDefault(value => value.Id == episode.EpisodeFileId);
                                return new SonarrEpisodeSnapshot { EpisodeId = episode.Id!.Value, HasFile = file is not null,
                                    Monitored = episode.Monitored ?? false, File = new MediaFileSnapshot { Id = file?.Id, Path = file?.Path,
                                        Size = file?.Size, ReleaseGroup = file?.ReleaseGroup,
                                        Quality = file?.Quality?.Quality?.Name,
                                        QualityRevision = file?.Quality?.Revision,
                                        VideoCodec = file?.MediaInfo?.VideoCodec, AudioCodec = file?.MediaInfo?.AudioCodec,
                                        Resolution = file?.MediaInfo?.Resolution,
                                        AudioLanguages = MediaFileLanguageHelper.CollectLanguageNames(file?.Languages) } };
                            }).ToList() }]
                    });
                }
                current.SonarrSnapshots.Add(snapshot);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                token.ThrowIfCancellationRequested();
                foreach (SymlinkLink row in group) findings[row.Id].Add(new("arr-unreachable", exception.Message, clock.GetUtcNow()));
            }
        }
        return new(current, findings, naming);
    }
}
