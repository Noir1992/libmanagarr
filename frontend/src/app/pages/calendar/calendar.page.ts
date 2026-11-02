import { formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, OnDestroy, OnInit, signal, WritableSignal } from '@angular/core';
import { MatIcon } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { MatTableModule } from '@angular/material/table';
import { CalendarEntry, CalendarReleaseType } from '../../interfaces/calendar/calendar-entry';
import { CalendarInstanceStatus } from '../../interfaces/calendar/calendar-response';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { CalendarApiService } from '../../services/calendar-api.service';
import { CalendarDayDialogWidget } from '../../widgets/calendar-day-dialog/calendar-day-dialog.widget';
import { CalendarEntryDialogData, CalendarEntryDialogWidget } from '../../widgets/calendar-entry-dialog/calendar-entry-dialog.widget';
import { CalendarEntryCardWidget } from '../../widgets/calendar-entry-card/calendar-entry-card.widget';
import { LoadingErrorSectionWidget } from '../../widgets/sections/loading-error-section/loading-error-section.widget';
import { LoadingSectionWidget } from '../../widgets/sections/loading-section/loading-section.widget';
import { NoArrInstancesConfiguredSectionWidget } from '../../widgets/sections/no-arr-instances-configured-section/no-arr-instances-configured-section.widget';
import { NoDataSectionWidget } from '../../widgets/sections/no-data-section/no-data-section.widget';
import {
  addDays,
  CALENDAR_FILTER_TYPE_LABEL_KEYS,
  CALENDAR_FILTERABLE_RELEASE_TYPES,
  CALENDAR_STATUS_ICON_NAMES,
  CALENDAR_STATUS_LABEL_KEYS,
  CalendarDayCell,
  CalendarEntryStatus,
  dayKeyFromDate,
  deriveEntryStatus,
  entryDayKey,
  entryInstant,
  entryTimeLabel,
  episodeLabel,
  filterEntriesByTypes,
  groupEntriesByDay,
  isEntryPast,
  monthGrid,
  parseDayKey,
  sortAgendaEntries,
  startOfWeekMonday,
  weekDays,
} from './calendar.utils';

export type CalendarViewMode = 'week' | 'month' | 'agenda';

interface CalendarWeekColumn {
  readonly date: Date;
  readonly dayKey: string;
  readonly weekdayLabel: string;
  readonly dayNumberLabel: string;
  readonly isToday: boolean;
  readonly undated: readonly CalendarEntry[];
  readonly past: readonly CalendarEntry[];
  readonly upcoming: readonly CalendarEntry[];
}

interface CalendarMonthCell {
  readonly cell: CalendarDayCell;
  readonly entries: readonly CalendarEntry[];
  readonly visible: readonly CalendarEntry[];
  readonly hiddenCount: number;
}

interface CalendarMonthCompactRow {
  readonly entry: CalendarEntry;
  readonly timeText: string;
  readonly status: CalendarEntryStatus;
  readonly statusLabel: string;
  readonly statusIcon: string;
  readonly isPast: boolean;
}

interface CalendarAgendaRow {
  readonly entry: CalendarEntry;
  readonly showDate: boolean;
  readonly dayLabel: string;
  readonly timeText: string;
  readonly typeAttr: 'movie' | 'tv';
  readonly title: string;
  readonly status: CalendarEntryStatus;
  readonly statusLabel: string;
  readonly statusIcon: string;
  readonly releaseTypeText: string;
  readonly episodeText: string;
  readonly isPast: boolean;
  readonly nowLineBefore: boolean;
}

const AGENDA_COLUMNS: readonly string[] = ['date', 'time', 'media', 'episode'];
const MONTH_VISIBLE_ENTRIES = 3;
const POLL_INTERVAL_MS = 60_000;
const NOW_TICK_MS = 30_000;
const HIDDEN_TYPES_STORAGE_KEY: string = 'calendar:hidden-release-types';
const VIEW_MODE_STORAGE_KEY: string = 'calendar:view-mode';
const CALENDAR_VIEW_MODES: readonly CalendarViewMode[] = ['week', 'month', 'agenda'];

function readStoredViewMode(): CalendarViewMode {
  try {
    const raw: string | null = globalThis.localStorage.getItem(VIEW_MODE_STORAGE_KEY);
    if (CALENDAR_VIEW_MODES.includes(raw as CalendarViewMode)) {
      return raw as CalendarViewMode;
    }
  } catch {
    // Storage can be unavailable (private mode); the default view is used.
  }

  return 'week';
}

function readStoredHiddenReleaseTypes(): ReadonlySet<CalendarReleaseType> {
  try {
    const raw: string | null = globalThis.localStorage.getItem(HIDDEN_TYPES_STORAGE_KEY);
    if (raw === null) {
      return new Set();
    }

    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) {
      return new Set();
    }

    return new Set(parsed.filter((value: unknown): value is CalendarReleaseType => CALENDAR_FILTERABLE_RELEASE_TYPES.includes(value as CalendarReleaseType)));
  } catch {
    return new Set();
  }
}

@Component({
  selector: 'app-calendar-page',
  imports: [
    MatIcon,
    MatTableModule,
    CalendarEntryCardWidget,
    LoadingErrorSectionWidget,
    LoadingSectionWidget,
    NoArrInstancesConfiguredSectionWidget,
    NoDataSectionWidget,
  ],
  templateUrl: './calendar.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './calendar.page.scss',
})
export class CalendarPage implements OnInit, OnDestroy {
  private readonly calendarApi: CalendarApiService = inject(CalendarApiService);
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly dialog: MatDialog = inject(MatDialog);
  private readonly timeZoneId: string = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  private requestSequence: number = 0;
  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private nowTimer: ReturnType<typeof setInterval> | null = null;
  private documentVisible: boolean = true;

  protected readonly agendaColumns: readonly string[] = AGENDA_COLUMNS;

  protected readonly viewMode: WritableSignal<CalendarViewMode> = signal<CalendarViewMode>(readStoredViewMode());
  protected readonly anchorDate: WritableSignal<Date> = signal<Date>(new Date());
  protected readonly now: WritableSignal<Date> = signal<Date>(new Date());
  protected readonly entries: WritableSignal<readonly CalendarEntry[]> = signal<readonly CalendarEntry[]>([]);
  protected readonly instances: WritableSignal<readonly CalendarInstanceStatus[]> = signal<readonly CalendarInstanceStatus[]>([]);
  protected readonly loading: WritableSignal<boolean> = signal<boolean>(true);
  protected readonly loadError: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly notConfigured: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly hiddenReleaseTypes: WritableSignal<ReadonlySet<CalendarReleaseType>> = signal<ReadonlySet<CalendarReleaseType>>(
    readStoredHiddenReleaseTypes(),
  );
  protected readonly filterableReleaseTypes: readonly CalendarReleaseType[] = CALENDAR_FILTERABLE_RELEASE_TYPES;

  protected readonly title = computed((): string => this.i18n.t('calendar.title'));
  protected readonly description = computed((): string => this.i18n.t('calendar.description'));
  protected readonly weekViewLabel = computed((): string => this.i18n.t('calendar.view.week'));
  protected readonly monthViewLabel = computed((): string => this.i18n.t('calendar.view.month'));
  protected readonly agendaViewLabel = computed((): string => this.i18n.t('calendar.view.agenda'));
  protected readonly todayLabel = computed((): string => this.i18n.t('calendar.today'));
  protected readonly previousLabel = computed((): string => this.i18n.t('calendar.previous'));
  protected readonly nextLabel = computed((): string => this.i18n.t('calendar.next'));
  protected readonly refreshLabel = computed((): string => this.i18n.t('calendar.refresh'));
  protected readonly filterLabel = computed((): string => this.i18n.t('calendar.filter.label'));
  protected readonly retryLabel = computed((): string => this.i18n.t('calendar.instanceErrorRetry'));
  protected readonly dateColumnLabel = computed((): string => this.i18n.t('calendar.column.date'));
  protected readonly timeColumnLabel = computed((): string => this.i18n.t('calendar.column.time'));
  protected readonly mediaColumnLabel = computed((): string => this.i18n.t('calendar.column.media'));
  protected readonly seasonEpisodeColumnLabel = computed((): string => this.i18n.t('calendar.column.seasonEpisode'));

  protected readonly nowLineLabel = computed(
    (): string => `${this.i18n.t('calendar.now')} · ${this.formatTime(this.now())}`,
  );

  protected readonly failedInstanceNames = computed(
    (): readonly string[] => this.instances().filter((instance) => !instance.loaded).map((instance) => instance.name),
  );

  protected readonly instanceErrorText = computed((): string => {
    const names: readonly string[] = this.failedInstanceNames();
    return this.i18n.t('calendar.instanceError', { names: names.join(', ') });
  });

  protected readonly rangeLabel = computed((): string => {
    const range = this.currentRange();
    if (this.viewMode() === 'week') {
      return `${this.formatDate(range.start, 'd.M.yyyy')} – ${this.formatDate(addDays(range.end, -1), 'd.M.yyyy')}`;
    }

    return this.formatDate(range.start, 'LLLL y');
  });

  protected readonly weekColumns = computed((): readonly CalendarWeekColumn[] => {
    const now = this.now();
    const todayKey = dayKeyFromDate(now);
    const groups = groupEntriesByDay(this.visibleEntries());

    return weekDays(this.anchorDate()).map(
      (date: Date): CalendarWeekColumn => {
        const dayKey = dayKeyFromDate(date);
        const dayEntries = groups.get(dayKey) ?? [];
        const undated = dayEntries.filter((entry: CalendarEntry) => entryInstant(entry) === null);
        const timed = dayEntries.filter((entry: CalendarEntry) => entryInstant(entry) !== null);

        return {
          date,
          dayKey,
          weekdayLabel: this.formatDate(date, 'EEEE'),
          dayNumberLabel: this.formatDate(date, 'd'),
          isToday: dayKey === todayKey,
          undated,
          past: timed.filter((entry: CalendarEntry) => isEntryPast(entry, now)),
          upcoming: timed.filter((entry: CalendarEntry) => !isEntryPast(entry, now)),
        };
      },
    );
  });

  protected readonly monthCells = computed((): readonly CalendarMonthCell[] => {
    const groups = groupEntriesByDay(this.visibleEntries());

    return monthGrid(this.anchorDate()).map(
      (cell: CalendarDayCell): CalendarMonthCell => {
        const entries = groups.get(cell.dayKey) ?? [];
        return {
          cell,
          entries,
          visible: entries.slice(0, MONTH_VISIBLE_ENTRIES),
          hiddenCount: Math.max(0, entries.length - MONTH_VISIBLE_ENTRIES),
        };
      },
    );
  });

  protected readonly agendaRows = computed((): readonly CalendarAgendaRow[] => {
    const now = this.now();
    const range = this.currentRange();
    const sorted = sortAgendaEntries(this.visibleEntries());
    const nowInRange = now >= range.start && now < range.end;
    const firstFutureIndex = sorted.findIndex((entry: CalendarEntry) => !isEntryPast(entry, now));
    let previousDayKey: string | null = null;

    return sorted.map(
      (entry: CalendarEntry, index: number): CalendarAgendaRow => {
        const dayKey = entryDayKey(entry);
        const showDate = dayKey !== previousDayKey;
        previousDayKey = dayKey;
        const status = deriveEntryStatus(entry, now);

        return {
          entry,
          showDate,
          dayLabel: this.formatDate(parseDayKey(dayKey), 'EEE, d. MMM y'),
          timeText: entryTimeLabel(entry) ?? this.i18n.t('calendar.allDay'),
          typeAttr: entry.application === 'Radarr' ? 'movie' : 'tv',
          title: entry.title,
          status,
          statusLabel: this.i18n.t(CALENDAR_STATUS_LABEL_KEYS[status]),
          statusIcon: CALENDAR_STATUS_ICON_NAMES[status],
          releaseTypeText: this.i18n.t(CALENDAR_FILTER_TYPE_LABEL_KEYS[entry.releaseType]),
          episodeText: this.episodeText(entry),
          isPast: isEntryPast(entry, now),
          nowLineBefore: nowInRange && firstFutureIndex === index,
        };
      },
    );
  });

  protected readonly agendaNowLineAtEnd = computed((): boolean => {
    const now = this.now();
    const range = this.currentRange();
    const entries = this.visibleEntries();
    if (entries.length === 0 || now < range.start || now >= range.end) {
      return false;
    }

    return sortAgendaEntries(entries).every((entry: CalendarEntry) => isEntryPast(entry, now));
  });

  protected readonly hasEntries = computed((): boolean => this.entries().length > 0);

  protected readonly visibleEntries = computed((): readonly CalendarEntry[] =>
    filterEntriesByTypes(this.entries(), this.hiddenReleaseTypes()),
  );

  protected readonly hasVisibleEntries = computed((): boolean => this.visibleEntries().length > 0);

  protected readonly weekdayLabels = computed((): readonly string[] =>
    weekDays(this.now()).map((date: Date): string => this.formatDate(date, 'EEE')),
  );

  async ngOnInit(): Promise<void> {
    await this.appSettingsService.loadInstances().catch((): undefined => undefined);
    if (this.appSettingsService.sonarrInstances().length === 0 && this.appSettingsService.radarrInstances().length === 0) {
      this.notConfigured.set(true);
      this.loading.set(false);
      return;
    }

    this.load(false);
    this.startTimers();
  }

  ngOnDestroy(): void {
    if (this.pollTimer !== null) {
      clearInterval(this.pollTimer);
    }

    if (this.nowTimer !== null) {
      clearInterval(this.nowTimer);
    }

    document.removeEventListener('visibilitychange', this.onVisibilityChange);
  }

  protected setViewMode(mode: CalendarViewMode): void {
    if (this.viewMode() === mode) {
      return;
    }

    this.viewMode.set(mode);
    try {
      globalThis.localStorage.setItem(VIEW_MODE_STORAGE_KEY, mode);
    } catch {
      // Storage can be unavailable (private mode); the view still switches for this session.
    }

    this.load(false);
  }

  protected previous(): void {
    this.anchorDate.set(this.shiftAnchor(-1));
    this.load(false);
  }

  protected next(): void {
    this.anchorDate.set(this.shiftAnchor(1));
    this.load(false);
  }

  protected today(): void {
    this.anchorDate.set(new Date());
    this.load(false);
  }

  protected refresh(): void {
    this.load(true);
  }

  protected isReleaseTypeShown(type: CalendarReleaseType): boolean {
    return !this.hiddenReleaseTypes().has(type);
  }

  protected toggleReleaseTypeFilter(type: CalendarReleaseType): void {
    const hidden = new Set(this.hiddenReleaseTypes());
    if (hidden.has(type)) {
      hidden.delete(type);
    } else {
      hidden.add(type);
    }

    this.hiddenReleaseTypes.set(hidden);
    try {
      globalThis.localStorage.setItem(HIDDEN_TYPES_STORAGE_KEY, JSON.stringify([...hidden]));
    } catch {
      // Storage can be unavailable (private mode); the filter still works for this session.
    }
  }

  protected releaseTypeFilterLabel(type: CalendarReleaseType): string {
    return this.i18n.t(CALENDAR_FILTER_TYPE_LABEL_KEYS[type]);
  }

  protected openDayDialog(cell: CalendarMonthCell): void {
    this.dialog.open(CalendarDayDialogWidget, {
      width: '480px',
      maxWidth: '95vw',
      data: {
        title: this.formatDate(cell.cell.date, 'fullDate'),
        entries: cell.entries,
        now: this.now(),
      },
    });
  }

  protected openEntryDetail(entry: CalendarEntry): void {
    const data: CalendarEntryDialogData = { entry, now: this.now() };
    this.dialog.open(CalendarEntryDialogWidget, {
      width: '480px',
      maxWidth: '95vw',
      data,
    });
  }

  protected monthCompactRows(cell: CalendarMonthCell): readonly CalendarMonthCompactRow[] {
    const now = this.now();
    return cell.visible.map(
      (entry: CalendarEntry): CalendarMonthCompactRow => {
        const status = deriveEntryStatus(entry, now);
        return {
          entry,
          timeText: entryTimeLabel(entry) ?? '·',
          status,
          statusLabel: this.i18n.t(CALENDAR_STATUS_LABEL_KEYS[status]),
          statusIcon: CALENDAR_STATUS_ICON_NAMES[status],
          isPast: isEntryPast(entry, now),
        };
      },
    );
  }

  protected isTodayCell(cell: CalendarMonthCell): boolean {
    return cell.cell.dayKey === dayKeyFromDate(this.now());
  }

  protected monthHiddenLabel(count: number): string {
    return this.i18n.t('calendar.more', { count: String(count) });
  }

  private episodeText(entry: CalendarEntry): string {
    const episodeCode = episodeLabel(entry);
    if (episodeCode !== null && entry.episodeTitle) {
      return `${episodeCode} · ${entry.episodeTitle}`;
    }

    return episodeCode ?? entry.episodeTitle ?? '–';
  }

  private currentRange(): { start: Date; end: Date } {
    const anchor = this.anchorDate();
    if (this.viewMode() === 'week') {
      const start = startOfWeekMonday(anchor);
      return { start, end: addDays(start, 7) };
    }

    if (this.viewMode() === 'month') {
      const cells = monthGrid(anchor);
      return { start: cells[0].date, end: addDays(cells[cells.length - 1].date, 1) };
    }

    const start = new Date(anchor.getFullYear(), anchor.getMonth(), 1);
    return { start, end: new Date(anchor.getFullYear(), anchor.getMonth() + 1, 1) };
  }

  private shiftAnchor(direction: number): Date {
    const anchor = this.anchorDate();
    if (this.viewMode() === 'week') {
      return addDays(anchor, direction * 7);
    }

    return new Date(anchor.getFullYear(), anchor.getMonth() + direction, 1);
  }

  private load(refresh: boolean): void {
    const sequence = ++this.requestSequence;
    const range = this.currentRange();
    this.loading.set(true);
    this.loadError.set(null);

    this.calendarApi
      .fetchCalendar(dayKeyFromDate(range.start), dayKeyFromDate(range.end), this.timeZoneId, refresh)
      .subscribe({
        next: (response) => {
          if (sequence !== this.requestSequence) {
            return;
          }

          this.entries.set(response.entries);
          this.instances.set(response.instances);
          this.loading.set(false);
        },
        error: () => {
          if (sequence !== this.requestSequence) {
            return;
          }

          this.entries.set([]);
          this.instances.set([]);
          this.loading.set(false);
          this.loadError.set(this.i18n.t('common.error.loadData'));
        },
      });
  }

  private startTimers(): void {
    document.addEventListener('visibilitychange', this.onVisibilityChange);
    this.pollTimer = setInterval((): void => {
      if (this.documentVisible) {
        this.load(false);
      }
    }, POLL_INTERVAL_MS);
    this.nowTimer = setInterval((): void => this.now.set(new Date()), NOW_TICK_MS);
  }

  private readonly onVisibilityChange = (): void => {
    const wasHidden = !this.documentVisible;
    this.documentVisible = document.visibilityState === 'visible';
    if (this.documentVisible && wasHidden && !this.notConfigured()) {
      this.load(false);
    }
  };

  private formatDate(date: Date, pattern: string): string {
    return formatDate(date, pattern, this.appSettingsService.localeId());
  }

  private formatTime(date: Date): string {
    const hours = date.getHours();
    const minutes = date.getMinutes();
    return `${hours < 10 ? '0' : ''}${hours}:${minutes < 10 ? '0' : ''}${minutes}`;
  }
}
