import { ScrollingModule } from '@angular/cdk/scrolling';
import { ChangeDetectionStrategy, Component, computed, effect, inject, OnInit, signal, Signal, WritableSignal } from '@angular/core';
import { MatTableModule } from '@angular/material/table';
import { FilterOption } from '../../interfaces/filter-option';
import { FilterOptionType } from '../../enums/filter-option-type';
import { AppLogLevel } from '../../interfaces/general/app-log-level';
import { LogEntry } from '../../interfaces/log-entry';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppLogsService } from '../../services/app-logs.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { PageFilterWidget } from '../../widgets/page-filter/page-filter.widget';
import { LoadingErrorSectionWidget } from '../../widgets/sections/loading-error-section/loading-error-section.widget';
import { LoadingSectionWidget } from '../../widgets/sections/loading-section/loading-section.widget';
import { NoDataSectionWidget } from '../../widgets/sections/no-data-section/no-data-section.widget';

const LOG_LEVELS: readonly AppLogLevel[] = ['Debug', 'Information', 'Warning', 'Error'];

@Component({
  selector: 'app-logs-page',
  imports: [
    MatTableModule,
    PageFilterWidget,
    AppDatePipe,
    LoadingErrorSectionWidget,
    LoadingSectionWidget,
    NoDataSectionWidget,
    ScrollingModule,
  ],
  templateUrl: './logs.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './logs.page.scss',
})
export class LogsPage implements OnInit {
  /* Injections */
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly appLogsService: AppLogsService = inject(AppLogsService);
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);

  /* Constants */
  protected readonly displayedColumns: readonly string[] = ['timestamp', 'level', 'source', 'message'];

  /* I18n signals */
  protected readonly title: Signal<string> = computed((): string => this.i18n.t('settings.logs.title'));
  protected readonly description: Signal<string> = computed((): string => this.i18n.t('settings.logs.description'));
  protected readonly filterLevelLabel: Signal<string> = computed((): string => this.i18n.t('settings.logs.filter.levels'));
  protected readonly filterRangeLabel: Signal<string> = computed((): string => this.i18n.t('settings.logs.filter.range'));
  protected readonly logsTimestampColumnLabel: Signal<string> = computed((): string => this.i18n.t('settings.logs.column.timestamp'));
  protected readonly logsLevelColumnLabel: Signal<string> = computed((): string => this.i18n.t('settings.logs.column.level'));
  protected readonly logsSourceColumnLabel: Signal<string> = computed((): string => this.i18n.t('settings.logs.column.source'));
  protected readonly logsMessageColumnLabel: Signal<string> = computed((): string => this.i18n.t('settings.logs.column.message'));

  /* Signals */
  protected readonly loadSettingsError: Signal<string | null> = this.appSettingsService.loadError;
  protected readonly logsState: WritableSignal<readonly LogEntry[]> = signal<readonly LogEntry[]>([]);
  protected readonly loadError: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly loading: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly initialLoadComplete: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly selectedStartDate: WritableSignal<Date | null> = signal<Date | null>(null);
  protected readonly selectedEndDate: WritableSignal<Date | null> = signal<Date | null>(null);
  protected readonly selectedLevels: WritableSignal<readonly AppLogLevel[]> = signal<readonly AppLogLevel[]>([]);
  protected readonly isFiltered: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly noDataAvailable: Signal<boolean> = computed((): boolean => this.logsState().length === 0);
  protected readonly filters: Signal<readonly FilterOption[]> = computed((): readonly FilterOption[] => [
    {
      id: 'log-levels',
      label: this.filterLevelLabel(),
      type: FilterOptionType.MULTISELECT,
      options: LOG_LEVELS.map((level) => ({ value: level, label: level })),
      selectedValues: [...this.selectedLevels()],
      onChanged: (values: string[]) => this.updateSelectedLevels(values),
    },
    {
      id: 'log-range',
      label: this.filterRangeLabel(),
      type: FilterOptionType.DATE_TIME_RANGE,
      persist: false,
      startDate: this.selectedStartDate(),
      endDate: this.selectedEndDate(),
      onChanged: (value: { startDate: Date | null; endDate: Date | null }) => this.updateSelectedDateRange(value),
    },
  ]);
  protected readonly querySignature = computed(
    (): string =>
      `${this.selectedStartDate()?.toISOString() ?? ''}|${this.selectedEndDate()?.toISOString() ?? ''}|${this.selectedLevels().join(',')}`,
  );
  protected readonly logsRefreshEffect = effect(() => {
    this.querySignature();
    if (!this.initialLoadComplete()) {
      return;
    }

    if (this.skipInitialRefreshEffect) {
      this.skipInitialRefreshEffect = false;
      return;
    }

    void this.refreshLogsAsync();
  });

  /* Runtime state */
  private filtersReady: boolean = false;
  private skipInitialRefreshEffect: boolean = true;

  /* Lifecycle */
  ngOnInit(): void {
    this.initializeDefaultDates();
    void this.initializeAsync();
  }

  /* Handlers */
  protected async refreshLogsAsync(): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);

    try {
      const startDateTime: string = this.selectedStartDate()?.toISOString() ?? this.buildTodayStart();
      const endDateTime: string = this.selectedEndDate()?.toISOString() ?? this.buildTodayEnd();
      const levels = this.selectedLevels().length > 0 ? this.selectedLevels() : LOG_LEVELS;
      const entries: readonly LogEntry[] = await this.appLogsService.loadLogs(startDateTime, endDateTime, levels);
      this.logsState.set(entries);
    } catch (error) {
      this.loadError.set(
        error instanceof Error && error.message.trim().length > 0 ? error.message.trim() : this.i18n.t('settings.logs.loadError'),
      );
      this.logsState.set([]);
    } finally {
      this.loading.set(false);
    }
  }

  protected isLoading(): boolean {
    return this.loading();
  }

  /* Helpers */
  private initializeDefaultDates(): void {
    const today: Date = new Date();
    const start: Date = new Date(today.getFullYear(), today.getMonth(), today.getDate(), 0, 0, 0);
    const end: Date = new Date(today.getFullYear(), today.getMonth(), today.getDate(), 23, 59, 0);
    this.selectedStartDate.set(start);
    this.selectedEndDate.set(end);
  }

  private async initializeAsync(): Promise<void> {
    this.filtersReady = false;
    this.loading.set(true);

    if (this.loadSettingsError()) {
      this.loading.set(false);
      this.initialLoadComplete.set(true);
      this.filtersReady = true;
      return;
    }

    await this.refreshLogsAsync();
    this.initialLoadComplete.set(true);
    await Promise.resolve();
    this.filtersReady = true;
  }

  private updateSelectedLevels(values: string[]): void {
    const nextLevels: AppLogLevel[] = values.filter((value): value is AppLogLevel => LOG_LEVELS.includes(value as AppLogLevel));

    this.selectedLevels.set(nextLevels);
    if (this.filtersReady) {
      this.updateIsFiltered();
    }
  }

  private updateSelectedDateRange(value: { startDate: Date | null; endDate: Date | null }): void {
    this.selectedStartDate.set(value.startDate);
    this.selectedEndDate.set(value.endDate);
    if (this.filtersReady) {
      this.updateIsFiltered();
    }
  }

  private buildTodayStart(): string {
    const today: Date = new Date();
    const start: Date = new Date(today.getFullYear(), today.getMonth(), today.getDate(), 0, 0, 0);
    return start.toISOString();
  }

  private buildTodayEnd(): string {
    const today: Date = new Date();
    const end: Date = new Date(today.getFullYear(), today.getMonth(), today.getDate(), 23, 59, 0);
    return end.toISOString();
  }

  private updateIsFiltered(): void {
    const hasFilteredLevels: boolean = this.selectedLevels().length > 0 && this.selectedLevels().length !== LOG_LEVELS.length;

    const defaultStart: Date = new Date();
    defaultStart.setHours(0, 0, 0, 0);
    const defaultEnd: Date = new Date();
    defaultEnd.setHours(23, 59, 0, 0);

    const hasFilteredDateRange: boolean =
      !this.datesEqual(this.selectedStartDate(), defaultStart) || !this.datesEqual(this.selectedEndDate(), defaultEnd);

    this.isFiltered.set(hasFilteredLevels || hasFilteredDateRange);
  }

  private datesEqual(a: Date | null, b: Date): boolean {
    if (a === null) {
      return false;
    }
    return a.getTime() === b.getTime();
  }
}
