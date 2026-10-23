import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal, WritableSignal } from '@angular/core';
import { FilterOptionType } from '../../enums/filter-option-type';
import { LibraryChangeHistoryEntry } from '../../interfaces/library-trend/library-change-history-entry';
import { By } from '@angular/platform-browser';
import { PageFilterWidget } from '../../widgets/page-filter/page-filter.widget';
import { of } from 'rxjs';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { LibraryTrendStatisticsApiService } from '../../services/library-trend-statistics-api.service';
import { LibraryTrendPage } from './library-trend.page';

describe('LibraryTrendPage', () => {
  let fixture: ComponentFixture<LibraryTrendPage>;
  let apiService: jasmine.SpyObj<LibraryTrendStatisticsApiService>;
  let sonarrInstancesSignal: WritableSignal<{ id: string; name: string; baseUrl: string; apiKey: string }[]>;
  let radarrInstancesSignal: WritableSignal<{ id: string; name: string; baseUrl: string; apiKey: string }[]>;

  beforeEach(async () => {
    globalThis.localStorage.clear();
    apiService = jasmine.createSpyObj<LibraryTrendStatisticsApiService>('LibraryTrendStatisticsApiService', [
      'fetchLibraryTrendStatistics',
    ]);

    sonarrInstancesSignal = signal([{ id: 'sonarr-1', name: 'Sonarr Main', baseUrl: 'http://sonarr', apiKey: 'a' }]);
    radarrInstancesSignal = signal([{ id: 'radarr-1', name: 'Radarr Main', baseUrl: 'http://radarr', apiKey: 'b' }]);

    const appSettingsService: Pick<AppSettingsService, 'loadInstances' | 'localeId' | 'sonarrInstances' | 'radarrInstances'> = {
      loadInstances: jasmine.createSpy('loadInstances').and.resolveTo(undefined),
      localeId: jasmine.createSpy('localeId').and.returnValue('en-US'),
      sonarrInstances: sonarrInstancesSignal,
      radarrInstances: radarrInstancesSignal,
    };

    const appI18nService: Pick<AppI18nService, 't'> = {
      t: (key: string) => {
        const translations: Record<string, string> = {
          'libraryTrend.title': 'Library Trend',
          'libraryTrend.description': 'Tracks library changes over time.',
          'libraryTrend.empty': 'No library history available.',
          'libraryTrend.filter.range': 'Date range',
          'libraryTrend.mediaFiles': 'media files',
          'libraryTrend.storage': 'storage',
          'libraryTrend.change': 'Changes',
          'libraryTrend.noChange': 'No change',
          'libraryTrend.mediaChart': 'Media changes',
          'libraryTrend.storageChart': 'Storage usage',
          'libraryTrend.history': 'History',
          'libraryTrend.instance': 'Instance',
          'libraryTrend.time': 'Time',
          'libraryTrend.added': 'Added',
          'libraryTrend.changed': 'Changed',
          'libraryTrend.deleted': 'Deleted',
          'libraryTrend.totalMediaFiles': 'Total media files',
          'libraryTrend.totalStorage': 'Total storage',
          'common.noArrInstances': 'No Sonarr/Radarr instances configured.',
          'filter.title': 'Filter',
          'filter.sonarrInstances': 'Sonarr instances',
          'filter.radarrInstances': 'Radarr instances',
          'common.loading': 'Loading data...',
          'common.tableLoadError': 'Data could not be loaded.',
        };

        return translations[key] ?? key;
      },
    };

    apiService.fetchLibraryTrendStatistics.and.returnValue(
      of({
        availableFrom: '2026-04-15T00:00:00.000Z',
        availableTo: '2026-05-02T10:00:00.000Z',
        totalMediaFileCount: 42,
        totalSizeBytes: 1400,
        points: [
          {
            occurredAt: '2026-05-01T10:00:00.000Z',
            mediaFileCount: 10,
            totalSizeBytes: 1000,
            deltaMediaFileCount: 10,
            deltaSizeBytes: 1000,
          },
          {
            occurredAt: '2026-05-02T10:00:00.000Z',
            mediaFileCount: 12,
            totalSizeBytes: 1400,
            deltaMediaFileCount: 2,
            deltaSizeBytes: 400,
          },
        ],
        history: [
          {
            sourceKey: 'sonarr:1',
            kind: 'Sonarr',
            instanceId: 'sonarr-1',
            instanceName: 'Sonarr Main',
            occurredAt: '2026-05-01T10:00:00.000Z',
            changeType: 'Added',
            entityTitle: 'Episode 1',
            seriesTitle: 'Sample Series',
            filePath: '/media/episode-1.mkv',
            sizeBytes: 1000,
            deltaSizeBytes: 1000,
          },
        ],
      }),
    );

    await TestBed.configureTestingModule({
      imports: [LibraryTrendPage],
      providers: [
        { provide: LibraryTrendStatisticsApiService, useValue: apiService },
        { provide: AppSettingsService, useValue: appSettingsService },
        { provide: AppI18nService, useValue: appI18nService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LibraryTrendPage);
  });

  it('should load library history statistics with instance filters and date range', async () => {
    fixture.detectChanges();
    await TestBed.inject(AppSettingsService).loadInstances();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(apiService.fetchLibraryTrendStatistics).toHaveBeenCalled();
    const call: jasmine.CallInfo<typeof apiService.fetchLibraryTrendStatistics> = apiService.fetchLibraryTrendStatistics.calls.mostRecent();
    expect(call.args[0]).toEqual(['sonarr-1']);
    expect(call.args[1]).toEqual(['radarr-1']);
    const selectedStartDate: Date = fixture.componentInstance['selectedStartDate']();
    const selectedEndDate: Date = fixture.componentInstance['selectedEndDate']();
    const dayDiff: number = Math.round((selectedEndDate.getTime() - selectedStartDate.getTime()) / (24 * 60 * 60 * 1000));
    expect(dayDiff).toBeGreaterThanOrEqual(28);
    expect(dayDiff).toBeLessThanOrEqual(32);
    expect(call.args[2]).toBe(selectedStartDate.toISOString());
    expect(call.args[3]).toBe(selectedEndDate.toISOString());

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Library Trend');
    const overviewCards: NodeListOf<Element> = compiled.querySelectorAll('.trend-overview-grid .card');
    expect(overviewCards).toHaveSize(2);
    expect(overviewCards[0].textContent).toContain('Total media files');
    expect(overviewCards[0].textContent).toContain('42');
    expect(overviewCards[1].textContent).toContain('Total storage');
    expect(overviewCards[1].textContent).toContain('1.4 KB');
    expect(compiled.querySelectorAll('svg')).toHaveSize(2);
    expect(fixture.componentInstance['historyEntries']().length).toBeGreaterThan(0);
  });

  it('keeps instance exclusions when changing the date range', async () => {
    radarrInstancesSignal.set([
      { id: 'radarr-1', name: 'Radarr Main', baseUrl: 'http://radarr', apiKey: 'b' },
      { id: 'radarr-2', name: 'Radarr Second', baseUrl: 'http://radarr2', apiKey: 'c' },
    ]);
    fixture.detectChanges();
    await TestBed.inject(AppSettingsService).loadInstances();
    await fixture.whenStable();
    const page = fixture.componentInstance;
    const instance = page['filterOptions']().find((filter) => filter.id === 'radarr-instance-filter')!;
    if (instance.type !== FilterOptionType.MULTISELECT) throw new Error('Expected multiselect');
    const widget = fixture.debugElement.query(By.directive(PageFilterWidget)).componentInstance as PageFilterWidget;
    widget['onMultiSelectChanged'](instance, ['radarr-1']);
    widget['onPolarityChanged'](instance, true);
    const start = new Date('2026-04-01T00:00:00Z');
    const end = new Date('2026-05-01T00:00:00Z');
    page['updateSelectedDateRange']({ startDate: start, endDate: end });
    await fixture.whenStable();
    expect(apiService.fetchLibraryTrendStatistics.calls.mostRecent().args).toEqual([
      ['sonarr-1'],
      ['radarr-2'],
      start.toISOString(),
      end.toISOString(),
    ]);
    const restored = page['filterOptions']().find((filter) => filter.id === 'radarr-instance-filter')!;
    expect(restored.exclude).toBeTrue();
    expect(restored.type === FilterOptionType.MULTISELECT && restored.selectedValues).toEqual(['radarr-1']);
  });

  it('loads with only one instance type and treats an empty selection as all', async () => {
    sonarrInstancesSignal.set([]);
    fixture.detectChanges();
    await TestBed.inject(AppSettingsService).loadInstances();
    await fixture.whenStable();
    const call = apiService.fetchLibraryTrendStatistics.calls.mostRecent();
    expect(call.args[0]).toEqual([]);
    expect(call.args[1]).toEqual(['radarr-1']);
    expect(fixture.componentInstance['filterOptions']().map((filter) => filter.id)).toEqual(['radarr-instance-filter', 'date-range']);
  });

  it('should build chart models from returned points', async () => {
    fixture.detectChanges();
    await TestBed.inject(AppSettingsService).loadInstances();
    await fixture.whenStable();
    fixture.detectChanges();

    const mediaModel: ReturnType<LibraryTrendPage['mediaChartModel']> = fixture.componentInstance['mediaChartModel']();
    const storageModel: ReturnType<LibraryTrendPage['storageChartModel']> = fixture.componentInstance['storageChartModel']();
    expect(mediaModel).not.toBeNull();
    expect(storageModel).not.toBeNull();
    expect(mediaModel?.points).toHaveSize(2);
    expect(storageModel?.points).toHaveSize(2);
    expect(mediaModel?.points[0].tooltipLines[1]).toContain('10');
    expect(storageModel?.points[1].tooltipLines[1]).toContain('1.4 KB');
    expect(fixture.nativeElement.querySelector('cdk-virtual-scroll-viewport')).toBeTruthy();
  });

  function buildHistoryEntry(occurredAt: string): LibraryChangeHistoryEntry {
    return {
      sourceKey: `sonarr:${occurredAt}`,
      kind: 'Sonarr',
      instanceId: 'sonarr-1',
      instanceName: 'Sonarr Main',
      occurredAt,
      changeType: 'Added',
      entityTitle: `Episode ${occurredAt}`,
      seriesTitle: null,
      filePath: '/media/episode.mkv',
      sizeBytes: 1000,
      deltaSizeBytes: 1000,
    };
  }

  async function loadPageWithHistory(history: LibraryChangeHistoryEntry[]): Promise<LibraryTrendPage> {
    fixture.detectChanges();
    await TestBed.inject(AppSettingsService).loadInstances();
    await fixture.whenStable();
    const page: LibraryTrendPage = fixture.componentInstance;
    const current = fixture.componentInstance['stats']()!;
    page['stats'].set({ ...current, history });
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve));
    const viewport = page['historyViewport']();
    viewport?.checkViewportSize();
    viewport?.scrollToIndex(0);
    await fixture.whenStable();
    fixture.detectChanges();
    return page;
  }

  it('highlights every history entry of the clicked day and scrolls the first one into the center', async () => {
    const entries: LibraryChangeHistoryEntry[] = [
      buildHistoryEntry('2026-05-03T12:00:00.000Z'),
      buildHistoryEntry('2026-05-02T14:00:00.000Z'),
      buildHistoryEntry('2026-05-02T12:00:00.000Z'),
      buildHistoryEntry('2026-05-01T12:00:00.000Z'),
    ];
    const page: LibraryTrendPage = await loadPageWithHistory(entries);

    const viewport = page['historyViewport']();
    expect(viewport).toBeTruthy();
    const scrollToSpy: jasmine.Spy = spyOn(viewport!, 'scrollTo');

    page['onChartPointSelected']('2026-05-02T12:00:00.000Z');

    const highlighted: ReadonlySet<LibraryChangeHistoryEntry> = page['highlightedHistoryEntries']();
    expect(highlighted.has(entries[1])).toBeTrue();
    expect(highlighted.has(entries[2])).toBeTrue();
    expect(highlighted.has(entries[0])).toBeFalse();
    expect(highlighted.has(entries[3])).toBeFalse();
    expect(scrollToSpy).toHaveBeenCalledTimes(1);
    const options: ScrollToOptions = scrollToSpy.calls.mostRecent().args[0];
    const expectedTop: number = Math.max(0, 1 * 29 - (viewport!.getElementRef().nativeElement.clientHeight - 29) / 2);
    expect(options.top).toBe(expectedTop);
    expect(options.behavior).toBe('smooth');
  });

  it('scrolls to the next following entry without highlighting when the clicked day has no history entries', async () => {
    const entries: LibraryChangeHistoryEntry[] = [
      buildHistoryEntry('2026-05-03T12:00:00.000Z'),
      buildHistoryEntry('2026-05-01T12:00:00.000Z'),
    ];
    const page: LibraryTrendPage = await loadPageWithHistory(entries);
    const viewport = page['historyViewport']();
    expect(viewport).toBeTruthy();
    const scrollToSpy: jasmine.Spy = spyOn(viewport!, 'scrollTo');

    page['onChartPointSelected']('2026-05-04T12:00:00.000Z');

    expect(page['highlightedHistoryEntries']().size).toBe(0);
    expect(scrollToSpy).toHaveBeenCalledTimes(1);
  });

  it('clears the highlighted history entries when new statistics are loaded', async () => {
    const entries: LibraryChangeHistoryEntry[] = [buildHistoryEntry('2026-05-02T12:00:00.000Z')];
    const page: LibraryTrendPage = await loadPageWithHistory(entries);
    spyOn(page['historyViewport']()!, 'scrollTo');

    page['onChartPointSelected']('2026-05-02T12:00:00.000Z');
    expect(page['highlightedHistoryEntries']().has(entries[0])).toBeTrue();

    page['loadStats']();
    await fixture.whenStable();

    expect(page['highlightedHistoryEntries']().size).toBe(0);
  });
});
