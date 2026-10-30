import { signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { LibrarySortDirection } from '../../enums/library-sort-direction';
import { LibrarySortField } from '../../enums/library-sort-field';
import { RadarrLibraryEntryResponse } from '../../interfaces/library/radarr-library-entry-response';
import { RadarrLibraryBatchResponse } from '../../interfaces/library/radarr-library-batch-response';
import { SonarrLibraryEntryResponse } from '../../interfaces/library/sonarr-library-entry-response';
import { TitleSortingSettings } from '../../interfaces/settings/title-sorting-settings';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { RadarrApiService } from '../../services/radarr-api.service';
import { SonarrApiService } from '../../services/sonarr-api.service';
import { LibraryPage } from './library.page';

describe('LibraryPage sorting', () => {
  let page: LibraryPage;
  let radarr: jasmine.SpyObj<RadarrApiService>;
  let sonarr: jasmine.SpyObj<SonarrApiService>;
  let titleSorting: WritableSignal<TitleSortingSettings>;
  const storageKey = 'sort:library';

  beforeEach(() => {
    localStorage.removeItem(storageKey);
    radarr = jasmine.createSpyObj<RadarrApiService>('Radarr', ['fetchFilterValues', 'fetchLibrary']);
    sonarr = jasmine.createSpyObj<SonarrApiService>('Sonarr', ['fetchFilterValues', 'fetchLibrary']);
    radarr.fetchFilterValues.and.returnValue(
      of({ genres: [], tags: [], statuses: [], qualityProfiles: [], audioLanguages: [], subtitleLanguages: [] }),
    );
    sonarr.fetchFilterValues.and.returnValue(
      of({ genres: [], tags: [], statuses: [], qualityProfiles: [], audioLanguages: [], subtitleLanguages: [] }),
    );
    radarr.fetchLibrary.and.returnValue(of({ syncedAt: null, instances: [] }));
    sonarr.fetchLibrary.and.returnValue(of({ syncedAt: null, instances: [] }));
    titleSorting = signal({ enabled: false, ignoredLeadingWords: [] });
    TestBed.configureTestingModule({
      providers: [
        {
          provide: AppSettingsService,
          useValue: {
            sonarrInstances: signal([{ id: 's', name: 'Sonarr', baseUrl: '', apiKey: '' }]),
            radarrInstances: signal([{ id: 'r', name: 'Radarr', baseUrl: '', apiKey: '' }]),
            loadError: signal(null),
            titleSorting,
          },
        },
        { provide: RadarrApiService, useValue: radarr },
        { provide: SonarrApiService, useValue: sonarr },
      ],
    });
    TestBed.inject(AppI18nService).setLanguage('en');
    page = TestBed.runInInjectionContext(() => new LibraryPage());
  });

  afterEach(() => {
    page['ngOnDestroy']();
    localStorage.removeItem(storageKey);
  });

  function movie(id: number, title: string, airDateUtc: string | null, addedAt: string | null): RadarrLibraryEntryResponse {
    return {
      instanceName: 'Radarr',
      movieId: id,
      movieTitle: title,
      movieTitleSlug: title.toLowerCase(),
      qualityProfileName: 'HD',
      status: 'released',
      addedAt,
      airDateUtc,
      monitored: true,
      hasFile: true,
      customFormatScore: null,
      resolution: null,
      size: null,
    };
  }

  function series(id: number, title: string, addedAt: string | null, airDates: (string | null)[]): SonarrLibraryEntryResponse {
    return {
      instanceName: 'Sonarr',
      seriesId: id,
      seriesTitle: title,
      seriesTitleSlug: title.toLowerCase(),
      qualityProfileName: 'HD',
      status: 'continuing',
      addedAt,
      monitored: true,
      totalEpisodes: airDates.length,
      airedEpisodes: airDates.length,
      seasons: [
        {
          number: 1,
          monitored: true,
          episodes: airDates.map((airDateUtc, index) => ({
            number: index + 1,
            title: `${title} ${index + 1}`,
            airDateUtc,
            hasFile: true,
            customFormatScore: null,
          })),
        },
      ],
    };
  }

  function fill(): void {
    page['upsertRadarrInstanceTable']({
      instanceId: 'r',
      instanceName: 'Radarr',
      rows: [movie(1, 'Zeta', '2024-01-01T00:00:00Z', '2020-01-01T00:00:00Z'), movie(2, 'Alpha', null, '2026-01-01T00:00:00Z')],
    });
    page['upsertSonarrInstanceTable']({
      instanceId: 's',
      instanceName: 'Sonarr',
      rows: [series(3, 'Mike', '2024-01-01T00:00:00Z', ['2021-01-01T00:00:00Z', '2022-01-01T00:00:00Z']), series(4, 'Bravo', null, [null])],
    });
  }

  function titles(): string[] {
    return page['gridItems']().map((item) => item.title);
  }

  it('requests the alphabetical ascending default on both sources', () => {
    page['loadData']();
    const expected = jasmine.objectContaining({
      sortField: LibrarySortField.ALPHABETICAL,
      sortDirection: LibrarySortDirection.ASCENDING,
    });
    expect(radarr.fetchLibrary).toHaveBeenCalledOnceWith(expected);
    expect(sonarr.fetchLibrary).toHaveBeenCalledOnceWith(expected);
  });

  it('sends the selected field and direction', () => {
    page['sortField'].set(LibrarySortField.AIRED);
    page['sortDirection'].set(LibrarySortDirection.DESCENDING);
    page['loadData']();
    const expected = jasmine.objectContaining({
      sortField: LibrarySortField.AIRED,
      sortDirection: LibrarySortDirection.DESCENDING,
    });
    expect(radarr.fetchLibrary).toHaveBeenCalledOnceWith(expected);
    expect(sonarr.fetchLibrary).toHaveBeenCalledOnceWith(expected);
  });

  it('keeps the sorting in local storage and restores it', () => {
    page['onSortFieldChanged'](LibrarySortField.ADDED);
    page['onSortDirectionChanged'](LibrarySortDirection.DESCENDING);
    expect(localStorage.getItem(storageKey)).toBe('added:descending');

    page['sortField'].set(LibrarySortField.ALPHABETICAL);
    page['sortDirection'].set(LibrarySortDirection.ASCENDING);
    page['restoreSort']();
    expect(page['sortField']()).toBe(LibrarySortField.ADDED);
    expect(page['sortDirection']()).toBe(LibrarySortDirection.DESCENDING);
  });

  it('ignores an unknown stored sorting', () => {
    localStorage.setItem(storageKey, 'runtime:nonsense');
    page['restoreSort']();
    expect(page['sortField']()).toBe(LibrarySortField.ALPHABETICAL);
    expect(page['sortDirection']()).toBe(LibrarySortDirection.ASCENDING);
  });

  it('orders the merged tiles by the selected field and trails entries without a date', () => {
    fill();

    expect(titles()).toEqual(['Alpha', 'Bravo', 'Mike', 'Zeta']);

    page['sortDirection'].set(LibrarySortDirection.DESCENDING);
    expect(titles()).toEqual(['Zeta', 'Mike', 'Bravo', 'Alpha']);

    page['sortField'].set(LibrarySortField.AIRED);
    expect(titles()).toEqual(['Zeta', 'Mike', 'Alpha', 'Bravo']);
    page['sortDirection'].set(LibrarySortDirection.ASCENDING);
    expect(titles()).toEqual(['Mike', 'Zeta', 'Alpha', 'Bravo']);

    page['sortField'].set(LibrarySortField.ADDED);
    expect(titles()).toEqual(['Zeta', 'Mike', 'Alpha', 'Bravo']);
    page['sortDirection'].set(LibrarySortDirection.DESCENDING);
    expect(titles()).toEqual(['Alpha', 'Mike', 'Zeta', 'Bravo']);
  });

  it('sorts mixed tiles by movie size and the complete series total', () => {
    page['upsertRadarrInstanceTable']({
      instanceId: 'r',
      instanceName: 'Radarr',
      rows: [
        { ...movie(1, 'Large movie', null, null), size: 9_000_000_000 },
        { ...movie(2, 'Small movie', null, null), size: 1_000_000_000 },
        movie(3, 'Unknown', null, null),
      ],
    });
    page['upsertSonarrInstanceTable']({
      instanceId: 's',
      instanceName: 'Sonarr',
      rows: [
        { ...series(4, 'Series', null, [null]), totalSize: 12_000_000_000 },
        { ...series(5, 'Empty series', null, []), totalSize: 0 },
      ],
    });
    page['sortField'].set(LibrarySortField.SIZE);
    expect(titles()).toEqual(['Empty series', 'Small movie', 'Large movie', 'Series', 'Unknown']);
    page['sortDirection'].set(LibrarySortDirection.DESCENDING);
    expect(titles()).toEqual(['Series', 'Large movie', 'Small movie', 'Empty series', 'Unknown']);
  });

  it('persists and restores size sorting and sends it to both library sources', () => {
    page['onSortFieldChanged'](LibrarySortField.SIZE);
    page['onSortDirectionChanged'](LibrarySortDirection.DESCENDING);
    expect(localStorage.getItem(storageKey)).toBe('size:descending');
    page['sortField'].set(LibrarySortField.ALPHABETICAL);
    page['restoreSort']();
    expect(page['sortField']()).toBe(LibrarySortField.SIZE);
    page['loadData']();
    const expected = jasmine.objectContaining({ sortField: LibrarySortField.SIZE, sortDirection: LibrarySortDirection.DESCENDING });
    expect(radarr.fetchLibrary).toHaveBeenCalledOnceWith(expected);
    expect(sonarr.fetchLibrary).toHaveBeenCalledOnceWith(expected);
  });

  it('reactively sorts mixed media from multiple instances while preserving display titles and direction', () => {
    page['upsertRadarrInstanceTable']({ instanceId: 'r', instanceName: 'Radarr', rows: [movie(1, 'A Quiet Place', null, null)] });
    page['upsertRadarrInstanceTable']({ instanceId: 'r2', instanceName: 'Other Radarr', rows: [movie(2, 'Batman', null, null)] });
    page['upsertSonarrInstanceTable']({ instanceId: 's', instanceName: 'Sonarr', rows: [series(3, 'The Office', null, [])] });
    page['upsertSonarrInstanceTable']({ instanceId: 's2', instanceName: 'Other Sonarr', rows: [series(4, 'Office', null, [])] });
    expect(titles()).toEqual(['A Quiet Place', 'Batman', 'Office', 'The Office']);

    titleSorting.set({ enabled: true, ignoredLeadingWords: ['A', 'The'] });
    expect(titles()).toEqual(['Batman', 'Office', 'The Office', 'A Quiet Place']);
    page['sortDirection'].set(LibrarySortDirection.DESCENDING);
    expect(titles()).toEqual(['A Quiet Place', 'The Office', 'Office', 'Batman']);
    titleSorting.set({ enabled: false, ignoredLeadingWords: ['A', 'The'] });
    expect(titles()).toEqual(['The Office', 'Office', 'Batman', 'A Quiet Place']);
    expect(page['sortField']()).toBe(LibrarySortField.ALPHABETICAL);
    expect(page['sortDirection']()).toBe(LibrarySortDirection.DESCENDING);
    expect(radarr.fetchLibrary).not.toHaveBeenCalled();
    expect(sonarr.fetchLibrary).not.toHaveBeenCalled();
  });

  it('reloads the current table query when persisted rules change and cancels the older response', () => {
    const oldResponse = new Subject<RadarrLibraryBatchResponse>();
    const newResponse = new Subject<RadarrLibraryBatchResponse>();
    radarr.fetchLibrary.and.returnValues(oldResponse, newResponse);
    page['sortField'].set(LibrarySortField.ADDED);
    page['sortDirection'].set(LibrarySortDirection.DESCENDING);
    page['viewMode'].set('table');
    page['loadData']();
    TestBed.tick();

    titleSorting.set({ enabled: true, ignoredLeadingWords: ['The'] });
    TestBed.tick();
    expect(radarr.fetchLibrary).toHaveBeenCalledTimes(2);
    expect(sonarr.fetchLibrary).toHaveBeenCalledTimes(2);
    expect(radarr.fetchLibrary.calls.mostRecent().args[0]).toEqual(
      jasmine.objectContaining({
        sortField: LibrarySortField.ADDED,
        sortDirection: LibrarySortDirection.DESCENDING,
      }),
    );
    newResponse.next({ syncedAt: null, instances: [{ instanceId: 'r', instanceName: 'Radarr', rows: [movie(2, 'Current', null, null)] }] });
    newResponse.complete();
    oldResponse.next({
      syncedAt: null,
      instances: [{ instanceId: 'r', instanceName: 'Radarr', rows: [movie(1, 'Outdated', null, null)] }],
    });
    oldResponse.complete();
    expect(page['radarrInstanceTables']().flatMap((table) => table.movies.map((item) => item.title))).toEqual(['Current']);
    expect(titles()).toEqual(['Current']);
    expect(page['sortField']()).toBe(LibrarySortField.ADDED);
    expect(page['sortDirection']()).toBe(LibrarySortDirection.DESCENDING);
  });

  it('does not reload before initial loading, for equivalent rules, or after destruction', () => {
    titleSorting.set({ enabled: true, ignoredLeadingWords: ['The'] });
    TestBed.tick();
    expect(radarr.fetchLibrary).not.toHaveBeenCalled();
    page['loadData']();
    titleSorting.set({ enabled: true, ignoredLeadingWords: ['The'] });
    TestBed.tick();
    expect(radarr.fetchLibrary).toHaveBeenCalledTimes(1);
    page.ngOnDestroy();
    titleSorting.set({ enabled: false, ignoredLeadingWords: ['The'] });
    TestBed.tick();
    expect(radarr.fetchLibrary).toHaveBeenCalledTimes(1);
  });
});
