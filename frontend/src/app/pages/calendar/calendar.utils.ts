import { CalendarEntry, CalendarReleaseType } from '../../interfaces/calendar/calendar-entry';
import { TranslationKey } from '../../services/i18n/_i18n-key';

export type CalendarEntryStatus = 'inLibrary' | 'missing' | 'unreleased' | 'notAired';

export const CALENDAR_STATUS_LABEL_KEYS: Record<CalendarEntryStatus, TranslationKey> = {
  inLibrary: 'calendar.status.inLibrary',
  missing: 'calendar.status.missing',
  notAired: 'calendar.status.notAired',
  unreleased: 'calendar.status.notReleased',
};

export const CALENDAR_STATUS_ICON_NAMES: Record<CalendarEntryStatus, string> = {
  inLibrary: 'check_circle',
  missing: 'error',
  notAired: 'schedule',
  unreleased: 'hourglass_empty',
};

export const CALENDAR_RELEASE_TYPE_LABEL_KEYS: Record<Exclude<CalendarReleaseType, 'Episode'>, TranslationKey> = {
  Cinema: 'calendar.releaseType.cinema',
  Digital: 'calendar.releaseType.digital',
  Physical: 'calendar.releaseType.physical',
};

// Chip order and labels for the release type filter; the episode chip covers all Sonarr entries.
export const CALENDAR_FILTERABLE_RELEASE_TYPES: readonly CalendarReleaseType[] = ['Episode', 'Cinema', 'Digital', 'Physical'];

export const CALENDAR_FILTER_TYPE_LABEL_KEYS: Record<CalendarReleaseType, TranslationKey> = {
  Episode: 'calendar.filter.episode',
  Cinema: 'calendar.releaseType.cinema',
  Digital: 'calendar.releaseType.digital',
  Physical: 'calendar.releaseType.physical',
};

// Display-only type filter: entries of hidden release types disappear from every calendar view.
export function filterEntriesByTypes(
  entries: readonly CalendarEntry[],
  hiddenTypes: ReadonlySet<CalendarReleaseType>,
): CalendarEntry[] {
  if (hiddenTypes.size === 0) {
    return [...entries];
  }

  return entries.filter((entry: CalendarEntry) => !hiddenTypes.has(entry.releaseType));
}

export interface CalendarDayCell {
  readonly date: Date;
  readonly inMonth: boolean;
  readonly dayKey: string;
}

export function padTwo(value: number): string {
  return value < 10 ? `0${value}` : `${value}`;
}

export function dayKeyFromDate(date: Date): string {
  return `${date.getFullYear()}-${padTwo(date.getMonth() + 1)}-${padTwo(date.getDate())}`;
}

// Parses "yyyy-MM-dd" as a local calendar day. Deliberately avoids Date(string), which would
// interpret the ISO form as UTC and shift the day for non-UTC viewers.
export function parseDayKey(dayKey: string): Date {
  const parts = dayKey.split('-');
  if (parts.length !== 3) {
    throw new Error(`Invalid day key: ${dayKey}`);
  }

  return new Date(Number(parts[0]), Number(parts[1]) - 1, Number(parts[2]));
}

export function addDays(date: Date, days: number): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days);
}

export function startOfWeekMonday(date: Date): Date {
  const mondayOffset = (date.getDay() + 6) % 7;
  return addDays(new Date(date.getFullYear(), date.getMonth(), date.getDate()), -mondayOffset);
}

export function weekDays(anchor: Date): Date[] {
  const start = startOfWeekMonday(anchor);
  return Array.from({ length: 7 }, (_, index) => addDays(start, index));
}

// Full Monday-anchored weeks covering the anchor month, so the month grid always shows complete weeks.
export function monthGrid(anchor: Date): CalendarDayCell[] {
  const year = anchor.getFullYear();
  const month = anchor.getMonth();
  const firstOfMonth = new Date(year, month, 1);
  const gridStart = startOfWeekMonday(firstOfMonth);
  const lastOfMonth = new Date(year, month + 1, 0);
  const gridEnd = addDays(startOfWeekMonday(lastOfMonth), 6);

  const cells: CalendarDayCell[] = [];
  for (let current = gridStart; current <= gridEnd; current = addDays(current, 1)) {
    cells.push({
      date: current,
      inMonth: current.getMonth() === month,
      dayKey: dayKeyFromDate(current),
    });
  }

  return cells;
}

// The instant of a timed entry, or null when only the calendar day is known.
export function entryInstant(entry: CalendarEntry): Date | null {
  if (entry.releaseDateTimeUtc === null) {
    return null;
  }

  const parsed = new Date(entry.releaseDateTimeUtc);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

export function entryDayKey(entry: CalendarEntry): string {
  const instant = entryInstant(entry);
  if (instant !== null) {
    return dayKeyFromDate(instant);
  }

  if (entry.releaseDate !== null) {
    return entry.releaseDate;
  }

  throw new Error(`Calendar entry ${entry.id} carries neither a date nor an instant`);
}

// A timed entry is past once its instant has passed; a pure-date entry only after its whole local
// day has ended, so releases stay "not yet aired" during their release day.
export function isEntryPast(entry: CalendarEntry, now: Date): boolean {
  const instant = entryInstant(entry);
  if (instant !== null) {
    return instant.getTime() < now.getTime();
  }

  const day = parseDayKey(entryDayKey(entry));
  return addDays(day, 1).getTime() <= now.getTime();
}

export function deriveEntryStatus(entry: CalendarEntry, now: Date): CalendarEntryStatus {
  if (entry.hasFile) {
    return 'inLibrary';
  }

  if (!isEntryPast(entry, now)) {
    return entry.releaseType === 'Episode' ? 'notAired' : 'unreleased';
  }

  // A cinema date alone never means the film is missing from the library.
  if (entry.releaseType === 'Cinema') {
    return 'unreleased';
  }

  return 'missing';
}

export function entryTimeLabel(entry: CalendarEntry): string | null {
  const instant = entryInstant(entry);
  if (instant === null) {
    return null;
  }

  return `${padTwo(instant.getHours())}:${padTwo(instant.getMinutes())}`;
}

// Agenda ordering: calendar day asc, entries without a known time first within a day, then by time,
// then title and id for a stable result.
export function sortAgendaEntries(entries: readonly CalendarEntry[]): CalendarEntry[] {
  return [...entries].sort((left, right) => {
    const dayCompare = entryDayKey(left).localeCompare(entryDayKey(right));
    if (dayCompare !== 0) {
      return dayCompare;
    }

    const leftInstant = entryInstant(left);
    const rightInstant = entryInstant(right);
    if (leftInstant === null && rightInstant !== null) {
      return -1;
    }

    if (leftInstant !== null && rightInstant === null) {
      return 1;
    }

    if (leftInstant !== null && rightInstant !== null) {
      const timeCompare = leftInstant.getTime() - rightInstant.getTime();
      if (timeCompare !== 0) {
        return timeCompare;
      }
    }

    const titleCompare = left.title.localeCompare(right.title);
    return titleCompare !== 0 ? titleCompare : left.id.localeCompare(right.id);
  });
}

export function groupEntriesByDay(entries: readonly CalendarEntry[]): Map<string, CalendarEntry[]> {
  const groups = new Map<string, CalendarEntry[]>();
  for (const entry of sortAgendaEntries(entries)) {
    const key = entryDayKey(entry);
    const bucket = groups.get(key);
    if (bucket === undefined) {
      groups.set(key, [entry]);
    } else {
      bucket.push(entry);
    }
  }

  return groups;
}

export function episodeLabel(entry: CalendarEntry): string | null {
  if (entry.seasonNumber === null || entry.episodeNumber === null) {
    return null;
  }

  return `S${padTwo(entry.seasonNumber)}E${padTwo(entry.episodeNumber)}`;
}
