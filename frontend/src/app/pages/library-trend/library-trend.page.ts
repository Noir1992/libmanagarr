import { CdkVirtualScrollViewport, ScrollingModule } from '@angular/cdk/scrolling';
import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  linkedSignal,
  OnInit,
  signal,
  Signal,
  viewChild,
  WritableSignal,
} from '@angular/core';
import { MatTableModule } from '@angular/material/table';
import { DEFAULT_FILTER_ALL, FilterMultiSelectOption, FilterOption } from '../../interfaces/filter-option';
import { FilterOptionType } from '../../enums/filter-option-type';
import { LibraryChangeHistoryEntry } from '../../interfaces/library-trend/library-change-history-entry';
import { LibraryTrendPoint } from '../../interfaces/library-trend/library-trend-point';
import { LibraryTrendStatisticsResponse } from '../../interfaces/library-trend/library-trend-statistics-response';
import { ArrInstanceSettings } from '../../interfaces/settings/arr-instance-settings';
import { FormatBytesPipe } from '../../pipes/format-bytes.pipe';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { LibraryTrendStatisticsApiService } from '../../services/library-trend-statistics-api.service';
import { TrendChartLabel, TrendChartModel, TrendChartPoint, TrendChartSeries, TrendChartTick } from '../../types/trend-chart/trend-chart';
import { buildDefaultHistoryDateRange } from '../../utils/library-trend-date-range-utils';
import { multiSelectInstanceFilterValues } from '../../utils/filter-utils';
import { PageFilterWidget } from '../../widgets/page-filter/page-filter.widget';
import { LoadingErrorSectionWidget } from '../../widgets/sections/loading-error-section/loading-error-section.widget';
import { LoadingSectionWidget } from '../../widgets/sections/loading-section/loading-section.widget';
import { NoArrInstancesConfiguredSectionWidget } from '../../widgets/sections/no-arr-instances-configured-section/no-arr-instances-configured-section.widget';
import { NoDataSectionWidget } from '../../widgets/sections/no-data-section/no-data-section.widget';
import { TrendChartWidget } from '../../widgets/trend-chart/trend-chart.widget';

const CHART_WIDTH: number = 960;
const CHART_HEIGHT: number = 360;
const HISTORY_ITEM_SIZE: number = 29;
const CHART_PADDING: Readonly<{ top: number; right: number; bottom: number; left: number }> = {
  top: 24,
  right: 24,
  bottom: 56,
  left: 72,
};

@Component({
  selector: 'app-library-trend-page',
  imports: [
    DatePipe,
    LoadingErrorSectionWidget,
    LoadingSectionWidget,
    NoArrInstancesConfiguredSectionWidget,
    NoDataSectionWidget,
    MatTableModule,
    TrendChartWidget,
    PageFilterWidget,
    ScrollingModule,
  ],
  templateUrl: './library-trend.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './library-trend.page.scss',
})
export class LibraryTrendPage implements OnInit {
  /* Injections */
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);
  private readonly libraryTrendApi: LibraryTrendStatisticsApiService = inject(LibraryTrendStatisticsApiService);
  private readonly i18n: AppI18nService = inject(AppI18nService);

  /* Constants */
  protected readonly formatBytesPipe: FormatBytesPipe = new FormatBytesPipe();
  protected readonly historyColumns: readonly string[] = ['occurredAt', 'instanceName', 'changeType', 'entityTitle', 'deltaSizeBytes'];
  protected readonly chartAccentColor: string = 'var(--theme-accent)';
  protected readonly chartPositiveColor: string = 'var(--theme-success-color)';
  protected readonly chartNegativeColor: string = 'var(--theme-danger-color)';
  protected readonly defaultHistoryDateRange: ReturnType<typeof buildDefaultHistoryDateRange> = buildDefaultHistoryDateRange();
  protected readonly formatBytes: (value: number) => string = (value: number): string => this.formatBytesPipe.transform(value);
  protected readonly absolute: (x: number) => number = Math.abs;

  /* I18n signals */
  protected readonly title: Signal<string> = computed((): string => this.i18n.t('libraryTrend.title'));
  protected readonly description: Signal<string> = computed((): string => this.i18n.t('libraryTrend.description'));
  protected readonly emptyStateText: Signal<string> = computed((): string => this.i18n.t('libraryTrend.empty'));
  protected readonly dateRangeLabel: Signal<string> = computed((): string => this.i18n.t('libraryTrend.filter.range'));
  protected readonly mediaChartLabel: Signal<string> = computed((): string => this.i18n.t('libraryTrend.mediaChart'));
  protected readonly storageChartLabel: Signal<string> = computed((): string => this.i18n.t('libraryTrend.storageChart'));
  protected readonly historyLabel: Signal<string> = computed((): string => this.i18n.t('libraryTrend.history'));
  protected readonly instanceLabel: Signal<string> = computed((): string => this.i18n.t('libraryTrend.instance'));
  protected readonly timeLabel: Signal<string> = computed((): string => this.i18n.t('libraryTrend.time'));
  protected readonly changeLabel: Signal<string> = computed((): string => this.i18n.t('libraryTrend.change'));
  protected readonly mediaFilesLabel: Signal<string> = computed((): string => this.i18n.t('libraryTrend.mediaFiles'));
  protected readonly storageLabel: Signal<string> = computed((): string => this.i18n.t('libraryTrend.storage'));
  protected readonly totalMediaFilesLabel: Signal<string> = computed((): string => this.i18n.t('libraryTrend.totalMediaFiles'));
  protected readonly totalStorageLabel: Signal<string> = computed((): string => this.i18n.t('libraryTrend.totalStorage'));
  protected readonly noChangeLabel: Signal<string> = computed((): string => this.i18n.t('libraryTrend.noChange'));
  protected readonly loadErrorLabel: Signal<string> = computed((): string => this.i18n.t('common.tableLoadError'));
  protected readonly addedLabel: Signal<string> = computed((): string => this.i18n.t('libraryTrend.added'));
  protected readonly changedLabel: Signal<string> = computed((): string => this.i18n.t('libraryTrend.changed'));
  protected readonly deletedLabel: Signal<string> = computed((): string => this.i18n.t('libraryTrend.deleted'));
  protected readonly updatedLabel: Signal<string> = computed((): string => this.i18n.t('libraryTrend.updated'));

  /* Signals */
  protected readonly sonarrInstances: Signal<readonly ArrInstanceSettings[]> = computed(() => this.appSettingsService.sonarrInstances());
  protected readonly radarrInstances: Signal<readonly ArrInstanceSettings[]> = computed(() => this.appSettingsService.radarrInstances());
  protected readonly noInstancesConfigured: Signal<boolean> = computed(
    (): boolean => this.sonarrInstances().length === 0 && this.radarrInstances().length === 0,
  );
  protected readonly selectedStartDate: WritableSignal<Date> = signal(new Date(`${this.defaultHistoryDateRange.startDate}T00:00:00`));
  protected readonly selectedEndDate: WritableSignal<Date> = signal(new Date(`${this.defaultHistoryDateRange.endDate}T23:59:00`));
  protected readonly stats: WritableSignal<LibraryTrendStatisticsResponse | null> = signal<LibraryTrendStatisticsResponse | null>(null);
  protected readonly loading: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly loadError: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly isFiltered: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly noDataAvailable: Signal<boolean> = computed(
    (): boolean => this.stats() !== null && (this.stats()?.points.length ?? 0) === 0,
  );
  private readonly filterDefinitions: Signal<readonly FilterOption[]> = computed(() => {
    const filters: FilterOption[] = [];

    if (this.radarrInstances().length > 0) {
      const radarrOptions: FilterMultiSelectOption[] = this.radarrInstances().map((instance) => ({
        value: instance.id,
        label: instance.name,
      }));

      filters.push({
        id: 'radarr-instance-filter',
        label: this.i18n.t('filter.radarrInstances'),
        type: FilterOptionType.MULTISELECT,
        options: radarrOptions,
        selectedValues: [],
        supportsExclusion: true,
        onChanged: () => this.scheduleLoadStats(),
      });
    }

    if (this.sonarrInstances().length > 0) {
      const sonarrOptions: FilterMultiSelectOption[] = this.sonarrInstances().map((instance) => ({
        value: instance.id,
        label: instance.name,
      }));

      filters.push({
        id: 'sonarr-instance-filter',
        label: this.i18n.t('filter.sonarrInstances'),
        type: FilterOptionType.MULTISELECT,
        options: sonarrOptions,
        selectedValues: [],
        supportsExclusion: true,
        onChanged: () => this.scheduleLoadStats(),
      });
    }

    filters.push({
      id: 'date-range',
      label: this.dateRangeLabel(),
      type: FilterOptionType.DATE_TIME_RANGE,
      startDate: this.selectedStartDate(),
      endDate: this.selectedEndDate(),
      persist: false,
      onChanged: (value: { startDate: Date | null; endDate: Date | null }) => this.updateSelectedDateRange(value),
    });

    return filters;
  });
  protected readonly filterOptions: Signal<readonly FilterOption[]> = linkedSignal<readonly FilterOption[], readonly FilterOption[]>({
    source: this.filterDefinitions,
    computation: (filters, previous) =>
      filters.map((filter) => {
        const selected = previous?.value.find((option) => option.id === filter.id);
        if (filter.type === FilterOptionType.MULTISELECT && selected?.type === FilterOptionType.MULTISELECT) {
          return { ...filter, selectedValues: [...selected.selectedValues], exclude: selected.exclude ?? false };
        }
        return filter;
      }),
  });
  protected readonly mediaChartModel: Signal<TrendChartModel | null> = computed(() =>
    this.buildChartModel(this.stats()?.points ?? [], 'media'),
  );
  protected readonly storageChartModel: Signal<TrendChartModel | null> = computed(() =>
    this.buildChartModel(this.stats()?.points ?? [], 'storage'),
  );
  protected readonly historyEntries: Signal<readonly LibraryChangeHistoryEntry[]> = computed(() => this.stats()?.history ?? []);
  protected readonly historyViewport = viewChild(CdkVirtualScrollViewport);
  protected readonly historyItemSize: number = HISTORY_ITEM_SIZE;
  protected readonly highlightedHistoryDayRange: WritableSignal<Readonly<{ start: number; end: number }> | null> = signal<Readonly<{
    start: number;
    end: number;
  }> | null>(null);
  protected readonly highlightedHistoryEntries: Signal<ReadonlySet<LibraryChangeHistoryEntry>> = computed(
    (): ReadonlySet<LibraryChangeHistoryEntry> => {
      const range: Readonly<{ start: number; end: number }> | null = this.highlightedHistoryDayRange();
      if (!range) {
        return new Set<LibraryChangeHistoryEntry>();
      }

      return new Set<LibraryChangeHistoryEntry>(
        this.historyEntries().filter((entry: LibraryChangeHistoryEntry): boolean => {
          const occurredTime: number = new Date(entry.occurredAt).getTime();
          return occurredTime >= range.start && occurredTime < range.end;
        }),
      );
    },
  );

  /* Runtime state */
  private loadStatsQueued: boolean = false;
  private loadRequestId: number = 0;

  /* Lifecycle */
  ngOnInit(): void {
    void this.initializeAsync();
  }

  /* Helpers */
  private async initializeAsync(): Promise<void> {
    try {
      await this.appSettingsService.loadInstances();
    } catch (error) {
      this.loadError.set(error instanceof Error && error.message.trim().length > 0 ? error.message.trim() : this.loadErrorLabel());
      this.loading.set(false);
      return;
    }

    if (this.noInstancesConfigured()) {
      this.loading.set(false);
      return;
    }

    this.scheduleLoadStats();
  }

  private updateSelectedDateRange(value: { startDate: Date | null; endDate: Date | null }): void {
    const newStart: Date | null = value.startDate;
    const newEnd: Date | null = value.endDate;

    if (
      this.selectedStartDate() &&
      this.selectedEndDate() &&
      this.selectedStartDate().getTime() === (newStart?.getTime() ?? -1) &&
      this.selectedEndDate().getTime() === (newEnd?.getTime() ?? -1)
    ) {
      return;
    }

    if (newStart) {
      this.selectedStartDate.set(newStart);
    }
    if (newEnd) {
      this.selectedEndDate.set(newEnd);
    }
    this.scheduleLoadStats();
  }

  protected onFilterActiveCountChange(activeCount: number): void {
    this.isFiltered.set(activeCount > 0);
  }

  protected onChartPointSelected(occurredAt: string): void {
    const entries: readonly LibraryChangeHistoryEntry[] = this.historyEntries();
    if (entries.length === 0) {
      return;
    }

    const pointDate: Date = new Date(occurredAt);
    const dayStart: number = new Date(pointDate.getFullYear(), pointDate.getMonth(), pointDate.getDate()).getTime();
    const dayEnd: number = new Date(pointDate.getFullYear(), pointDate.getMonth(), pointDate.getDate() + 1).getTime();
    this.highlightedHistoryDayRange.set({ start: dayStart, end: dayEnd });
    const index: number = this.findHistoryEntryIndexForDay(entries, dayStart, dayEnd);
    this.scrollHistoryToIndex(index);
  }

  private findHistoryEntryIndexForDay(entries: readonly LibraryChangeHistoryEntry[], dayStart: number, dayEnd: number): number {
    const sameDayIndex: number = entries.findIndex((entry) => {
      const occurredTime: number = new Date(entry.occurredAt).getTime();
      return occurredTime >= dayStart && occurredTime < dayEnd;
    });
    if (sameDayIndex !== -1) {
      return sameDayIndex;
    }

    const nextFollowingIndex: number = entries.findIndex((entry) => new Date(entry.occurredAt).getTime() < dayStart);
    return nextFollowingIndex !== -1 ? nextFollowingIndex : entries.length - 1;
  }

  private scrollHistoryToIndex(index: number): void {
    const viewport: CdkVirtualScrollViewport | undefined = this.historyViewport();
    if (!viewport) {
      return;
    }

    const viewportHeight: number = viewport.getElementRef().nativeElement.clientHeight;
    const centeredTop: number = index * HISTORY_ITEM_SIZE - (viewportHeight - HISTORY_ITEM_SIZE) / 2;
    viewport.scrollTo({ top: Math.max(0, centeredTop), behavior: 'smooth' });
  }

  private scheduleLoadStats(): void {
    if (this.loadStatsQueued) {
      return;
    }

    this.loadStatsQueued = true;
    queueMicrotask(() => {
      this.loadStatsQueued = false;
      this.loadStats();
    });
  }

  private loadStats(): void {
    const loadRequestId: number = ++this.loadRequestId;
    if (this.noInstancesConfigured()) {
      this.stats.set(null);
      this.loadError.set(null);
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.loadError.set(null);

    const selectedSonarrIds: string[] = this.instanceFilterValues('sonarr-instance-filter');
    const selectedRadarrIds: string[] = this.instanceFilterValues('radarr-instance-filter');

    this.libraryTrendApi
      .fetchLibraryTrendStatistics(
        selectedSonarrIds,
        selectedRadarrIds,
        this.selectedStartDate().toISOString(),
        this.selectedEndDate().toISOString(),
      )
      .subscribe({
        next: (response) => {
          if (loadRequestId !== this.loadRequestId) {
            return;
          }

          this.stats.set(response);
          this.highlightedHistoryDayRange.set(null);
          this.loading.set(false);
        },
        error: () => {
          if (loadRequestId !== this.loadRequestId) {
            return;
          }

          this.loadError.set(this.loadErrorLabel());
          this.stats.set(null);
          this.loading.set(false);
        },
      });
  }

  private instanceFilterValues(id: string): string[] {
    const filters = this.filterOptions();
    const filter = filters.find((option) => option.id === id);
    if (filter?.type !== FilterOptionType.MULTISELECT) return [];
    const selected = multiSelectInstanceFilterValues(id, filters);
    if (!filter.exclude || filter.selectedValues.length === 0 || filter.selectedValues.includes(DEFAULT_FILTER_ALL)) return selected;
    return filter.options.filter((option) => !selected.includes(option.value)).map((option) => option.value);
  }

  private buildChartModel(points: readonly LibraryTrendPoint[], chartId: TrendChartSeries): TrendChartModel | null {
    if (points.length === 0) {
      return null;
    }

    const plotWidth: number = CHART_WIDTH - CHART_PADDING.left - CHART_PADDING.right;
    const plotHeight: number = CHART_HEIGHT - CHART_PADDING.top - CHART_PADDING.bottom;
    const values: number[] = points.map((point) => this.resolveChartValue(point, chartId));
    const minValue: number = Math.min(...values);
    const maxValue: number = Math.max(...values);
    const chartPoints: TrendChartPoint[] = points.map((point, index) => {
      const x: number =
        points.length === 1 ? CHART_PADDING.left + plotWidth / 2 : CHART_PADDING.left + (index / (points.length - 1)) * plotWidth;
      const value: number = this.resolveChartValue(point, chartId);
      const y: number = this.resolvePointY(value, minValue, maxValue, plotHeight);
      return {
        x,
        y,
        color: this.resolvePointColor(this.resolveChartDelta(point, chartId)),
        tooltipLines: this.buildTooltipLines(point, chartId),
        occurredAt: point.occurredAt,
      };
    });

    return {
      points: chartPoints,
      linePath: this.buildLinePath(chartPoints),
      areaPath: this.buildAreaPath(chartPoints, plotHeight),
      yTicks: this.buildYAxisTicks(minValue, maxValue, plotHeight, chartId),
      xLabels: this.buildXAxisLabels(chartPoints),
    };
  }

  private buildTooltipLines(point: LibraryTrendPoint, chartId: TrendChartSeries): readonly string[] {
    const delta: number = this.resolveChartDelta(point, chartId);
    let deltaLabel: string = this.noChangeLabel();
    if (delta !== 0) {
      if (chartId === 'media') {
        deltaLabel = `${this.changeLabel()}: ${this.formatSignedNumber(delta)} ${this.mediaFilesLabel()}`;
      } else {
        deltaLabel = `${this.changeLabel()}: ${this.formatSignedSize(delta)}`;
      }
    }

    return chartId === 'media'
      ? [this.formatTimestamp(point.occurredAt), `${this.mediaFilesLabel()}: ${this.formatNumber(point.mediaFileCount)}`, deltaLabel]
      : [
          this.formatTimestamp(point.occurredAt),
          `${this.storageLabel()}: ${this.formatBytesPipe.transform(point.totalSizeBytes)}`,
          deltaLabel,
        ];
  }

  private buildLinePath(points: readonly TrendChartPoint[]): string {
    return points.map((point, index) => `${index === 0 ? 'M' : 'L'} ${point.x.toFixed(2)} ${point.y.toFixed(2)}`).join(' ');
  }

  private buildAreaPath(points: readonly TrendChartPoint[], plotHeight: number): string | null {
    if (points.length < 2) {
      return null;
    }

    const baselineY: number = CHART_PADDING.top + plotHeight;
    const startPoint: TrendChartPoint = points[0];
    const endPoint: TrendChartPoint = points.at(-1)!;

    return [
      `M ${startPoint.x.toFixed(2)} ${baselineY.toFixed(2)}`,
      `L ${startPoint.x.toFixed(2)} ${startPoint.y.toFixed(2)}`,
      ...points.slice(1).map((point) => `L ${point.x.toFixed(2)} ${point.y.toFixed(2)}`),
      `L ${endPoint.x.toFixed(2)} ${baselineY.toFixed(2)}`,
      'Z',
    ].join(' ');
  }

  private buildYAxisTicks(minValue: number, maxValue: number, plotHeight: number, chartId: TrendChartSeries): readonly TrendChartTick[] {
    if (minValue === maxValue) {
      return [
        {
          y: CHART_PADDING.top + plotHeight / 2,
          label: this.formatAxisValue(maxValue, chartId),
        },
      ];
    }

    const tickCount: number = 4;
    const range: number = maxValue - minValue;
    return Array.from({ length: tickCount }, (_, index) => {
      const ratio: number = index / (tickCount - 1);
      return {
        y: CHART_PADDING.top + plotHeight - ratio * plotHeight,
        label: this.formatAxisValue(Math.round(minValue + ratio * range), chartId),
      };
    });
  }

  private buildXAxisLabels(points: readonly TrendChartPoint[]): readonly TrendChartLabel[] {
    if (points.length === 1) {
      return [
        {
          x: points[0].x,
          label: this.formatTimestamp(points[0].occurredAt),
          anchor: 'middle',
        },
      ];
    }

    return [
      {
        x: points[0].x,
        label: this.formatTimestamp(points[0].occurredAt),
        anchor: 'start',
      },
      {
        x: points.at(-1)!.x,
        label: this.formatTimestamp(points.at(-1)!.occurredAt),
        anchor: 'end',
      },
    ];
  }

  private resolveChartValue(point: LibraryTrendPoint, chartId: TrendChartSeries): number {
    return chartId === 'media' ? point.mediaFileCount : point.totalSizeBytes;
  }

  private resolveChartDelta(point: LibraryTrendPoint, chartId: TrendChartSeries): number {
    return chartId === 'media' ? point.deltaMediaFileCount : point.deltaSizeBytes;
  }

  private resolvePointY(value: number, minValue: number, maxValue: number, plotHeight: number): number {
    if (minValue === maxValue) {
      return CHART_PADDING.top + plotHeight / 2;
    }

    const range: number = maxValue - minValue;
    const normalized: number = (value - minValue) / range;
    return CHART_PADDING.top + plotHeight - normalized * plotHeight;
  }

  private resolvePointColor(delta: number): string {
    if (delta > 0) {
      return this.chartPositiveColor;
    }

    if (delta < 0) {
      return this.chartNegativeColor;
    }

    return this.chartAccentColor;
  }

  private formatAxisValue(value: number, chartId: TrendChartSeries): string {
    return chartId === 'media' ? this.formatNumber(value) : this.formatBytesPipe.transform(value);
  }

  private formatTimestamp(value: string): string {
    return new Intl.DateTimeFormat(this.localeId(), {
      dateStyle: 'medium',
    }).format(new Date(value));
  }

  protected formatNumber(value: number): string {
    return new Intl.NumberFormat(this.localeId()).format(value);
  }

  private formatSignedNumber(value: number): string {
    let prefix: string = '';
    if (value > 0) {
      prefix = '+';
    } else if (value < 0) {
      prefix = '-';
    }

    const absoluteValue: number = Math.abs(value);
    return `${prefix}${this.formatNumber(absoluteValue)}`;
  }

  private formatSignedSize(value: number): string {
    let prefix: string = '';
    if (value > 0) {
      prefix = '+';
    } else if (value < 0) {
      prefix = '-';
    }

    return `${prefix}${this.formatBytesPipe.transform(Math.abs(value))}`;
  }

  private localeId(): string {
    return this.appSettingsService.localeId();
  }
}
