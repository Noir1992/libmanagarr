import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { FilterOptionType } from '../../enums/filter-option-type';
import { RadarrLibraryBatchResponse } from '../../interfaces/library/radarr-library-batch-response';
import { MultiSelectFilterOption } from '../../interfaces/filter-option';
import { LibraryFilterValues } from '../../interfaces/library/library-filter-values';
import { AppSettingsService } from '../../services/app-settings.service';
import { RadarrApiService } from '../../services/radarr-api.service';
import { SonarrApiService } from '../../services/sonarr-api.service';
import { LibraryPage } from './library.page';

describe('LibraryPage filter counts', () => {
  let page: LibraryPage;
  let radarr: jasmine.SpyObj<RadarrApiService>;
  let sonarr: jasmine.SpyObj<SonarrApiService>;
  const empty: LibraryFilterValues = {
    genres: [],
    tags: [],
    statuses: [],
    qualityProfiles: [],
    audioLanguages: [],
    subtitleLanguages: [],
  };

  beforeEach(() => {
    radarr = jasmine.createSpyObj<RadarrApiService>('Radarr', ['fetchFilterValues', 'fetchLibrary']);
    sonarr = jasmine.createSpyObj<SonarrApiService>('Sonarr', ['fetchFilterValues', 'fetchLibrary']);
    radarr.fetchLibrary.and.returnValue(of({ syncedAt: null, instances: [], counts: {} }));
    sonarr.fetchLibrary.and.returnValue(of({ syncedAt: null, instances: [], counts: {} }));
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
    page = TestBed.runInInjectionContext(() => new LibraryPage());
  });

  it('adds movie and episode counts without creating duplicate options for casing differences', async () => {
    radarr.fetchFilterValues.and.returnValue(
      of({
        ...empty,
        videoCodecs: ['HEVC'],
        counts: {
          'video-codec-filter': { HEVC: 3, __all__: 3 },
          'monitored-filter': { monitored: 2, notMonitored: 1, __all__: 3 },
        },
      }),
    );
    sonarr.fetchFilterValues.and.returnValue(
      of({
        ...empty,
        videoCodecs: ['hevc', 'AV1'],
        counts: {
          'video-codec-filter': { hevc: 2, AV1: 1, __all__: 3 },
          'monitored-filter': { monitored: 1, __all__: 1 },
        },
      }),
    );
    await page['loadFilterValues']();
    const codec = page['filterOptions']().find((filter) => filter.id === 'video-codec-filter');
    if (codec?.type !== FilterOptionType.MULTISELECT) throw new Error('Missing codec filter');
    expect(codec.options).toEqual([
      { value: 'AV1', label: 'AV1', count: 1 },
      { value: 'HEVC', label: 'HEVC', count: 5 },
    ]);
    expect(codec.totalCount).toBe(6);
    const monitored = page['filterOptions']().find((filter) => filter.id === 'monitored-filter');
    if (monitored?.type !== FilterOptionType.SELECT) throw new Error('Missing monitored filter');
    expect(monitored.options.map((option) => option.count)).toEqual([4, 3, 1]);
  });

  it('keeps counts absent when an older backend response does not supply them', async () => {
    radarr.fetchFilterValues.and.returnValue(of({ ...empty, videoCodecs: ['HEVC'] }));
    sonarr.fetchFilterValues.and.returnValue(of(empty));
    await page['loadFilterValues']();
    const codec = page['filterOptions']().find((filter) => filter.id === 'video-codec-filter');
    if (codec?.type !== FilterOptionType.MULTISELECT) throw new Error('Missing codec filter');
    expect(codec.options[0].count).toBeUndefined();
    expect(codec.totalCount).toBeUndefined();
  });
  it('replaces full-library counts with current results while keeping options and selections', async () => {
    radarr.fetchFilterValues.and.returnValue(
      of({ ...empty, videoCodecs: ['HEVC', 'AV1'], counts: { 'video-codec-filter': { HEVC: 100, AV1: 50, __all__: 150 } } }),
    );
    sonarr.fetchFilterValues.and.returnValue(of(empty));
    await page['loadFilterValues']();
    const codec = () => page['filterOptions']().find((filter) => filter.id === 'video-codec-filter') as MultiSelectFilterOption;
    codec().selectedValues = ['HEVC'];
    radarr.fetchLibrary.and.returnValue(
      of({
        syncedAt: null,
        instances: [],
        counts: { 'video-codec-filter': { HEVC: 1, __all__: 1 }, 'episode-type-filter': { movie: 1, __all__: 1 } },
      }),
    );
    sonarr.fetchLibrary.and.returnValue(
      of({
        syncedAt: null,
        instances: [],
        counts: { 'video-codec-filter': { hevc: 2, __all__: 2 }, 'episode-type-filter': { regular: 2, __all__: 2 } },
      }),
    );
    page['loadData']();
    expect(codec().selectedValues).toEqual(['HEVC']);
    expect(codec().options.map((option) => [option.value, option.count])).toEqual([
      ['AV1', 0],
      ['HEVC', 3],
    ]);
    expect(codec().totalCount).toBe(3);
    const type = page['filterOptions']().find((filter) => filter.id === 'episode-type-filter') as MultiSelectFilterOption;
    expect(type.options.map((option) => option.count)).toEqual([1, 2, 0]);
    expect(type.totalCount).toBe(3);
    radarr.fetchLibrary.and.returnValue(of({ syncedAt: null, instances: [], counts: {} }));
    sonarr.fetchLibrary.and.returnValue(of({ syncedAt: null, instances: [], counts: {} }));
    page['loadData']();
    expect(codec().options.map((option) => option.count)).toEqual([0, 0]);
    expect(codec().totalCount).toBe(0);
    expect(codec().selectedValues).toEqual(['HEVC']);
  });

  it('ignores an older result arriving after a newer filter request', async () => {
    radarr.fetchFilterValues.and.returnValue(of({ ...empty, videoCodecs: ['HEVC'] }));
    sonarr.fetchFilterValues.and.returnValue(of(empty));
    await page['loadFilterValues']();
    const oldResult = new Subject<RadarrLibraryBatchResponse>();
    const newResult = new Subject<RadarrLibraryBatchResponse>();
    radarr.fetchLibrary.and.returnValues(oldResult, newResult);
    page['loadData']();
    page['loadData']();
    newResult.next({ syncedAt: null, instances: [], counts: { 'video-codec-filter': { HEVC: 1, __all__: 1 } } });
    newResult.complete();
    oldResult.next({ syncedAt: null, instances: [], counts: { 'video-codec-filter': { HEVC: 99, __all__: 99 } } });
    oldResult.complete();
    const codec = page['filterOptions']().find((filter) => filter.id === 'video-codec-filter') as MultiSelectFilterOption;
    expect(codec.options[0].count).toBe(1);
    expect(codec.totalCount).toBe(1);
    expect(page['loading']()).toBeFalse();
  });
});
