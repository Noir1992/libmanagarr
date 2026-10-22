import { LibrarySortDirection } from '../enums/library-sort-direction';
import { LibrarySortField } from '../enums/library-sort-field';
import { TitleSortingSettings } from '../interfaces/settings/title-sorting-settings';
import { buildTitleSortKey } from './title-sort-key-utils';

export interface LibrarySortKey {
  title: string;
  airedAt?: string | null;
  addedAt?: string | null;
}

interface PreparedLibrarySortKey {
  title: string;
  sortTitle: string;
  date: number | null;
}

/**
 * Mirrors the ordering of the backend LibrarySorter so that tables, which arrive pre-sorted, and the merged tile
 * view, which is sorted here, never disagree. Entries without a value trail in both directions, the title keeps
 * equal values in a stable order.
 */
export function sortLibraryEntries<T>(
  entries: readonly T[],
  keyOf: (entry: T) => LibrarySortKey,
  field: LibrarySortField,
  direction: LibrarySortDirection,
  titleSorting: TitleSortingSettings = { enabled: false, ignoredLeadingWords: [] },
): T[] {
  const descending: boolean = direction === LibrarySortDirection.DESCENDING;
  // Prepare derived values once per entry, rather than rebuilding them inside each comparison.
  return entries
    .map((entry) => {
      const key = keyOf(entry);
      return {
        entry,
        key: {
          title: key.title.toUpperCase(),
          sortTitle: buildTitleSortKey(key.title, titleSorting).toUpperCase(),
          date: field === LibrarySortField.ALPHABETICAL ? null : timeValue(dateOf(key, field)),
        },
      };
    })
    .sort((a, b) => compareKeys(a.key, b.key, field, descending))
    .map(({ entry }) => entry);
}

/** Newest of the given air dates, ignoring the ones that are missing or unparseable. */
export function latestAirDate(dates: readonly (string | null | undefined)[]): string | null {
  let newest: string | null = null;
  let newestTime: number = Number.NEGATIVE_INFINITY;

  for (const date of dates) {
    if (typeof date !== 'string' || date.length === 0) {
      continue;
    }

    const time: number = new Date(date).getTime();

    if (Number.isNaN(time) || time <= newestTime) {
      continue;
    }

    newestTime = time;
    newest = date;
  }

  return newest;
}

/** Ordinal comparison ignoring case, matching the StringComparer used by the backend. */
export function compareTitlesIgnoringCase(a: string, b: string): number {
  const left: string = a.toUpperCase();
  const right: string = b.toUpperCase();

  if (left < right) {
    return -1;
  }

  return left > right ? 1 : 0;
}

function compareKeys(a: PreparedLibrarySortKey, b: PreparedLibrarySortKey, field: LibrarySortField, descending: boolean): number {
  const byTitle = comparePreparedTitles(a.sortTitle, b.sortTitle) || comparePreparedTitles(a.title, b.title);
  if (field === LibrarySortField.ALPHABETICAL) {
    return descending ? -byTitle : byTitle;
  }

  const byValue: number = compareValues(a.date, b.date, descending);

  return byValue !== 0 ? byValue : byTitle;
}

function comparePreparedTitles(a: string, b: string): number {
  if (a < b) return -1;
  return a > b ? 1 : 0;
}

function dateOf(key: LibrarySortKey, field: LibrarySortField): string | null | undefined {
  return field === LibrarySortField.ADDED ? key.addedAt : key.airedAt;
}

function compareValues(a: number | null, b: number | null, descending: boolean): number {
  if (a === null || b === null) {
    if (a === b) {
      return 0;
    }

    return a === null ? 1 : -1;
  }

  return descending ? b - a : a - b;
}

function timeValue(date: string | null | undefined): number | null {
  if (!date) {
    return null;
  }

  const time: number = new Date(date).getTime();

  return Number.isNaN(time) ? null : time;
}
