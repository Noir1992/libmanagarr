import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { FilterOptionType } from '../../enums/filter-option-type';
import { MultiSelectFilterOption, SelectFilterOption } from '../../interfaces/filter-option';
import { LibraryFilterValues } from '../../interfaces/library/library-filter-values';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { RadarrApiService } from '../../services/radarr-api.service';
import { SonarrApiService } from '../../services/sonarr-api.service';
import { LibraryPage } from './library.page';

describe('LibraryPage file status', () => {
  let page: LibraryPage;
  let radarr: jasmine.SpyObj<RadarrApiService>;
  let sonarr: jasmine.SpyObj<SonarrApiService>;
  const sonarrInstances = signal([{ id: 's', name: 'Sonarr', baseUrl: '', apiKey: '' }]);
  const empty: LibraryFilterValues = { genres: [], tags: [], statuses: [], qualityProfiles: [], audioLanguages: [], subtitleLanguages: [] };

  beforeEach(() => {
    sonarrInstances.set([{ id: 's', name: 'Sonarr', baseUrl: '', apiKey: '' }]);
    radarr = jasmine.createSpyObj<RadarrApiService>('Radarr', ['fetchFilterValues', 'fetchLibrary']);
    sonarr = jasmine.createSpyObj<SonarrApiService>('Sonarr', ['fetchFilterValues', 'fetchLibrary']);
    radarr.fetchFilterValues.and.returnValue(
      of({
        ...empty,
        counts: {
          'file-status-filter': { __all__: 3, present: 1, missing: 2 },
          'episode-monitored-filter': { __all__: 3, monitored: 1, notMonitored: 2 },
          'episode-type-filter': { __all__: 3, movie: 3 },
        },
      }),
    );
    sonarr.fetchFilterValues.and.returnValue(
      of({
        ...empty,
        counts: {
          'file-status-filter': { __all__: 2, present: 1, partial: 1 },
          'episode-monitored-filter': { __all__: 4, monitored: 3, notMonitored: 1 },
          'episode-type-filter': { __all__: 4, regular: 3, special: 1 },
        },
      }),
    );
    radarr.fetchLibrary.and.returnValue(of({ syncedAt: null, instances: [] }));
    sonarr.fetchLibrary.and.returnValue(of({ syncedAt: null, instances: [] }));
    TestBed.configureTestingModule({
      providers: [
        {
          provide: AppSettingsService,
          useValue: {
            sonarrInstances,
            radarrInstances: signal([{ id: 'r', name: 'Radarr', baseUrl: '', apiKey: '' }]),
            loadError: signal(null),
            titleSorting: signal({ enabled: false, ignoredLeadingWords: [] }),
          },
        },
        { provide: RadarrApiService, useValue: radarr },
        { provide: SonarrApiService, useValue: sonarr },
      ],
    });
    TestBed.inject(AppI18nService).setLanguage('en');
    page = TestBed.runInInjectionContext(() => new LibraryPage());
  });

  function filter(id: string): SelectFilterOption {
    const option = page['filterOptions']().find((option) => option.id === id);
    if (option?.type !== FilterOptionType.SELECT) throw new Error(`Missing ${id}`);
    return option;
  }

  it('starts with all entries and merges file counts while retaining an existing selection', async () => {
    expect(filter('file-status-filter').selectedValue).toBe('all');
    expect(filter('file-status-filter').options.map((option) => option.label)).toEqual([
      'All',
      'Available',
      'Partially available',
      'Missing',
    ]);
    expect(page['filterOptions']().find((option) => option.id === 'status-filter')?.label).toBe('Aired status');
    filter('file-status-filter').selectedValue = 'missing';
    await page['loadFilterValues']();
    expect(filter('file-status-filter').selectedValue).toBe('missing');
    expect(filter('file-status-filter').options.map((option) => option.count)).toEqual([5, 2, 1, 2]);
    expect(filter('episode-monitored-filter').label).toBe('Monitored');
    expect(filter('monitored-filter').label).toBe('Series monitored');
    expect(filter('episode-monitored-filter').options.map((option) => option.count)).toEqual([7, 4, 3]);
    const type = page['filterOptions']().find((option) => option.id === 'episode-type-filter') as MultiSelectFilterOption;
    expect(type.type).toBe(FilterOptionType.MULTISELECT);
    expect(type.label).toBe('Type');
    expect(type.selectedValues).toEqual([]);
    expect(type.options.map((option) => option.count)).toEqual([3, 3, 1]);
    expect(type.totalCount).toBe(7);
  });

  it('combines missing files, series monitoring, episode monitoring, aired and regular episodes in one request', () => {
    for (const [id, selection] of Object.entries({
      'file-status-filter': 'missing',
      'monitored-filter': 'monitored',
      'episode-monitored-filter': 'monitored',
      'aired-filter': 'aired',
    })) {
      filter(id).selectedValue = selection;
    }
    (page['filterOptions']().find((option) => option.id === 'episode-type-filter') as MultiSelectFilterOption).selectedValues = [
      'regular',
      'movie',
    ];
    page['loadData']();
    const expected = jasmine.objectContaining({
      fileStatusFilter: 'missing',
      monitoredFilter: 'monitored',
      episodeMonitoredFilter: 'monitored',
      airedFilter: 'aired',
      types: ['regular', 'movie'],
    });
    expect(radarr.fetchLibrary).toHaveBeenCalledOnceWith(expected);
    expect(sonarr.fetchLibrary).toHaveBeenCalledOnceWith(expected);
  });

  it('omits Sonarr-only controls and request values when there are no Sonarr instances', () => {
    sonarrInstances.set([]);
    expect(page['filterOptions']().some((option) => option.id === 'monitored-filter')).toBeFalse();
    page['loadData']();
    expect(radarr.fetchLibrary).toHaveBeenCalledWith(
      jasmine.objectContaining({ monitoredFilter: undefined, episodeMonitoredFilter: 'all', types: ['__all__'] }),
    );
    expect(sonarr.fetchLibrary).not.toHaveBeenCalled();
  });

  it('retains missing file and episode monitoring flags and retains an absent score for the detail dialog', () => {
    page['upsertRadarrInstanceTable']({
      instanceId: 'r',
      instanceName: 'Radarr',
      rows: [
        {
          movieId: 1,
          instanceName: 'Radarr',
          movieTitle: 'Movie',
          movieTitleSlug: 'movie',
          qualityProfileName: 'HD',
          status: 'released',
          airDateUtc: null,
          monitored: true,
          hasFile: false,
          customFormatScore: null,
          resolution: null,
          size: null,
        },
      ],
    });
    page['upsertSonarrInstanceTable']({
      instanceId: 's',
      instanceName: 'Sonarr',
      rows: [
        {
          seriesId: 1,
          instanceName: 'Sonarr',
          seriesTitle: 'Series',
          seriesTitleSlug: 'series',
          qualityProfileName: 'HD',
          status: 'continuing',
          monitored: true,
          libraryStatus: 'missing',
          totalEpisodes: 1,
          airedEpisodes: 0,
          seasons: [
            {
              number: 1,
              monitored: true,
              episodes: [{ number: 1, title: 'Episode', airDateUtc: null, hasFile: false, monitored: true, customFormatScore: null }],
            },
          ],
        },
      ],
    });
    const movie = page['gridItems']()[0];
    expect(movie.detailRows).toContain({ label: 'Library status', value: 'Missing', tone: 'danger' });
    expect(movie.movieData?.customFormatScore).toBeNull();
    expect(page['gridItems']()[1].detailRows).toContain({ label: 'Library status', value: 'Missing', tone: 'danger' });
    expect(movie.detailRows.map((row) => row.label)).toEqual(['Instance', 'Aired status', 'Library status', 'Monitored']);
    expect(page['gridItems']()[1].detailRows.map((row) => row.label)).toEqual([
      'Instance',
      'Aired status',
      'Library status',
      'Series monitored',
    ]);
    expect(page['gridItems']()[1].seriesData?.seasons[0].episodes[0].details).toEqual(
      jasmine.objectContaining({
        instanceName: 'Sonarr',
        seriesTitle: 'Series',
        seasonNumber: 1,
        episodeNumber: 1,
        hasFile: false,
        monitored: true,
        customFormatScore: null,
      }),
    );
    expect(page['gridItems']()[1].seriesData?.seasons[0].episodes[0]).toEqual(
      jasmine.objectContaining({ hasFile: false, monitored: true, customFormatScore: null }),
    );
    expect(page['sonarrEpisodeColumns']().map((column) => column.key)).toContain('hasFile');
  });

  it('requires a file even when all missing episodes are unmonitored and the backend sends no availability flag', () => {
    const aired = new Date(Date.now() - 86400000).toISOString();
    page['upsertSonarrInstanceTable']({
      instanceId: 's',
      instanceName: 'Sonarr',
      rows: [
        {
          seriesId: 1,
          instanceName: 'Sonarr',
          seriesTitle: 'Series',
          seriesTitleSlug: 'series',
          qualityProfileName: 'HD',
          status: 'continuing',
          monitored: true,
          totalEpisodes: 2,
          airedEpisodes: 2,
          seasons: [
            {
              number: 1,
              monitored: true,
              episodes: [{ number: 1, title: 'Episode', airDateUtc: aired, hasFile: false, monitored: true, customFormatScore: null }],
            },
            {
              number: 0,
              monitored: false,
              episodes: [{ number: 0, title: 'Special', airDateUtc: aired, hasFile: false, monitored: false, customFormatScore: null }],
            },
          ],
        },
        {
          seriesId: 2,
          instanceName: 'Sonarr',
          seriesTitle: 'Only Specials Missing',
          seriesTitleSlug: 'only-specials-missing',
          qualityProfileName: 'HD',
          status: 'continuing',
          monitored: true,
          totalEpisodes: 1,
          airedEpisodes: 1,
          seasons: [
            {
              number: 0,
              monitored: false,
              episodes: [{ number: 0, title: 'Special', airDateUtc: aired, hasFile: false, monitored: false, customFormatScore: null }],
            },
          ],
        },
      ],
    });
    const cards = page['gridItems']();
    expect(cards.find((item) => item.title === 'Series')?.detailRows).toContain({
      label: 'Library status',
      value: 'Missing',
      tone: 'danger',
    });
    expect(cards.find((item) => item.title === 'Only Specials Missing')?.detailRows).toContain({
      label: 'Library status',
      value: 'Missing',
      tone: 'danger',
    });
  });

  for (const scenario of [
    { status: 'upcoming', airDateUtc: '2099-01-01T00:00:00Z', hasFile: false, value: 'Missing', tone: 'danger' },
    { status: 'upcoming', airDateUtc: null, hasFile: false, value: 'Missing', tone: 'danger' },
    { status: 'continuing', airDateUtc: null, hasFile: false, value: 'Missing', tone: 'danger' },
    { status: 'upcoming', airDateUtc: '2099-01-01T00:00:00Z', hasFile: true, value: 'Available', tone: 'success' },
    { status: 'continuing', airDateUtc: '2020-01-01T00:00:00Z', hasFile: true, value: 'Available', tone: 'success' },
  ] as const) {
    it(`shows ${scenario.value} for ${scenario.status} with file=${scenario.hasFile} and date=${scenario.airDateUtc}`, () => {
      page['upsertSonarrInstanceTable']({
        instanceId: 's',
        instanceName: 'Sonarr',
        rows: [
          {
            seriesId: 1,
            instanceName: 'Sonarr',
            seriesTitle: 'Series',
            seriesTitleSlug: 'series',
            qualityProfileName: 'HD',
            status: scenario.status,
            monitored: true,
            totalEpisodes: 1,
            airedEpisodes: 0,
            seasons: [
              {
                number: 1,
                monitored: true,
                episodes: [
                  {
                    number: 1,
                    title: 'Episode',
                    hasFile: scenario.hasFile,
                    monitored: true,
                    airDateUtc: scenario.airDateUtc,
                    customFormatScore: null,
                  },
                ],
              },
            ],
          },
        ],
      });

      expect(page['gridItems']()[0].detailRows).toContain({
        label: 'Library status',
        value: scenario.value,
        tone: scenario.tone,
      });
    });
  }

  it('shows partially available when the backend reports a partial library status', () => {
    page['upsertSonarrInstanceTable']({
      instanceId: 's',
      instanceName: 'Sonarr',
      rows: [
        {
          seriesId: 1,
          instanceName: 'Sonarr',
          seriesTitle: 'Series',
          seriesTitleSlug: 'series',
          qualityProfileName: 'HD',
          status: 'ended',
          monitored: true,
          libraryStatus: 'partial',
          fileCount: 1,
          totalEpisodes: 1,
          airedEpisodes: 1,
          seasons: [
            {
              number: 1,
              monitored: true,
              episodes: [
                {
                  number: 1,
                  title: 'Episode',
                  airDateUtc: '2020-01-01T00:00:00Z',
                  hasFile: true,
                  monitored: true,
                  customFormatScore: null,
                },
              ],
            },
          ],
        },
      ],
    });

    expect(page['gridItems']()[0].detailRows).toContain({ label: 'Library status', value: 'Partially available', tone: 'warning' });
    expect(page['gridItems']()[0].seriesData?.libraryStatus).toBe('partial');
  });

  it('computes partially available locally when a monitored episode lacks a file and the backend sends no status', () => {
    page['upsertSonarrInstanceTable']({
      instanceId: 's',
      instanceName: 'Sonarr',
      rows: [
        {
          seriesId: 1,
          instanceName: 'Sonarr',
          seriesTitle: 'Series',
          seriesTitleSlug: 'series',
          qualityProfileName: 'HD',
          status: 'continuing',
          monitored: true,
          totalEpisodes: 2,
          airedEpisodes: 1,
          seasons: [
            {
              number: 1,
              monitored: true,
              episodes: [
                {
                  number: 1,
                  title: 'Downloaded',
                  airDateUtc: '2020-01-01T00:00:00Z',
                  hasFile: true,
                  monitored: true,
                  customFormatScore: null,
                },
                {
                  number: 2,
                  title: 'Pending',
                  airDateUtc: '2099-01-01T00:00:00Z',
                  hasFile: false,
                  monitored: true,
                  customFormatScore: null,
                },
              ],
            },
          ],
        },
      ],
    });

    expect(page['gridItems']()[0].detailRows).toContain({ label: 'Library status', value: 'Partially available', tone: 'warning' });
  });

  it('uses the backend library status even when the visible episodes are all in the future', () => {
    page['upsertSonarrInstanceTable']({
      instanceId: 's',
      instanceName: 'Sonarr',
      rows: [
        {
          seriesId: 1,
          instanceName: 'Sonarr',
          seriesTitle: 'Series',
          seriesTitleSlug: 'series',
          qualityProfileName: 'HD',
          status: 'continuing',
          monitored: true,
          totalEpisodes: 1,
          airedEpisodes: 0,
          libraryStatus: 'missing',
          fileCount: 0,
          seasons: [
            {
              number: 1,
              monitored: true,
              episodes: [
                {
                  number: 2,
                  title: 'Future',
                  hasFile: false,
                  monitored: true,
                  airDateUtc: '2099-01-01T00:00:00Z',
                  customFormatScore: null,
                },
              ],
            },
          ],
        },
      ],
    });

    expect(page['gridItems']()[0].detailRows).toContain({ label: 'Library status', value: 'Missing', tone: 'danger' });
  });
});
