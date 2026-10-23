import { signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { FilterOptionType } from '../../enums/filter-option-type';
import { MultiSelectFilterOption } from '../../interfaces/filter-option';
import { LibraryFilterValues } from '../../interfaces/library/library-filter-values';
import { ArrInstanceSettings } from '../../interfaces/settings/arr-instance-settings';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { RadarrApiService } from '../../services/radarr-api.service';
import { SonarrApiService } from '../../services/sonarr-api.service';
import { LibraryPage } from './library.page';

describe('LibraryPage instance filters', () => {
  let page: LibraryPage;
  let sonarr: jasmine.SpyObj<SonarrApiService>;
  let radarr: jasmine.SpyObj<RadarrApiService>;
  let sonarrInstances: WritableSignal<ArrInstanceSettings[]>;
  let radarrInstances: WritableSignal<ArrInstanceSettings[]>;

  beforeEach(() => {
    sonarr = jasmine.createSpyObj<SonarrApiService>('SonarrApiService', ['fetchLibrary', 'fetchFilterValues']);
    radarr = jasmine.createSpyObj<RadarrApiService>('RadarrApiService', ['fetchLibrary', 'fetchFilterValues']);
    const filterValues: LibraryFilterValues = {
      genres: ['Drama'],
      tags: ['Favorite'],
      statuses: ['released'],
      qualityProfiles: ['HD'],
      audioLanguages: ['English'],
      subtitleLanguages: ['German'],
    };
    sonarr.fetchFilterValues.and.returnValue(of(filterValues));
    radarr.fetchFilterValues.and.returnValue(of(filterValues));
    sonarr.fetchLibrary.and.returnValue(
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
                seriesTitle: 'Series',
                seriesTitleSlug: 'series',
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
    radarr.fetchLibrary.and.returnValue(
      of({
        syncedAt: null,
        instances: [
          {
            instanceId: 'radarr-1',
            instanceName: 'Radarr',
            rows: [
              {
                instanceName: 'Radarr',
                movieId: 2,
                movieTitle: 'Movie',
                movieTitleSlug: 'movie',
                qualityProfileName: 'HD',
                status: 'released',
                monitored: true,
                hasFile: true,
                airDateUtc: null,
                customFormatScore: 100,
                resolution: '1080p',
                size: 1024,
              },
            ],
          },
        ],
      }),
    );
    sonarrInstances = signal([{ id: 'sonarr-1', name: 'Sonarr', baseUrl: 'http://sonarr', apiKey: 'test' }]);
    radarrInstances = signal([{ id: 'radarr-1', name: 'Radarr', baseUrl: 'http://radarr', apiKey: 'test' }]);
    TestBed.configureTestingModule({
      providers: [
        { provide: SonarrApiService, useValue: sonarr },
        { provide: RadarrApiService, useValue: radarr },
        { provide: AppI18nService, useValue: { t: (key: string) => key } },
        {
          provide: AppSettingsService,
          useValue: {
            sonarrInstances,
            radarrInstances,
            loadError: signal(null),
            titleSorting: signal({ enabled: false, ignoredLeadingWords: [] }),
            loadInstances: jasmine.createSpy().and.resolveTo(),
          },
        },
      ],
    });
    page = TestBed.runInInjectionContext(() => new LibraryPage());
  });

  function instanceFilter(kind: 'sonarr' | 'radarr'): MultiSelectFilterOption {
    const filter = page['filterOptions']().find((option) => option.id === `${kind}-instance-filter`);
    if (filter?.type !== FilterOptionType.MULTISELECT) {
      throw new Error(`Missing ${kind} instance filter`);
    }
    return filter;
  }

  for (const excluded of ['sonarr', 'radarr'] as const) {
    it(`should include ${excluded} again when its instance selection is cleared`, async () => {
      await page['loadFilterValues']();
      page['loadData']();
      expect(page['gridItems']()).toHaveSize(2);
      sonarr.fetchLibrary.calls.reset();
      radarr.fetchLibrary.calls.reset();
      sonarr.fetchFilterValues.calls.reset();
      radarr.fetchFilterValues.calls.reset();
      const filter = instanceFilter(excluded);
      filter.selectedValues = [];

      await filter.onChanged([]);

      const excludedApi = excluded === 'sonarr' ? sonarr : radarr;
      const includedApi = excluded === 'sonarr' ? radarr : sonarr;
      expect(excludedApi.fetchFilterValues).toHaveBeenCalledOnceWith(['__all__']);
      expect(excludedApi.fetchLibrary).toHaveBeenCalledTimes(1);
      expect(includedApi.fetchLibrary).toHaveBeenCalledTimes(1);
      const request = includedApi.fetchLibrary.calls.mostRecent().args[0];
      expect(request.sonarrInstanceFilter).toEqual(['__all__']);
      expect(request.radarrInstanceFilter).toEqual(['__all__']);
      expect(instanceFilter(excluded).selectedValues).toEqual([]);
      expect(
        page['gridItems']()
          .map((item) => item.kind)
          .sort(),
      ).toEqual(['movie', 'series']);
      expect(page['visibleItemCount']()).toBe(2);
    });
  }

  it('should load both sources with no instance selections', async () => {
    instanceFilter('sonarr').selectedValues = [];
    instanceFilter('radarr').selectedValues = [];

    await page['loadFilterValues']();
    page['loadData']();

    expect(sonarr.fetchFilterValues).toHaveBeenCalledOnceWith(['__all__']);
    expect(radarr.fetchFilterValues).toHaveBeenCalledOnceWith(['__all__']);
    expect(sonarr.fetchLibrary).toHaveBeenCalledTimes(1);
    expect(radarr.fetchLibrary).toHaveBeenCalledTimes(1);
    expect(page['loading']()).toBeFalse();
    expect(page['initialLoadComplete']()).toBeTrue();
    expect(page['loadError']()).toBeNull();
    expect(page['gridItems']()).toHaveSize(2);
  });

  it('should skip a source when all of its instances are excluded', async () => {
    const filter = instanceFilter('radarr');
    filter.selectedValues = ['__all__'];
    filter.exclude = true;
    expect(filter.excludeAllRestricts).toBeTrue();

    page['loadData']();

    expect(radarr.fetchLibrary).not.toHaveBeenCalled();
    expect(sonarr.fetchLibrary).toHaveBeenCalledTimes(1);
    const request = sonarr.fetchLibrary.calls.mostRecent().args[0];
    expect(request.radarrInstanceFilter).toEqual(['__all__']);
    expect(request.excludedFilters).toContain('radarr-instance-filter');
    expect(page['visibleItemCount']()).toBe(1);
  });

  it('should keep both sources when an instance exclusion has no selection', async () => {
    const filter = instanceFilter('radarr');
    filter.selectedValues = [];
    filter.exclude = true;

    page['loadData']();

    expect(radarr.fetchLibrary).toHaveBeenCalledTimes(1);
    expect(sonarr.fetchLibrary).toHaveBeenCalledTimes(1);
    const request = radarr.fetchLibrary.calls.mostRecent().args[0];
    expect(request.radarrInstanceFilter).toEqual(['__all__']);
    expect(request.excludedFilters).not.toContain('radarr-instance-filter');
    expect(page['gridItems']()).toHaveSize(2);
  });

  for (const absent of ['sonarr', 'radarr'] as const) {
    it(`should load with no ${absent} instances configured`, async () => {
      (absent === 'sonarr' ? sonarrInstances : radarrInstances).set([]);

      await page['loadFilterValues']();
      page['loadData']();

      expect((absent === 'sonarr' ? sonarr : radarr).fetchLibrary).not.toHaveBeenCalled();
      expect((absent === 'sonarr' ? radarr : sonarr).fetchLibrary).toHaveBeenCalledTimes(1);
      expect(page['loading']()).toBeFalse();
      expect(page['visibleItemCount']()).toBe(1);
    });
  }

  it('should keep other filter selections when available options are refreshed', async () => {
    const filters = page['filterOptions']();
    for (const filter of filters) {
      if (filter.type === FilterOptionType.SELECT) filter.selectedValue = 'custom';
      if (filter.type === FilterOptionType.MULTISELECT) filter.selectedValues = ['selected'];
      if (filter.type === FilterOptionType.NUMBER_RANGE) {
        filter.minValue = 10;
        filter.maxValue = 100;
      }
      if (filter.type === FilterOptionType.DATE_RANGE) {
        filter.startDate = '2020-01-01';
        filter.endDate = '2020-12-31';
      }
    }

    await page['loadFilterValues']();

    const request = page['buildLibraryRequestFilters']();
    expect(request.airedFilter).toBe('custom');
    expect(request.monitoredFilter).toBe('custom');
    expect(request.genres).toEqual(['selected']);
    expect(request.tags).toEqual(['selected']);
    expect(request.statuses).toEqual(['selected']);
    expect(request.profiles).toEqual(['selected']);
    expect(request.audioLanguages).toEqual(['selected']);
    expect(request.subtitleLanguages).toEqual(['selected']);
    expect(request.scoreMin).toBe(10);
    expect(request.scoreMax).toBe(100);
    expect(request.airedFrom).toBe('2020-01-01');
    expect(request.airedTo).toBe('2020-12-31');
    const genre = page['filterOptions']().find((filter) => filter.id === 'genre-filter');
    if (genre?.type !== FilterOptionType.MULTISELECT) throw new Error('Missing genre filter');
    expect(genre.options).toEqual([{ value: 'Drama', label: 'Drama', count: undefined }]);
  });
  it('cancels both obsolete library requests and cancels on destruction', () => {
    const sonarrPending = new Subject<never>();
    const radarrPending = new Subject<never>();
    sonarr.fetchLibrary.and.returnValue(sonarrPending);
    radarr.fetchLibrary.and.returnValue(radarrPending);
    page['loadData']();
    expect(sonarrPending.observers).toHaveSize(1);
    expect(radarrPending.observers).toHaveSize(1);
    page['loadData']();
    expect(sonarrPending.observers).toHaveSize(1);
    expect(radarrPending.observers).toHaveSize(1);
    page.ngOnDestroy();
    expect(sonarrPending.observed).toBeFalse();
    expect(radarrPending.observed).toBeFalse();
  });

  it('debounces ranges into one request with the latest bounds', () => {
    jasmine.clock().install();
    try {
      const range = page['filterOptions']().find((filter) => filter.id === 'score-range');
      if (range?.type !== FilterOptionType.NUMBER_RANGE) throw new Error('Missing score range');
      range.minValue = 10;
      range.onChanged({ minValue: range.minValue, maxValue: range.maxValue });
      jasmine.clock().tick(100);
      range.minValue = 20;
      range.onChanged({ minValue: range.minValue, maxValue: range.maxValue });
      jasmine.clock().tick(199);
      expect(radarr.fetchLibrary).not.toHaveBeenCalled();
      jasmine.clock().tick(1);
      expect(radarr.fetchLibrary).toHaveBeenCalledOnceWith(jasmine.objectContaining({ scoreMin: 20 }));
      expect(sonarr.fetchLibrary).toHaveBeenCalledTimes(1);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('cancels old option requests and never replaces current options with old results', async () => {
    const oldOptions = new Subject<LibraryFilterValues>();
    sonarr.fetchFilterValues.and.returnValue(oldOptions);
    const oldLoad = page['loadFilterValues']();
    expect(oldOptions.observed).toBeTrue();
    sonarr.fetchFilterValues.and.returnValue(
      of({ genres: ['Current'], tags: [], statuses: [], qualityProfiles: [], audioLanguages: [], subtitleLanguages: [] }),
    );
    expect(await page['loadFilterValues']()).toBeTrue();
    expect(await oldLoad).toBeFalse();
    expect(oldOptions.observed).toBeFalse();
    const genre = page['filterOptions']().find((filter) => filter.id === 'genre-filter');
    if (genre?.type !== FilterOptionType.MULTISELECT) throw new Error('Missing genre filter');
    expect(genre.options.map((option) => option.value)).toContain('Current');
  });
  it('includes the Added range and preserves it with exclusion after options refresh', async () => {
    const range = page['filterOptions']().find((filter) => filter.id === 'added-range');
    if (range?.type !== FilterOptionType.DATE_RANGE) throw new Error('Missing Added range');
    expect(range.startDate).toBeNull();
    expect(range.endDate).toBeNull();
    expect(range.supportsExclusion).toBeTrue();
    range.startDate = '2026-01-10';
    range.endDate = '2026-01-31';
    range.exclude = true;
    await page['loadFilterValues']();
    page['loadData']();
    const request = jasmine.objectContaining({ addedFrom: '2026-01-10', addedTo: '2026-01-31', excludedFilters: ['added-range'] });
    expect(radarr.fetchLibrary).toHaveBeenCalledOnceWith(request);
    expect(sonarr.fetchLibrary).toHaveBeenCalledOnceWith(request);
  });

  it('keeps entity addition dates in movie details and series cards', () => {
    const movieRows = page['buildMovieRows'](
      [
        {
          instanceName: 'Radarr',
          movieId: 1,
          movieTitle: 'Movie',
          movieTitleSlug: 'movie',
          qualityProfileName: 'HD',
          status: 'released',
          addedAt: '2026-01-10T12:00:00Z',
          airDateUtc: '2020-01-01T12:00:00Z',
          monitored: true,
          hasFile: false,
          customFormatScore: null,
          resolution: null,
          size: null,
        },
      ],
      radarrInstances()[0],
    );
    const seriesRows = page['buildSeriesRows'](
      [
        {
          instanceName: 'Sonarr',
          seriesId: 1,
          seriesTitle: 'Series',
          seriesTitleSlug: 'series',
          qualityProfileName: 'HD',
          status: 'continuing',
          addedAt: '2026-01-11T12:00:00Z',
          monitored: true,
          seasons: [],
          totalEpisodes: 0,
          airedEpisodes: 0,
        },
      ],
      sonarrInstances()[0],
    );
    page['radarrInstanceTables'].set([{ instanceId: 'radarr-1', instanceName: 'Radarr', syncedAt: null, movies: movieRows }]);
    page['sonarrInstanceTables'].set([{ instanceId: 'sonarr-1', instanceName: 'Sonarr', syncedAt: null, series: seriesRows }]);
    const cards = page['gridItems']();
    expect(cards.find((item) => item.kind === 'movie')?.movieData?.addedAt).toBe('2026-01-10T12:00:00Z');
    expect(cards.find((item) => item.kind === 'series')?.seriesData?.addedAt).toBe('2026-01-11T12:00:00Z');
  });

  it('labels season 0 with the specials translation in season table rows', () => {
    expect(page['seasonNumberLabel'](0)).toBe('filter.episodeSpecial');
    expect(page['seasonNumberLabel'](2)).toBe('2');
  });
});
