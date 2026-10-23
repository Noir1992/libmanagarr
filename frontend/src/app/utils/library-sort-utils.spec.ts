import { LibrarySortDirection } from '../enums/library-sort-direction';
import { LibrarySortField } from '../enums/library-sort-field';
import { TitleSortingSettings } from '../interfaces/settings/title-sorting-settings';
import { latestAirDate, sortLibraryEntries, LibrarySortKey } from './library-sort-utils';
import titleSortingCases from '../../../../test-data/title-sorting-cases.json';

type Entry = {
  title: string;
  airedAt: string | null;
  addedAt: string | null;
};

function entry(title: string, airedAt: string | null = null, addedAt: string | null = null): Entry {
  return { title, airedAt, addedAt };
}

function order(entries: Entry[], field: LibrarySortField, direction: LibrarySortDirection): string[] {
  return sortLibraryEntries(entries, (item: Entry): LibrarySortKey => item, field, direction).map((item) => item.title);
}

describe('library sort utils', () => {
  for (const testCase of titleSortingCases.orderCases) {
    it(`matches the shared ordering reference: ${testCase.name}`, () => {
      const input = testCase.titles.map((title) => entry(title));
      const rules = { enabled: true, ignoredLeadingWords: [...testCase.words] };
      for (const [direction, expected] of [
        [LibrarySortDirection.ASCENDING, testCase.ascending],
        [LibrarySortDirection.DESCENDING, testCase.descending],
      ] as const) {
        expect(
          sortLibraryEntries(input, (item) => item, LibrarySortField.ALPHABETICAL, direction, rules).map((item) => item.title),
        ).toEqual(expected);
      }
    });
  }

  const entries: Entry[] = [
    entry('No dates'),
    entry('Newest', '2024-12-31T00:00:00Z', '2026-09-09T00:00:00Z'),
    entry('Oldest', '2020-01-01T00:00:00Z', '2024-01-01T00:00:00Z'),
    entry('Middle', '2022-06-15T00:00:00Z', '2025-06-15T00:00:00Z'),
  ];

  it('orders titles ignoring case while keeping the input untouched', () => {
    const input: Entry[] = [entry('Carol'), entry('alice'), entry('Bob')];

    expect(sortLibraryEntries(input, (item) => item, LibrarySortField.ALPHABETICAL, LibrarySortDirection.ASCENDING)).toEqual([
      entry('alice'),
      entry('Bob'),
      entry('Carol'),
    ]);
    expect(input.map((item) => item.title)).toEqual(['Carol', 'alice', 'Bob']);
  });

  it('reverses the title order for descending alphabetical sorts', () => {
    expect(order(entries, LibrarySortField.ALPHABETICAL, LibrarySortDirection.DESCENDING)).toEqual([
      'Oldest',
      'No dates',
      'Newest',
      'Middle',
    ]);
  });

  it('orders by the requested date and trails entries without a value in both directions', () => {
    expect(order(entries, LibrarySortField.AIRED, LibrarySortDirection.ASCENDING)).toEqual(['Oldest', 'Middle', 'Newest', 'No dates']);
    expect(order(entries, LibrarySortField.AIRED, LibrarySortDirection.DESCENDING)).toEqual(['Newest', 'Middle', 'Oldest', 'No dates']);
    expect(order(entries, LibrarySortField.ADDED, LibrarySortDirection.ASCENDING)).toEqual(['Oldest', 'Middle', 'Newest', 'No dates']);
    expect(order(entries, LibrarySortField.ADDED, LibrarySortDirection.DESCENDING)).toEqual(['Newest', 'Middle', 'Oldest', 'No dates']);
  });

  it('keeps equal dates in ascending title order for both directions', () => {
    const equal: Entry[] = [
      entry('Charlie', '2024-05-05T00:00:00Z'),
      entry('Alpha', '2024-05-05T00:00:00Z'),
      entry('Bravo', '2024-05-05T00:00:00Z'),
    ];

    for (const direction of [LibrarySortDirection.ASCENDING, LibrarySortDirection.DESCENDING]) {
      expect(order(equal, LibrarySortField.AIRED, direction)).toEqual(['Alpha', 'Bravo', 'Charlie']);
    }
  });

  it('treats unparseable dates as missing', () => {
    const broken: Entry[] = [entry('Known', '2021-01-01T00:00:00Z'), entry('Broken', 'not a date')];

    expect(order(broken, LibrarySortField.AIRED, LibrarySortDirection.DESCENDING)).toEqual(['Known', 'Broken']);
  });

  it('reads the added field independently of the aired one', () => {
    const mixed: Entry[] = [
      entry('EarlyAiredLateAdded', '2021-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
      entry('LateAiredEarlyAdded', '2025-01-01T00:00:00Z', '2020-01-01T00:00:00Z'),
    ];

    expect(order(mixed, LibrarySortField.AIRED, LibrarySortDirection.ASCENDING)).toEqual(['EarlyAiredLateAdded', 'LateAiredEarlyAdded']);
    expect(order(mixed, LibrarySortField.ADDED, LibrarySortDirection.ASCENDING)).toEqual(['LateAiredEarlyAdded', 'EarlyAiredLateAdded']);
  });

  it('sorts by derived titles and original titles in both alphabetical directions', () => {
    const input = [entry('The Office'), entry('A Quiet Place'), entry('Office'), entry('Batman')];
    const rules: TitleSortingSettings = { enabled: true, ignoredLeadingWords: ['A', 'The'] };
    for (const [direction, expected] of [
      [LibrarySortDirection.ASCENDING, ['Batman', 'Office', 'The Office', 'A Quiet Place']],
      [LibrarySortDirection.DESCENDING, ['A Quiet Place', 'The Office', 'Office', 'Batman']],
    ] as const) {
      expect(sortLibraryEntries(input, (item) => item, LibrarySortField.ALPHABETICAL, direction, rules).map((item) => item.title)).toEqual([
        ...expected,
      ]);
    }
    expect(input.map((item) => item.title)).toEqual(['The Office', 'A Quiet Place', 'Office', 'Batman']);
    expect(rules.ignoredLeadingWords).toEqual(['A', 'The']);
  });

  it('uses ascending derived titles for equal and missing dates, preserving missing dates at the end', () => {
    const rules: TitleSortingSettings = { enabled: true, ignoredLeadingWords: ['The'] };
    for (const field of [LibrarySortField.AIRED, LibrarySortField.ADDED]) {
      const input = [
        entry('The Office', '2024-01-01', '2024-01-01'),
        entry('Office', '2024-01-01', '2024-01-01'),
        entry('The Missing'),
        entry('Missing'),
        entry('Newest', '2026-01-01', '2026-01-01'),
      ];
      expect(sortLibraryEntries(input, (item) => item, field, LibrarySortDirection.ASCENDING, rules).map((item) => item.title)).toEqual([
        'Office',
        'The Office',
        'Newest',
        'Missing',
        'The Missing',
      ]);
      expect(sortLibraryEntries(input, (item) => item, field, LibrarySortDirection.DESCENDING, rules).map((item) => item.title)).toEqual([
        'Newest',
        'Office',
        'The Office',
        'Missing',
        'The Missing',
      ]);
    }
  });

  it('preserves the existing order for disabled rules and empty lists', () => {
    const input = [entry('The Office'), entry('Batman'), entry('A Quiet Place')];
    for (const rules of [
      { enabled: false, ignoredLeadingWords: ['A', 'The'] },
      { enabled: true, ignoredLeadingWords: [] },
    ]) {
      expect(
        sortLibraryEntries(input, (item) => item, LibrarySortField.ALPHABETICAL, LibrarySortDirection.ASCENDING, rules).map(
          (item) => item.title,
        ),
      ).toEqual(['A Quiet Place', 'Batman', 'The Office']);
    }
  });

  it('prepares each entry once and retains stable order when all title comparisons tie', () => {
    const input = [
      { ...entry('Office'), id: 1 },
      { ...entry('office'), id: 2 },
      { ...entry('OFFICE'), id: 3 },
    ];
    const keyOf = jasmine.createSpy('keyOf').and.callFake((item: Entry) => item);
    for (const direction of [LibrarySortDirection.ASCENDING, LibrarySortDirection.DESCENDING]) {
      keyOf.calls.reset();
      expect(
        sortLibraryEntries(input, keyOf, LibrarySortField.ALPHABETICAL, direction, { enabled: true, ignoredLeadingWords: ['The'] }).map(
          (item) => item.id,
        ),
      ).toEqual([1, 2, 3]);
      expect(keyOf).toHaveBeenCalledTimes(input.length);
    }
  });
});

describe('latestAirDate', () => {
  it('returns the newest date and skips missing or unparseable ones', () => {
    expect(latestAirDate(['2021-01-01T00:00:00Z', null, '2024-06-30T00:00:00Z', undefined, 'not a date'])).toBe('2024-06-30T00:00:00Z');
  });

  it('returns null when no date is usable', () => {
    expect(latestAirDate([null, undefined, '', 'not a date'])).toBeNull();
    expect(latestAirDate([])).toBeNull();
  });
});
