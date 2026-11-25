import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { MockInstance } from 'vitest';
import { signal, WritableSignal } from '@angular/core';
import { FilterOptionType } from '../../enums/filter-option-type';
import { LibraryChangeHistoryEntry } from '../../interfaces/library-trend/library-change-history-entry';
import { LibraryTrendStatisticsResponse } from '../../interfaces/library-trend/library-trend-statistics-response';
import { By } from '@angular/platform-browser';
import { PageFilterWidget } from '../../widgets/page-filter/page-filter.widget';
import { of } from 'rxjs';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { LibraryTrendStatisticsApiService } from '../../services/library-trend-statistics-api.service';
import { LibraryTrendPage } from './library-trend.page';

describe('LibraryTrendPage', () => {
  let fixture: ComponentFixture<LibraryTrendPage>;
  let apiService: Record<'fetchLibraryTrendStatistics', MockInstance>;
  let statisticsResponse: LibraryTrendStatisticsResponse;
  let sonarrInstancesSignal: WritableSignal<{ id: string; name: string; baseUrl: string; apiKey: string }[]>;
  let radarrInstancesSignal: WritableSignal<{ id: string; name: string; baseUrl: string; apiKey: string }[]>;

  beforeEach(async () => {
    globalThis.localStorage.clear();
    apiService = {
      fetchLibraryTrendStatistics: vi.fn<LibraryTrendStatisticsApiService['fetchLibraryTrendStatistics']>(),
    };

    sonarrInstancesSignal = signal([{ id: 'sonarr-1', name: 'Sonarr Main', baseUrl: 'http://sonarr', apiKey: 'a' }]);
    radarrInstancesSignal = signal([{ id: 'radarr-1', name: 'Radarr Main', baseUrl: 'http://radarr', apiKey: 'b' }]);

    const appSettingsService: Pick<AppSettingsService, 'loadInstances' | 'localeId' | 'sonarrInstances' | 'radarrInstances'> = {
      loadInstances: vi.fn().mockResolvedValue(undefined),
      localeId: vi.fn().mockReturnValue('en-US'),
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
          'libraryTrend.unknownSize': 'Unknown',
          'libraryTrend.inventoryReconciliation': 'Inventory reconciliation',
          'libraryTrend.inventoryDetails': 'Individual changes can no longer be reconstructed',
          'libraryTrend.observedChange': 'Detected during inventory check',
          'libraryTrend.mediaChart': 'Media changes',
          'libraryTrend.storageChart': 'Storage usage',
          'libraryTrend.history': 'History',
          'libraryTrend.instance': 'Instance',
          'libraryTrend.time': 'Time',
          'libraryTrend.added': 'Added',
          'libraryTrend.changed': 'Changed',
          'libraryTrend.deleted': 'Deleted',
          'libraryTrend.updated': 'Updated',
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

    statisticsResponse = {
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
    };
    apiService.fetchLibraryTrendStatistics.mockReturnValue(of(statisticsResponse));

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
    const call = apiService.fetchLibraryTrendStatistics.mock.lastCall!;
    expect(call[0]).toEqual(['sonarr-1']);
    expect(call[1]).toEqual(['radarr-1']);
    const selectedStartDate: Date = fixture.componentInstance['selectedStartDate']();
    const selectedEndDate: Date = fixture.componentInstance['selectedEndDate']();
    const dayDiff: number = Math.round((selectedEndDate.getTime() - selectedStartDate.getTime()) / (24 * 60 * 60 * 1000));
    expect(dayDiff).toBeGreaterThanOrEqual(28);
    expect(dayDiff).toBeLessThanOrEqual(32);
    expect(call[2]).toBe(selectedStartDate.toISOString());
    expect(call[3]).toBe(selectedEndDate.toISOString());

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Library Trend');
    const overviewCards: NodeListOf<Element> = compiled.querySelectorAll('.trend-overview-grid .card');
    expect(overviewCards).toHaveLength(2);
    expect(overviewCards[0].textContent).toContain('Total media files');
    expect(overviewCards[0].textContent).toContain('42');
    expect(overviewCards[1].textContent).toContain('Total storage');
    expect(overviewCards[1].textContent).toContain('1.4 KB');
    expect(compiled.querySelectorAll('svg')).toHaveLength(2);
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
    expect(apiService.fetchLibraryTrendStatistics.mock.lastCall).toEqual([
      ['sonarr-1'],
      ['radarr-2'],
      start.toISOString(),
      end.toISOString(),
    ]);
    const restored = page['filterOptions']().find((filter) => filter.id === 'radarr-instance-filter')!;
    expect(restored.exclude).toBe(true);
    expect(restored.type === FilterOptionType.MULTISELECT && restored.selectedValues).toEqual(['radarr-1']);
  });

  it('loads with only one instance type and treats an empty selection as all', async () => {
    sonarrInstancesSignal.set([]);
    fixture.detectChanges();
    await TestBed.inject(AppSettingsService).loadInstances();
    await fixture.whenStable();
    const call = apiService.fetchLibraryTrendStatistics.mock.lastCall!;
    expect(call[0]).toEqual([]);
    expect(call[1]).toEqual(['radarr-1']);
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
    expect(mediaModel?.points).toHaveLength(2);
    expect(storageModel?.points).toHaveLength(2);
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
    apiService.fetchLibraryTrendStatistics.mockReturnValue(of({ ...statisticsResponse, history }));
    fixture.detectChanges();
    await TestBed.inject(AppSettingsService).loadInstances();
    await fixture.whenStable();
    fixture.detectChanges();
    const page: LibraryTrendPage = fixture.componentInstance;
    const viewport = page['historyViewport']();
    if (viewport) {
      // Give the virtual table a real viewport even in the headless test browser.
      viewport.getElementRef().nativeElement.style.height = '240px';
      viewport.checkViewportSize();
      viewport.scrollToIndex(0);
    }
    // CDK measures the viewport on one frame and forwards its range to MatTable on the next.
    await new Promise<void>((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => resolve())));
    await fixture.whenStable();
    fixture.detectChanges();
    return page;
  }

  it('shows an unknown update size separately from an unchanged size and a reduction', async () => {
    const unknown = {
      ...buildHistoryEntry('2026-05-02T12:00:00.000Z'),
      changeType: 'Updated' as const,
      deltaSizeBytes: 0,
      isSizeDeltaKnown: false,
    };
    const unchanged = {
      ...buildHistoryEntry('2026-05-02T11:00:00.000Z'),
      changeType: 'Updated' as const,
      deltaSizeBytes: 0,
      isSizeDeltaKnown: true,
    };
    const smaller = {
      ...buildHistoryEntry('2026-05-02T10:00:00.000Z'),
      changeType: 'Updated' as const,
      deltaSizeBytes: -1024,
      isSizeDeltaKnown: true,
    };
    await loadPageWithHistory([unknown, unchanged, smaller]);
    const sizes = Array.from(fixture.nativeElement.querySelectorAll('td.mat-column-deltaSizeBytes')) as HTMLElement[];
    expect(sizes.map((cell) => cell.textContent?.trim())).toEqual(['Unknown', 'No change', '-1.0 KB']);
  });

  it('explains inferred deletions and distinguishes inventory reconciliation from a named deletion', async () => {
    const deleted = {
      ...buildHistoryEntry('2026-05-02T12:00:00.000Z'),
      changeType: 'Deleted' as const,
      isInferred: true,
      entityTitle: 'Missing episode',
      seriesTitle: 'Removed series',
      deltaSizeBytes: -1024,
    };
    const reconciled = {
      ...buildHistoryEntry('2026-05-02T11:00:00.000Z'),
      changeType: 'Changed' as const,
      isInferred: true,
      eventType: 'libraryInventoryReconciled',
      deltaSizeBytes: -2048,
    };
    await loadPageWithHistory([deleted, reconciled]);
    const rows = Array.from(fixture.nativeElement.querySelectorAll('tr.mat-mdc-row')) as HTMLElement[];
    expect(rows[0].textContent).toContain('Deleted');
    expect(rows[0].textContent).toContain('Missing episode');
    expect(rows[0].textContent).toContain('Removed series');
    expect(rows[0].textContent).toContain('Detected during inventory check');
    expect(rows[1].textContent).toContain('Inventory reconciliation');
    expect(rows[1].textContent).toContain('Individual changes can no longer be reconstructed');
    expect(rows[1].textContent).toContain('-2.0 KB');
  });

  it('shows an unknown storage delta in the chart tooltip', async () => {
    await loadPageWithHistory([]);
    const page = fixture.componentInstance;
    const point = {
      occurredAt: '2026-05-02T12:00:00.000Z',
      mediaFileCount: 1,
      totalSizeBytes: 100,
      deltaMediaFileCount: 0,
      deltaSizeBytes: 0,
      isSizeDeltaKnown: false,
    };
    expect(page['buildTooltipLines'](point, 'storage').at(-1)).toBe('Unknown');
    expect(page['buildTooltipLines'](point, 'media').at(-1)).toBe('No change');
  });

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
    const scrollToSpy: MockInstance = vi.spyOn(viewport!, 'scrollTo');

    page['onChartPointSelected']('2026-05-02T12:00:00.000Z');

    const highlighted: ReadonlySet<LibraryChangeHistoryEntry> = page['highlightedHistoryEntries']();
    expect(highlighted.has(entries[1])).toBe(true);
    expect(highlighted.has(entries[2])).toBe(true);
    expect(highlighted.has(entries[0])).toBe(false);
    expect(highlighted.has(entries[3])).toBe(false);
    expect(scrollToSpy).toHaveBeenCalledTimes(1);
    const options: ScrollToOptions = scrollToSpy.mock.lastCall![0];
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
    const scrollToSpy: MockInstance = vi.spyOn(viewport!, 'scrollTo');

    page['onChartPointSelected']('2026-05-04T12:00:00.000Z');

    expect(page['highlightedHistoryEntries']().size).toBe(0);
    expect(scrollToSpy).toHaveBeenCalledTimes(1);
  });

  it('clears the highlighted history entries when new statistics are loaded', async () => {
    const entries: LibraryChangeHistoryEntry[] = [buildHistoryEntry('2026-05-02T12:00:00.000Z')];
    const page: LibraryTrendPage = await loadPageWithHistory(entries);
    vi.spyOn(page['historyViewport']()!, 'scrollTo');

    page['onChartPointSelected']('2026-05-02T12:00:00.000Z');
    expect(page['highlightedHistoryEntries']().has(entries[0])).toBe(true);

    page['loadStats']();
    await fixture.whenStable();

    expect(page['highlightedHistoryEntries']().size).toBe(0);
  });
});
