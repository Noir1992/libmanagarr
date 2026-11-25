import { MediaTitlesService } from '../../services/media-titles.service';
import type { MockInstance } from 'vitest';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { RadarrApiService } from '../../services/radarr-api.service';
import { SonarrApiService } from '../../services/sonarr-api.service';
import { LibraryPage } from './library.page';

describe('LibraryPage title search', () => {
  let page: LibraryPage;
  let sonarr: Record<'fetchLibrary' | 'fetchFilterValues', MockInstance>;
  let radarr: Record<'fetchLibrary' | 'fetchFilterValues', MockInstance>;

  beforeEach(async () => {
    sonarr = {
      fetchLibrary: vi.fn<SonarrApiService['fetchLibrary']>(),
      fetchFilterValues: vi.fn<SonarrApiService['fetchFilterValues']>(),
    };
    radarr = {
      fetchLibrary: vi.fn<RadarrApiService['fetchLibrary']>(),
      fetchFilterValues: vi.fn<RadarrApiService['fetchFilterValues']>(),
    };
    const filterValues = {
      genres: [],
      tags: [],
      statuses: [],
      qualityProfiles: [],
      audioLanguages: [],
      subtitleLanguages: [],
    };
    sonarr.fetchFilterValues.mockReturnValue(of(filterValues));
    radarr.fetchFilterValues.mockReturnValue(of(filterValues));
    sonarr.fetchLibrary.mockReturnValue(
      of({
        syncedAt: null,
        instances: [
          {
            instanceId: 'sonarr-1',
            instanceName: 'Sonarr',
            rows: [
              {
                instanceName: 'Sonarr',
                seriesId: 1,
                seriesTitle: 'Star Gate',
                seriesTitleSlug: 'star-gate',
                qualityProfileName: 'HD',
                status: 'continuing',
                monitored: true,
                seasons: [],
                totalEpisodes: 0,
                airedEpisodes: 0,
              },
              {
                instanceName: 'Sonarr',
                seriesId: 2,
                seriesTitle: 'Nova Point',
                seriesTitleSlug: 'nova-point',
                qualityProfileName: 'HD',
                status: 'continuing',
                monitored: true,
                seasons: [],
                totalEpisodes: 0,
                airedEpisodes: 0,
              },
            ],
          },
        ],
      }),
    );
    radarr.fetchLibrary.mockReturnValue(
      of({
        syncedAt: null,
        instances: [
          {
            instanceId: 'radarr-1',
            instanceName: 'Radarr',
            rows: [
              {
                instanceName: 'Radarr',
                movieId: 3,
                movieTitle: 'Star Dust',
                movieTitleSlug: 'star-dust',
                qualityProfileName: 'HD',
                status: 'released',
                monitored: true,
                hasFile: true,
                airDateUtc: null,
                customFormatScore: 100,
                resolution: '1080p',
                size: 1024,
              },
              {
                instanceName: 'Radarr',
                movieId: 4,
                movieTitle: 'Comet Run',
                movieTitleSlug: 'comet-run',
                qualityProfileName: 'HD',
                status: 'released',
                monitored: true,
                hasFile: true,
                airDateUtc: null,
                customFormatScore: 80,
                resolution: '1080p',
                size: 2048,
              },
            ],
          },
        ],
      }),
    );
    TestBed.configureTestingModule({
      providers: [
        { provide: MediaTitlesService, useValue: { revision: signal('en:0') } },
        { provide: SonarrApiService, useValue: sonarr },
        { provide: RadarrApiService, useValue: radarr },
        { provide: AppI18nService, useValue: { t: (key: string) => key } },
        {
          provide: AppSettingsService,
          useValue: {
            sonarrInstances: signal([{ id: 'sonarr-1', name: 'Sonarr', baseUrl: 'http://sonarr', apiKey: 'test' }]),
            radarrInstances: signal([{ id: 'radarr-1', name: 'Radarr', baseUrl: 'http://radarr', apiKey: 'test' }]),
            loadError: signal(null),
            titleSorting: signal({ enabled: false, ignoredLeadingWords: [] }),
            loadInstances: vi.fn().mockResolvedValue(undefined),
          },
        },
      ],
    });
    page = TestBed.runInInjectionContext(() => new LibraryPage());
    await page['loadFilterValues']();
    page['loadData']();
  });

  it('finds source and alternative titles after localization', () => {
    const table = page['radarrInstanceTables']()[0];
    const movie = table.movies[0];
    page['radarrInstanceTables'].set([
      { ...table, movies: [{ ...movie, title: 'Deutscher Titel', searchTitles: ['Original title', 'English alias'] }] },
    ]);
    page['titleSearch'].set('english alias');
    expect(page['visibleRadarrTables']()[0].movies[0].title).toBe('Deutscher Titel');
    expect(page['gridItems']().some((item) => item.title === 'Deutscher Titel')).toBe(true);
  });

  it('shows all loaded entries when no search is set', () => {
    expect(page['visibleItemCount']()).toBe(4);
    expect(page['gridItems']()).toHaveLength(4);
    expect(page['noDataAvailable']()).toBe(false);
    expect(page['isFiltered']()).toBe(false);
  });

  it('keeps only movies and series whose title contains the query, case-insensitively', () => {
    page['titleSearch'].set('  StAr  ');

    expect(page['visibleSonarrTables']().flatMap((table) => table.series.map((series) => series.title))).toEqual(['Star Gate']);
    expect(page['visibleRadarrTables']().flatMap((table) => table.movies.map((movie) => movie.title))).toEqual(['Star Dust']);
    expect(page['visibleItemCount']()).toBe(2);
    expect(
      page['gridItems']()
        .map((item) => item.title)
        .sort(),
    ).toEqual(['Star Dust', 'Star Gate']);
    expect(page['isFiltered']()).toBe(true);
  });

  it('filters client-side without reloading the library from the backend', () => {
    sonarr.fetchLibrary.mockClear();
    radarr.fetchLibrary.mockClear();

    page['titleSearch'].set('star');

    expect(sonarr.fetchLibrary).not.toHaveBeenCalled();
    expect(radarr.fetchLibrary).not.toHaveBeenCalled();
  });

  it('reports no data available when nothing matches the search', () => {
    page['titleSearch'].set('nothing matches');

    expect(page['visibleItemCount']()).toBe(0);
    expect(page['gridItems']()).toHaveLength(0);
    expect(page['noDataAvailable']()).toBe(true);
  });

  it('is filtered when a search or an active filter is present', () => {
    page['titleSearch'].set('star');
    expect(page['isFiltered']()).toBe(true);

    page['titleSearch'].set('   ');
    expect(page['isFiltered']()).toBe(false);
    expect(page['visibleItemCount']()).toBe(4);

    page['activeFilterCount'].set(1);
    expect(page['isFiltered']()).toBe(true);
  });
});
