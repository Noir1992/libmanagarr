import { MediaTitlesService } from '../../services/media-titles.service';
import type { MockInstance } from 'vitest';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { FilterOptionType } from '../../enums/filter-option-type';
import { LibraryFilterValues } from '../../interfaces/library/library-filter-values';
import { AppSettingsService } from '../../services/app-settings.service';
import { RadarrApiService } from '../../services/radarr-api.service';
import { SonarrApiService } from '../../services/sonarr-api.service';
import { LibraryPage } from './library.page';

describe('LibraryPage media filters', () => {
  let page: LibraryPage;
  let radarr: Record<'fetchFilterValues' | 'fetchLibrary', MockInstance>;
  let sonarr: Record<'fetchFilterValues' | 'fetchLibrary', MockInstance>;
  const values: LibraryFilterValues = {
    genres: [],
    tags: [],
    statuses: [],
    qualityProfiles: [],
    audioLanguages: [],
    subtitleLanguages: [],
    resolutions: ['1080p'],
    videoCodecs: ['HEVC'],
    audioCodecs: ['AAC'],
    dynamicRanges: ['HDR10'],
    containerFormats: ['MKV'],
  };

  beforeEach(() => {
    radarr = {
      fetchFilterValues: vi.fn<RadarrApiService['fetchFilterValues']>(),
      fetchLibrary: vi.fn<RadarrApiService['fetchLibrary']>(),
    };
    sonarr = {
      fetchFilterValues: vi.fn<SonarrApiService['fetchFilterValues']>(),
      fetchLibrary: vi.fn<SonarrApiService['fetchLibrary']>(),
    };
    radarr.fetchFilterValues.mockReturnValue(of(values));
    sonarr.fetchFilterValues.mockReturnValue(of({ ...values, resolutions: ['2160p', '1080p'], videoCodecs: ['AV1'] }));
    radarr.fetchLibrary.mockReturnValue(of({ syncedAt: null, instances: [] }));
    sonarr.fetchLibrary.mockReturnValue(of({ syncedAt: null, instances: [] }));
    TestBed.configureTestingModule({
      providers: [
        { provide: MediaTitlesService, useValue: { revision: signal('en:0') } },
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

  it('merges source options and preserves selections and exclusions when options refresh', async () => {
    await page['loadFilterValues']();
    expect(page['arrInstanceFilters']().resolutions).toEqual(['1080p', '2160p']);
    expect(page['arrInstanceFilters']().videoCodecs).toEqual(['AV1', 'HEVC']);
    const filter = page['filterOptions']().find((option) => option.id === 'video-codec-filter');
    if (filter?.type !== FilterOptionType.MULTISELECT) throw new Error('Missing video codec filter');
    filter.selectedValues = ['HEVC'];
    filter.exclude = true;
    await page['loadFilterValues']();
    const restored = page['filterOptions']().find((option) => option.id === filter.id);
    if (restored?.type !== FilterOptionType.MULTISELECT) throw new Error('Missing refreshed filter');
    expect(restored.selectedValues).toEqual(['HEVC']);
    expect(restored.exclude).toBe(true);
  });

  it('sends all media filters to both sources and converts decimal Mbit/s to bit/s', () => {
    const selections: Record<string, string[]> = {
      'resolution-filter': ['1080p'],
      'video-codec-filter': ['HEVC'],
      'audio-codec-filter': ['AAC'],
      'dynamic-range-filter': ['HDR10'],
      'container-format-filter': ['MKV'],
    };
    for (const filter of page['filterOptions']()) {
      if (filter.type === FilterOptionType.MULTISELECT && selections[filter.id]) filter.selectedValues = selections[filter.id];
      if (filter.id === 'video-bitrate-range' && filter.type === FilterOptionType.NUMBER_RANGE) {
        filter.minValue = 12.5;
        filter.maxValue = 25.25;
        filter.exclude = true;
      }
    }
    page['loadData']();
    const expected = expect.objectContaining({
      resolutions: ['1080p'],
      videoCodecs: ['HEVC'],
      audioCodecs: ['AAC'],
      dynamicRanges: ['HDR10'],
      containerFormats: ['MKV'],
      videoBitrateMin: 12_500_000,
      videoBitrateMax: 25_250_000,
      excludedFilters: ['video-bitrate-range'],
    });
    expect(radarr.fetchLibrary).toHaveBeenCalledExactlyOnceWith(expected);
    expect(sonarr.fetchLibrary).toHaveBeenCalledExactlyOnceWith(expected);
  });

  it('offers the same resolution groups as statistics in descending resolution order', async () => {
    sonarr.fetchFilterValues.mockReturnValue(
      of({ ...values, resolutions: ['Unknown', 'SD', '480p', '576p', '720p', '1080p', '1440p', '2160p', '4320p'] }),
    );
    await page['loadFilterValues']();
    const filter = page['filterOptions']().find((option) => option.id === 'resolution-filter');
    if (filter?.type !== FilterOptionType.MULTISELECT) throw new Error('Missing resolution filter');
    expect(filter.options.map((option) => option.value)).toEqual(['4k', '1440p', '1080p', '720p', '576p', '480p', 'SD', 'unknown']);
    expect(filter.normalizeValue!('2160p')).toBe('4k');
    filter.selectedValues = ['2160p', '4320p'];
    await page['loadFilterValues']();
    const refreshed = page['filterOptions']().find((option) => option.id === 'resolution-filter');
    if (refreshed?.type !== FilterOptionType.MULTISELECT) throw new Error('Missing resolution filter');
    expect(refreshed.selectedValues).toEqual(['4k']);
  });

  it('leaves bitrate bounds unset until a range is entered', () => {
    page['loadData']();
    expect(radarr.fetchLibrary).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ videoBitrateMin: null, videoBitrateMax: null }));
  });
});
