import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { FilterOptionType } from '../../enums/filter-option-type';
import { SelectFilterOption } from '../../interfaces/filter-option';
import { LibraryFilterValues } from '../../interfaces/library/library-filter-values';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { RadarrApiService } from '../../services/radarr-api.service';
import { SonarrApiService } from '../../services/sonarr-api.service';
import { LibraryPage } from './library.page';

describe('LibraryPage upgrade target', () => {
  let page: LibraryPage;
  let radarr: jasmine.SpyObj<RadarrApiService>;
  let sonarr: jasmine.SpyObj<SonarrApiService>;
  const empty: LibraryFilterValues = { genres: [], tags: [], statuses: [], qualityProfiles: [], audioLanguages: [], subtitleLanguages: [] };

  beforeEach(() => {
    radarr = jasmine.createSpyObj<RadarrApiService>('Radarr', ['fetchFilterValues', 'fetchLibrary']);
    sonarr = jasmine.createSpyObj<SonarrApiService>('Sonarr', ['fetchFilterValues', 'fetchLibrary']);
    radarr.fetchFilterValues.and.returnValue(of({ ...empty, counts: { 'upgrade-target-filter': { __all__: 4, below: 1, reached: 2 } } }));
    sonarr.fetchFilterValues.and.returnValue(of({ ...empty, counts: { 'upgrade-target-filter': { __all__: 6, below: 3, reached: 2 } } }));
    radarr.fetchLibrary.and.returnValue(of({ syncedAt: null, instances: [] }));
    sonarr.fetchLibrary.and.returnValue(of({ syncedAt: null, instances: [] }));
    TestBed.configureTestingModule({
      providers: [
        {
          provide: AppSettingsService,
          useValue: {
            sonarrInstances: signal([{ id: 's', name: 'Sonarr', baseUrl: '', apiKey: '' }]),
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

  function upgradeFilter(): SelectFilterOption {
    const filter = page['filterOptions']().find((option) => option.id === 'upgrade-target-filter');
    if (filter?.type !== FilterOptionType.SELECT) throw new Error('Missing upgrade filter');
    return filter;
  }

  it('starts unrestricted and merges movie and episode counts while preserving selections', async () => {
    expect(upgradeFilter().selectedValue).toBe('all');
    upgradeFilter().selectedValue = 'below';
    await page['loadFilterValues']();
    expect(upgradeFilter().selectedValue).toBe('below');
    expect(upgradeFilter().options.map(({ value, count }) => ({ value, count }))).toEqual([
      { value: 'all', count: 10 },
      { value: 'below', count: 4 },
      { value: 'reached', count: 4 },
    ]);
  });

  for (const selection of ['all', 'below', 'reached']) {
    it(`sends ${selection} to both sources together with the existing score range`, () => {
      upgradeFilter().selectedValue = selection;
      const score = page['filterOptions']().find((filter) => filter.id === 'score-range');
      if (score?.type !== FilterOptionType.NUMBER_RANGE) throw new Error('Missing score range');
      score.minValue = -5;
      score.maxValue = 500;
      page['loadData']();
      const expected = jasmine.objectContaining({ upgradeTargetFilter: selection, scoreMin: -5, scoreMax: 500 });
      expect(radarr.fetchLibrary).toHaveBeenCalledOnceWith(expected);
      expect(sonarr.fetchLibrary).toHaveBeenCalledOnceWith(expected);
    });
  }

  it('sends exclusion and retains target scores in table and card data including zero and unknown', () => {
    upgradeFilter().selectedValue = 'below';
    upgradeFilter().exclude = true;
    page['loadData']();
    expect(radarr.fetchLibrary).toHaveBeenCalledWith(jasmine.objectContaining({ excludedFilters: ['upgrade-target-filter'] }));
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
          upgradeUntilCustomFormatScore: 0,
          status: 'released',
          airDateUtc: null,
          monitored: true,
          hasFile: true,
          customFormatScore: -1,
          resolution: null,
          size: 100,
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
          upgradeUntilCustomFormatScore: 100,
          status: 'continuing',
          monitored: true,
          totalEpisodes: 1,
          airedEpisodes: 1,
          seasons: [
            {
              number: 1,
              monitored: true,
              episodes: [{ number: 1, title: 'Episode', airDateUtc: null, hasFile: true, customFormatScore: 50 }],
            },
          ],
        },
      ],
    });
    expect(page['radarrInstanceTables']()[0].movies[0].upgradeUntilCustomFormatScore).toBe(0);
    expect(page['sonarrInstanceTables']()[0].series[0].upgradeUntilCustomFormatScore).toBe(100);
    expect(page['gridItems']()[0].movieData?.upgradeUntilCustomFormatScore).toBe(0);
    expect(page['gridItems']()[0].detailRows.map((row) => row.label)).toEqual(['Instance', 'Aired status', 'Library status', 'Monitored']);
    expect(page['gridItems']()[1].seriesData?.upgradeUntilCustomFormatScore).toBe(100);
    expect(page['radarrColumns']().map((column) => column.key)).toContain('customFormatScore');
    expect(page['sonarrSeriesColumns']().map((column) => column.key)).toContain('upgradeUntilCustomFormatScore');
    expect(page['sonarrEpisodeColumns']().map((column) => column.key)).toContain('customFormatScore');
    const movie = page['radarrInstanceTables']()[0].movies[0];
    page['radarrInstanceTables'].set([
      { instanceId: 'r', instanceName: 'Radarr', syncedAt: null, movies: [{ ...movie, upgradeUntilCustomFormatScore: null }] },
    ]);
    expect(page['gridItems']()[0].movieData?.upgradeUntilCustomFormatScore).toBeNull();
  });
});
