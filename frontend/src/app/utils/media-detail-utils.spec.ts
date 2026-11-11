import { SonarrLibraryEpisodeTable } from '../interfaces/library/sonarr-library-episode-table';
import { SonarrLibrarySeriesTable } from '../interfaces/library/sonarr-library-series-table';
import { CodecMediaFile } from '../interfaces/codec-statistic/codec-media-file';
import { codecFileDetailData, episodeDetailData, isDetailRowActivation, specialsLast } from './media-detail-utils';

describe('media detail helpers', () => {
  it('maps episode fields while taking only the required context from its series', () => {
    const episode: SonarrLibraryEpisodeTable = {
      number: 3,
      title: 'Episode',
      customFormatScore: 120,
      airDateUtc: '2026-05-01T00:00:00Z',
      monitored: true,
      hasFile: true,
      mediaDetails: { videoCodec: 'HEVC' },
    };
    const series: SonarrLibrarySeriesTable = {
      id: 1,
      title: 'Series',
      titleSlug: 'series',
      url: 'http://sonarr/series/series',
      monitored: false,
      qualityProfileName: 'Series profile',
      upgradeUntilCustomFormatScore: 100,
      status: 'continuing',
      seasons: [],
      affectedSeasons: 0,
      affectedEpisodes: 0,
    };
    const details = episodeDetailData(episode, 2, series, 'Sonarr Main');

    expect(details).toEqual({
      ...episode,
      seasonNumber: 2,
      episodeNumber: 3,
      seriesTitle: 'Series',
      instanceName: 'Sonarr Main',
      url: series.url,
      upgradeUntilCustomFormatScore: 100,
    });
    expect(details.monitored).toBeTrue();
    expect(details.qualityProfileName).toBeUndefined();
    expect(details.status).toBeUndefined();
    expect(episode).not.toEqual(jasmine.objectContaining({ seriesTitle: 'Series' }));
  });

  it('maps codec statistics files onto dialog data and falls back to an empty url when unknown', () => {
    const episodeFile: CodecMediaFile = {
      title: 'Series - S02E03 - Episode',
      source: 'Sonarr (Sonarr Main)',
      instanceName: 'Sonarr Main',
      titleSlug: 'series-1',
      sizeBytes: 300,
      relativePath: 'series/season 2/series s02e03.mkv',
      rootTitle: 'Series',
      seasonNumber: 2,
      episodeNumber: 3,
      monitored: true,
      airDateUtc: '2026-01-05T20:00:00Z',
      qualityProfileName: 'HD',
      upgradeUntilCustomFormatScore: 1500,
      customFormatScore: 800,
      mediaDetails: { videoCodec: 'HEVC', resolution: '1080p', durationSeconds: 1500 },
    };
    const movieFile: CodecMediaFile = {
      title: 'Movie',
      source: 'Radarr (Radarr Main)',
      instanceName: 'Radarr Main',
      titleSlug: 'movie-1',
      sizeBytes: 500,
      relativePath: 'movie/movie.mkv',
      rootTitle: 'Movie',
      status: 'released',
      posterUrl: 'https://poster.example/movie.jpg',
      genres: ['Drama'],
      tags: ['oc'],
    };

    const episodeDetails = codecFileDetailData(episodeFile, 'http://sonarr/series/series-1');
    const movieDetails = codecFileDetailData(movieFile, null);

    expect(episodeDetails).toEqual(
      jasmine.objectContaining({
        title: 'Series - S02E03 - Episode',
        url: 'http://sonarr/series/series-1',
        instanceName: 'Sonarr Main',
        seasonNumber: 2,
        episodeNumber: 3,
        monitored: true,
        airDateUtc: '2026-01-05T20:00:00Z',
        hasFile: true,
        qualityProfileName: 'HD',
        upgradeUntilCustomFormatScore: 1500,
        customFormatScore: 800,
        size: 300,
        mediaDetails: episodeFile.mediaDetails,
      }),
    );
    expect(episodeDetails.status).toBeUndefined();
    expect(episodeDetails.posterUrl).toBeNull();
    expect(movieDetails).toEqual(
      jasmine.objectContaining({
        title: 'Movie',
        url: '',
        posterUrl: 'https://poster.example/movie.jpg',
        status: 'released',
        genres: ['Drama'],
        tags: ['oc'],
        hasFile: true,
      }),
    );
    expect(movieDetails.episodeNumber).toBeUndefined();
    expect(movieDetails.mediaDetails).toBeNull();
  });

  for (const tag of ['a', 'button', 'input', 'select', 'textarea']) {
    it(`preserves clicks on a nested ${tag} instead of opening row details`, () => {
      const row = document.createElement('div');
      const control = document.createElement(tag);
      const child = document.createElement('span');
      control.append(child);
      row.append(control);
      let activated: boolean | undefined;
      row.addEventListener('click', (event) => (activated = isDetailRowActivation(event)));
      child.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      expect(activated).toBeFalse();
    });
  }

  it('opens details for clicks on ordinary row content', () => {
    const row = document.createElement('div');
    const content = document.createElement('span');
    row.append(content);
    let activated: boolean | undefined;
    row.addEventListener('click', (event) => (activated = isDetailRowActivation(event)));
    content.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    expect(activated).toBeTrue();
  });

  it('handles keyboard activation on the row and preserves keyboard events from controls', () => {
    const row = document.createElement('div');
    const button = document.createElement('button');
    row.append(button);
    const activations: boolean[] = [];
    row.addEventListener('keydown', (event) => activations.push(isDetailRowActivation(event)));
    const rowEvent = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
    const buttonEvent = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
    row.dispatchEvent(rowEvent);
    button.dispatchEvent(buttonEvent);
    expect(activations).toEqual([true, false]);
    expect(rowEvent.defaultPrevented).toBeTrue();
    expect(buttonEvent.defaultPrevented).toBeFalse();
  });

  it('moves the specials season to the end while keeping the relative order of the other seasons', () => {
    const seasons = [{ number: 0 }, { number: 2 }, { number: 1 }];
    expect(specialsLast(seasons).map((season) => season.number)).toEqual([2, 1, 0]);
    expect(seasons.map((season) => season.number)).toEqual([0, 2, 1]);
  });
});
