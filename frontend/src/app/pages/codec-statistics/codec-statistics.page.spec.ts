import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, Subject, throwError } from 'rxjs';
import { FilterOptionType } from '../../enums/filter-option-type';
import { CodecStatisticsResponse } from '../../interfaces/codec-statistic/codec-statistics-response';
import { FilterOption } from '../../interfaces/filter-option';
import { CodecStatisticsPage } from './codec-statistics.page';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { CodecStatisticsApiService } from '../../services/codec-statistics-api.service';
import { StatisticsChartWidget } from '../../widgets/statistics-chart/statistics-chart.widget';

describe('CodecStatisticsPage', () => {
  let fixture: ComponentFixture<CodecStatisticsPage>;
  let codecStatisticsApiService: jasmine.SpyObj<CodecStatisticsApiService>;
  let appSettingsService: {
    loadInstances: jasmine.Spy;
    loadError: jasmine.Spy;
    sonarrInstances: jasmine.Spy;
    radarrInstances: jasmine.Spy;
  };

  beforeEach(async () => {
    globalThis.localStorage.clear();
    codecStatisticsApiService = jasmine.createSpyObj<CodecStatisticsApiService>('CodecStatisticsApiService', ['fetchCodecStatistics']);

    codecStatisticsApiService.fetchCodecStatistics.and.returnValue(
      of({
        containerFormats: [],
        resolutions: [],
        dynamicRanges: [],
        videoCodecs: [],
        audioCodecs: [],
        videoBitrates: [],
        audioLanguages: [],
        subtitleLanguages: [],
      }),
    );

    appSettingsService = {
      loadInstances: jasmine.createSpy('loadInstances').and.resolveTo(undefined),
      loadError: jasmine.createSpy('loadError').and.returnValue(null),
      sonarrInstances: jasmine
        .createSpy('sonarrInstances')
        .and.returnValue([{ id: 'sonarr-1', name: 'Sonarr 4K', baseUrl: 'http://sonarr', apiKey: 'a' }]),
      radarrInstances: jasmine
        .createSpy('radarrInstances')
        .and.returnValue([{ id: 'radarr-1', name: 'Radarr', baseUrl: 'http://radarr', apiKey: 'b' }]),
    };

    const appI18nService: Pick<AppI18nService, 't'> = {
      t: (key: string) => {
        const translations: Record<string, string> = {
          'codecStats.title': 'Codec statistics',
          'codecStats.description': 'Distribution of codecs across selected instances.',
          'codecStats.empty': 'No codec statistics available for the selected instances.',
          'codecStats.containerFormats': 'Container formats',
          'codecStats.resolutions': 'Resolutions',
          'codecStats.dynamicRanges': 'Dynamic ranges',
          'codecStats.videoCodecs': 'Video codecs',
          'codecStats.audioCodecs': 'Audio codecs',
          'codecStats.copyName': 'Copy name',
          'common.noArrInstances': 'No Sonarr/Radarr instances configured.',
          'common.noData': 'No data available.',
          'common.noDataForFilter': 'No data for filter.',
          'common.tableLoadError': 'Table could not be loaded.',
          'filter.title': 'Filter',
          'filter.sonarrInstances': 'Sonarr instances',
          'filter.radarrInstances': 'Radarr instances',
          'common.options': 'Options',
        };

        return translations[key] ?? key;
      },
    };

    await TestBed.configureTestingModule({
      imports: [CodecStatisticsPage],
      providers: [
        { provide: CodecStatisticsApiService, useValue: codecStatisticsApiService },
        { provide: AppSettingsService, useValue: appSettingsService },
        { provide: AppI18nService, useValue: appI18nService },
        {
          provide: MatSnackBar,
          useValue: jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']),
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CodecStatisticsPage);
  });

  it('should list Radarr filters before Sonarr filters', () => {
    const filterOptions: readonly FilterOption[] = fixture.componentInstance['filterOptions']();

    expect(filterOptions.slice(0, 2).map((filter) => filter.id)).toEqual(['radarr-instance-filter', 'sonarr-instance-filter']);
  });

  it('preserves media selections while refreshing filtered counts and stable options', () => {
    const page = fixture.componentInstance;
    const response = {
      containerFormats: [],
      resolutions: [],
      dynamicRanges: [],
      videoCodecs: [],
      audioCodecs: [],
      videoBitrates: [],
      audioLanguages: [],
      subtitleLanguages: [],
      filterValues: {
        genres: [],
        tags: [],
        statuses: [],
        qualityProfiles: [],
        audioLanguages: ['Japanese'],
        subtitleLanguages: [],
        resolutions: ['720p', '4k', '1080p'],
        videoCodecs: ['HEVC', 'AV1'],
      },
      counts: { 'video-codec-filter': { HEVC: 2, AV1: 1, __all__: 3 } },
    };
    page['stats'].set(response);
    let filters = page['filterOptions']();
    const resolution = filters.find((filter) => filter.id === 'resolution-filter')!;
    expect(resolution.type === FilterOptionType.MULTISELECT && resolution.options.map((option) => option.value)).toEqual([
      '4k',
      '1080p',
      '720p',
    ]);
    const codec = filters.find((filter) => filter.id === 'video-codec-filter')!;
    if (codec.type !== FilterOptionType.MULTISELECT) throw new Error('Expected multiselect');
    codec.selectedValues = ['HEVC'];
    codec.exclude = true;
    const bitrate = filters.find((filter) => filter.id === 'video-bitrate-range')!;
    if (bitrate.type !== FilterOptionType.NUMBER_RANGE) throw new Error('Expected range');
    bitrate.minValue = 2.5;
    bitrate.maxValue = 10;
    expect(page['mediaFilters']()).toEqual(
      jasmine.objectContaining({
        videoCodecs: ['HEVC'],
        excludedFilters: ['video-codec-filter'],
        videoBitrateMin: 2_500_000,
        videoBitrateMax: 10_000_000,
      }),
    );
    page['stats'].set({ ...response, counts: { 'video-codec-filter': { HEVC: 0, AV1: 1, __all__: 1 } } });
    filters = page['filterOptions']();
    const refreshed = filters.find((filter) => filter.id === 'video-codec-filter')!;
    if (refreshed.type !== FilterOptionType.MULTISELECT) throw new Error('Expected multiselect');
    expect(refreshed.selectedValues).toEqual(['HEVC']);
    expect(refreshed.exclude).toBeTrue();
    expect(refreshed.options).toContain(jasmine.objectContaining({ value: 'HEVC', count: 0 }));
    expect(page['mediaFilters']().videoBitrateMin).toBe(2_500_000);
    expect(filters.some((filter) => filter.id === 'aired-filter' || filter.id === 'monitored-filter')).toBeFalse();
  });

  it('loads when only one application has instances', async () => {
    appSettingsService.sonarrInstances.and.returnValue([]);
    fixture.detectChanges();
    await appSettingsService.loadInstances.calls.mostRecent().returnValue;
    await fixture.whenStable();
    expect(codecStatisticsApiService.fetchCodecStatistics).toHaveBeenCalledWith([], ['__all__'], jasmine.any(Object));
  });

  it('shows bitrate and both language charts with the existing filters and expandable rows', async () => {
    const entry = (label: string, percentage = 100, count = 1) => ({ label, count, percentage, totalSizeBytes: 100, mediaFiles: [] });
    codecStatisticsApiService.fetchCodecStatistics.and.returnValue(
      of({
        containerFormats: [entry('MKV')],
        resolutions: [entry('1920x1080')],
        dynamicRanges: [entry('SDR')],
        videoCodecs: [entry('HEVC')],
        audioCodecs: [entry('AAC')],
        videoBitrates: [entry('5–10 Mbit/s')],
        audioLanguages: [entry('German', 20, 5), entry('English', 80)],
        subtitleLanguages: [entry('German')],
      }),
    );
    fixture.detectChanges();
    await appSettingsService.loadInstances.calls.mostRecent().returnValue;
    await fixture.whenStable();
    // The editor popover renders lazily now, so no widget-driven change detection cycle refreshes the view after loading.
    fixture.detectChanges();
    expect(codecStatisticsApiService.fetchCodecStatistics).toHaveBeenCalledOnceWith(
      ['__all__'],
      ['__all__'],
      jasmine.objectContaining({ resolutions: ['__all__'], videoBitrateMin: null }),
    );
    const categories = fixture.componentInstance['chartCategories']();
    expect(categories.map((category) => category.key)).toEqual([
      'containerFormats',
      'resolutions',
      'dynamicRanges',
      'videoCodecs',
      'audioCodecs',
      'videoBitrates',
      'audioLanguages',
      'subtitleLanguages',
    ]);
    expect(categories[6].items.map((item) => item.label)).toEqual(['English', 'German']);
    const element = fixture.nativeElement as HTMLElement;
    const charts = element.querySelectorAll('app-statistics-chart');
    expect(charts).toHaveSize(8);
    expect(element.querySelector('.chart-row-button')).toBeNull();
    charts[5].querySelector('.pie-segment')!.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await fixture.whenStable();
    expect(charts[5].querySelector('.chart-row-button')!.textContent).toContain('5–10 Mbit/s');
    expect(charts[5].querySelector('.chart-row-button.expanded')).toBeTruthy();
    expect(charts[6].querySelector('.chart-row-button')).toBeNull();
  });

  it('shows grouped resolutions in the chart while retaining individual container formats', async () => {
    const entry = (label: string) => ({ label, count: 1, percentage: 50, totalSizeBytes: 100, mediaFiles: [] });
    codecStatisticsApiService.fetchCodecStatistics.and.returnValue(
      of({
        containerFormats: [entry('MKV'), entry('MP4')],
        resolutions: [entry('1920x1080'), entry('1920x800')],
        dynamicRanges: [],
        videoCodecs: [],
        audioCodecs: [],
        videoBitrates: [],
        audioLanguages: [],
        subtitleLanguages: [],
      }),
    );
    fixture.detectChanges();
    await appSettingsService.loadInstances.calls.mostRecent().returnValue;
    await fixture.whenStable();
    const categories = fixture.componentInstance['chartCategories']();
    expect(categories.find((category) => category.key === 'resolutions')!.items).toEqual([
      jasmine.objectContaining({ label: '1080p', count: 2, percentage: 100, totalSizeBytes: 200 }),
    ]);
    expect(categories.find((category) => category.key === 'containerFormats')!.items.map((item) => item.label)).toEqual(['MKV', 'MP4']);
  });

  it('keeps smaller container format segments within the rendered circle after two dominant values', async () => {
    const items = fixture.componentInstance['toSlices'](
      [91, 5.4, 3.5, 0.04, 0.01].map((percentage, index) => ({
        label: `Format ${index}`,
        percentage,
        count: 1,
        totalSizeBytes: 1,
        mediaFiles: [],
      })),
    );
    const chart = TestBed.createComponent(StatisticsChartWidget);
    chart.componentRef.setInput('category', { key: 'containerFormats', title: 'Container formats', items });
    await chart.whenStable();
    const segments = (chart.nativeElement as HTMLElement).querySelectorAll<SVGCircleElement>('.pie-segment');
    expect(segments).toHaveSize(5);
    const circumference = 2 * Math.PI * segments[0].r.baseVal.value;
    for (const [index, segment] of Array.from(segments).entries()) {
      const length = Number.parseFloat(segment.style.strokeDasharray);
      const start = -Number.parseFloat(segment.style.strokeDashoffset);
      expect((length / circumference) * 100)
        .withContext(items[index].label)
        .toBeCloseTo(items[index].percentage, 2);
      expect(start + length)
        .withContext(items[index].label)
        .toBeLessThanOrEqual(circumference + 0.01);
    }
  });

  it('ignores an older success arriving after the latest filter response', () => {
    const older = new Subject<CodecStatisticsResponse>();
    const latest = new Subject<CodecStatisticsResponse>();
    codecStatisticsApiService.fetchCodecStatistics.and.returnValues(older, latest);
    const page = fixture.componentInstance;
    page['loadStats']();
    page['loadStats']();
    const response: CodecStatisticsResponse = {
      containerFormats: [],
      resolutions: [],
      dynamicRanges: [],
      videoCodecs: [],
      audioCodecs: [],
      videoBitrates: [],
      audioLanguages: [],
      subtitleLanguages: [],
      counts: { 'video-codec-filter': { HEVC: 1, __all__: 1 } },
    };
    latest.next(response);
    latest.complete();
    older.next({ ...response, counts: { 'video-codec-filter': { AV1: 99, __all__: 99 } } });
    older.complete();
    expect(page['stats']()).toBe(response);
    expect(page['loading']()).toBeFalse();
    expect(page['loadError']()).toBeNull();
  });

  it('ignores an obsolete request error while the latest request is still loading', () => {
    const older = new Subject<CodecStatisticsResponse>();
    const latest = new Subject<CodecStatisticsResponse>();
    codecStatisticsApiService.fetchCodecStatistics.and.returnValues(older, latest);
    const page = fixture.componentInstance;
    page['loadStats']();
    page['loadStats']();
    older.error(new Error('Obsolete error'));
    expect(page['loadError']()).toBeNull();
    expect(page['loading']()).toBeTrue();
    const response: CodecStatisticsResponse = {
      containerFormats: [],
      resolutions: [],
      dynamicRanges: [],
      videoCodecs: [],
      audioCodecs: [],
      videoBitrates: [],
      audioLanguages: [],
      subtitleLanguages: [],
    };
    latest.next(response);
    latest.complete();
    expect(page['stats']()).toBe(response);
    expect(page['loading']()).toBeFalse();
  });

  it('should render a settings load error when settings failed to load', async () => {
    appSettingsService.loadError.and.returnValue('Settings could not be loaded.');

    fixture.detectChanges();
    await appSettingsService.loadInstances.calls.mostRecent().returnValue;
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.loading-error-state-message')?.textContent).toContain('Settings could not be loaded.');
    expect(compiled.querySelector('app-page-filter')).toBeNull();
  });

  it('should render no data when the API returns empty statistics', async () => {
    fixture.detectChanges();
    await appSettingsService.loadInstances.calls.mostRecent().returnValue;
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.no-data-state')?.textContent).toContain('No data available.');
    expect(compiled.querySelectorAll('app-statistics-chart')).toHaveSize(0);
  });

  it('should render a load error when the statistics request fails', async () => {
    codecStatisticsApiService.fetchCodecStatistics.and.returnValue(throwError(() => new Error('Codec request failed')));

    fixture.detectChanges();
    await appSettingsService.loadInstances.calls.mostRecent().returnValue;
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.loading-error-state-message')?.textContent).toContain('Table could not be loaded.');
  });
  it('builds each file tree only on first access and keeps the built tree', () => {
    const page = fixture.componentInstance;
    const buildTree = spyOn(page as unknown as { buildTree: CodecStatisticsPage['buildTree'] }, 'buildTree').and.callThrough();
    const slices = page['toSlices']([
      {
        label: 'MKV',
        count: 1,
        percentage: 100,
        totalSizeBytes: 5,
        mediaFiles: [
          {
            title: 'Movie',
            titleSlug: null,
            source: 'Radarr (Radarr)',
            instanceName: 'Radarr',
            rootTitle: 'Movie',
            relativePath: 'Movie/file.mkv',
            sizeBytes: 5,
          },
        ],
      },
    ]);
    expect(buildTree).not.toHaveBeenCalled();
    const roots = slices[0].treeRoots;
    expect(roots[0].fileCount).toBe(1);
    expect(roots[0].totalSizeBytes).toBe(5);
    expect(slices[0].treeRoots).toBe(roots);
    expect(buildTree).toHaveBeenCalledTimes(1);
  });

  it('cancels superseded requests and cancels the active request on destruction', () => {
    const older = new Subject<CodecStatisticsResponse>();
    const latest = new Subject<CodecStatisticsResponse>();
    codecStatisticsApiService.fetchCodecStatistics.and.returnValues(older, latest);
    fixture.componentInstance['loadStats']();
    expect(older.observed).toBeTrue();
    fixture.componentInstance['loadStats']();
    expect(older.observed).toBeFalse();
    expect(latest.observed).toBeTrue();
    fixture.destroy();
    expect(latest.observed).toBeFalse();
  });

  it('debounces range input and discards pending input when destroyed', () => {
    jasmine.clock().install();
    try {
      const page = fixture.componentInstance;
      page['scheduleLoadStats'](true);
      jasmine.clock().tick(100);
      page['scheduleLoadStats'](true);
      jasmine.clock().tick(199);
      expect(codecStatisticsApiService.fetchCodecStatistics).not.toHaveBeenCalled();
      jasmine.clock().tick(1);
      expect(codecStatisticsApiService.fetchCodecStatistics).toHaveBeenCalledTimes(1);
      page['scheduleLoadStats'](true);
      fixture.destroy();
      jasmine.clock().tick(200);
      expect(codecStatisticsApiService.fetchCodecStatistics).toHaveBeenCalledTimes(1);
    } finally {
      jasmine.clock().uninstall();
    }
  });
});
