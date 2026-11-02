import { CalendarEntry, CalendarReleaseType } from '../../interfaces/calendar/calendar-entry';
import {
  addDays,
  deriveEntryStatus,
  dayKeyFromDate,
  entryDayKey,
  entryInstant,
  entryTimeLabel,
  episodeLabel,
  filterEntriesByTypes,
  groupEntriesByDay,
  isEntryPast,
  monthGrid,
  padTwo,
  parseDayKey,
  sortAgendaEntries,
  startOfWeekMonday,
  weekDays,
} from './calendar.utils';

function makeEntry(overrides: Partial<CalendarEntry> & { id: string }): CalendarEntry {
  return {
    application: 'Sonarr',
    instanceId: 'inst-1',
    instanceName: 'Sonarr',
    mediaId: 1,
    seriesId: null,
    title: 'Title',
    episodeTitle: null,
    seasonNumber: null,
    episodeNumber: null,
    releaseType: 'Episode',
    releaseDate: null,
    releaseDateTimeUtc: null,
    hasFile: false,
    ...overrides,
  };
}

// Builds an instant from local calendar fields so the resulting ISO string round-trips to the
// same wall-clock time in every timezone the suite may run in.
function localInstant(year: number, month: number, day: number, hour: number, minute: number): string {
  return new Date(year, month, day, hour, minute).toISOString();
}

function movieEntry(id: string, releaseType: CalendarReleaseType, releaseDate: string): CalendarEntry {
  return makeEntry({ id, application: 'Radarr', instanceName: 'Radarr', releaseType, releaseDate });
}

describe('calendar day helpers', () => {
  it('pads single-digit values', () => {
    expect(padTwo(5)).toBe('05');
    expect(padTwo(12)).toBe('12');
    expect(padTwo(0)).toBe('00');
  });

  it('formats a local date as yyyy-MM-dd with zero padding', () => {
    expect(dayKeyFromDate(new Date(2026, 10, 5))).toBe('2026-11-05');
    expect(dayKeyFromDate(new Date(2026, 0, 1))).toBe('2026-01-01');
  });

  it('parses a day key as local midnight', () => {
    const parsed = parseDayKey('2026-11-03');
    expect(parsed.getFullYear()).toBe(2026);
    expect(parsed.getMonth()).toBe(10);
    expect(parsed.getDate()).toBe(3);
    expect(parsed.getHours()).toBe(0);
    expect(parsed.getMinutes()).toBe(0);
  });

  it('rejects malformed day keys', () => {
    expect(() => parseDayKey('2026-11')).toThrow();
    expect(() => parseDayKey('garbage')).toThrow();
  });

  it('adds days by calendar arithmetic, staying correct across a DST transition', () => {
    // 2026-10-25 is the European DST end; date-based arithmetic must not drift by an hour.
    expect(dayKeyFromDate(addDays(new Date(2026, 9, 24), 7))).toBe('2026-10-31');
    expect(dayKeyFromDate(addDays(parseDayKey('2026-11-02'), -7))).toBe('2026-10-26');
  });

  it('anchors the week at Monday, mapping Sunday to the previous Monday', () => {
    // 2026-11-01 is a Sunday, 2026-11-02 a Monday, 2026-11-07 a Saturday.
    expect(dayKeyFromDate(startOfWeekMonday(new Date(2026, 10, 1)))).toBe('2026-10-26');
    expect(dayKeyFromDate(startOfWeekMonday(new Date(2026, 10, 2)))).toBe('2026-11-02');
    expect(dayKeyFromDate(startOfWeekMonday(new Date(2026, 10, 7)))).toBe('2026-11-02');
  });

  it('drops the clock time when anchoring a week', () => {
    const start = startOfWeekMonday(new Date(2026, 10, 4, 15, 45));
    expect(start.getHours()).toBe(0);
    expect(start.getMinutes()).toBe(0);
  });

  it('builds a full Monday-to-Sunday week', () => {
    const days = weekDays(new Date(2026, 10, 1));
    expect(days.map(dayKeyFromDate)).toEqual([
      '2026-10-26',
      '2026-10-27',
      '2026-10-28',
      '2026-10-29',
      '2026-10-30',
      '2026-10-31',
      '2026-11-01',
    ]);
  });

  it('covers the anchor month in complete weeks with adjacency flags', () => {
    // November 2026 starts on a Sunday and ends on a Monday, so the grid spans 2026-10-26..2026-12-06.
    const cells = monthGrid(new Date(2026, 10, 2));
    expect(cells.length % 7).toBe(0);
    expect(cells[0].dayKey).toBe('2026-10-26');
    expect(cells[cells.length - 1].dayKey).toBe('2026-12-06');
    expect(cells.filter((cell) => cell.inMonth).length).toBe(30);
    expect(cells[0].inMonth).toBeFalse();
    expect(cells.find((cell) => cell.dayKey === '2026-11-15')?.inMonth).toBeTrue();
    expect(cells.find((cell) => cell.dayKey === '2026-12-01')?.inMonth).toBeFalse();
  });
});

describe('calendar entry instant helpers', () => {
  it('returns null for date-only and unparseable instants', () => {
    expect(entryInstant(makeEntry({ id: 'a', releaseDate: '2026-11-03' }))).toBeNull();
    expect(entryInstant(makeEntry({ id: 'b', releaseDateTimeUtc: 'not-a-date' }))).toBeNull();
  });

  it('uses the local day of the instant for timed entries and the raw date otherwise', () => {
    const timed = makeEntry({ id: 'a', releaseDateTimeUtc: localInstant(2026, 10, 3, 23, 30) });
    const dated = makeEntry({ id: 'b', releaseDate: '2026-11-03' });
    expect(entryDayKey(timed)).toBe('2026-11-03');
    expect(entryDayKey(dated)).toBe('2026-11-03');
  });

  it('throws when an entry carries neither a date nor an instant', () => {
    expect(() => entryDayKey(makeEntry({ id: 'a' }))).toThrow();
  });

  it('formats the local wall-clock time and omits it for date-only entries', () => {
    const timed = makeEntry({ id: 'a', releaseDateTimeUtc: localInstant(2026, 10, 3, 9, 5) });
    expect(entryTimeLabel(timed)).toBe('09:05');
    expect(entryTimeLabel(makeEntry({ id: 'b', releaseDate: '2026-11-03' }))).toBeNull();
  });

  it('formats the episode code and stays null when either number is missing', () => {
    expect(episodeLabel(makeEntry({ id: 'a', seasonNumber: 2, episodeNumber: 7 }))).toBe('S02E07');
    expect(episodeLabel(makeEntry({ id: 'b', seasonNumber: 12, episodeNumber: 25 }))).toBe('S12E25');
    expect(episodeLabel(makeEntry({ id: 'c', seasonNumber: 2 }))).toBeNull();
    expect(episodeLabel(makeEntry({ id: 'd', episodeNumber: 7 }))).toBeNull();
  });
});

describe('calendar past detection', () => {
  it('treats timed entries as past only once their instant passed', () => {
    const now = new Date(2026, 10, 3, 12, 0);
    const past = makeEntry({ id: 'a', releaseDateTimeUtc: localInstant(2026, 10, 3, 11, 59) });
    const exact = makeEntry({ id: 'b', releaseDateTimeUtc: now.toISOString() });
    const future = makeEntry({ id: 'c', releaseDateTimeUtc: localInstant(2026, 10, 3, 12, 1) });
    expect(isEntryPast(past, now)).toBeTrue();
    expect(isEntryPast(exact, now)).toBeFalse();
    expect(isEntryPast(future, now)).toBeFalse();
  });

  it('flips date-only entries only after the whole local day has ended', () => {
    const entry = makeEntry({ id: 'a', releaseDate: '2026-11-03' });
    expect(isEntryPast(entry, new Date(2026, 10, 3, 0, 0))).toBeFalse();
    expect(isEntryPast(entry, new Date(2026, 10, 3, 23, 59, 59))).toBeFalse();
    expect(isEntryPast(entry, new Date(2026, 10, 4, 0, 0))).toBeTrue();
  });
});

describe('calendar status derivation', () => {
  const now = new Date(2026, 10, 3, 12, 0);

  it('prioritises in-library over every time-based state', () => {
    const pastFile = makeEntry({ id: 'a', releaseDateTimeUtc: localInstant(2026, 9, 1, 20, 0), hasFile: true });
    const futureFile = makeEntry({ id: 'b', releaseDateTimeUtc: localInstant(2026, 10, 20, 20, 0), hasFile: true });
    expect(deriveEntryStatus(pastFile, now)).toBe('inLibrary');
    expect(deriveEntryStatus(futureFile, now)).toBe('inLibrary');
  });

  it('labels future entries as not aired or unreleased', () => {
    const futureEpisode = makeEntry({ id: 'a', releaseDateTimeUtc: localInstant(2026, 10, 5, 20, 0) });
    const futureMovie = movieEntry('b', 'Digital', '2026-11-10');
    expect(deriveEntryStatus(futureEpisode, now)).toBe('notAired');
    expect(deriveEntryStatus(futureMovie, now)).toBe('unreleased');
  });

  it('labels past entries without a file as missing', () => {
    const pastEpisode = makeEntry({ id: 'a', releaseDateTimeUtc: localInstant(2026, 9, 20, 20, 0) });
    const pastMovie = movieEntry('b', 'Digital', '2026-10-20');
    expect(deriveEntryStatus(pastEpisode, now)).toBe('missing');
    expect(deriveEntryStatus(pastMovie, now)).toBe('missing');
  });

  it('never labels cinema releases as missing, even long past', () => {
    const pastCinemaDay = movieEntry('a', 'Cinema', '2026-09-01');
    const pastCinemaTimed = makeEntry({
      id: 'b',
      application: 'Radarr',
      instanceName: 'Radarr',
      releaseType: 'Cinema',
      releaseDateTimeUtc: localInstant(2026, 9, 1, 20, 0),
    });
    expect(deriveEntryStatus(pastCinemaDay, now)).toBe('unreleased');
    expect(deriveEntryStatus(pastCinemaTimed, now)).toBe('unreleased');
  });

  it('keeps date-only entries unreleased during their release day', () => {
    const sameDayMovie = movieEntry('a', 'Digital', '2026-11-03');
    expect(deriveEntryStatus(sameDayMovie, now)).toBe('unreleased');
    expect(deriveEntryStatus(sameDayMovie, new Date(2026, 10, 4, 0, 0))).toBe('missing');
  });
});

describe('calendar agenda ordering', () => {
  it('sorts by day, puts undated entries first within a day, then by time, title and id', () => {
    const undated = makeEntry({ id: 'undated', releaseDate: '2026-11-03' });
    const late = makeEntry({ id: 'late', releaseDateTimeUtc: localInstant(2026, 10, 3, 18, 0) });
    const early = makeEntry({ id: 'early', releaseDateTimeUtc: localInstant(2026, 10, 3, 9, 0) });
    const previousDay = makeEntry({ id: 'prev', releaseDate: '2026-11-02' });

    const sorted = sortAgendaEntries([late, previousDay, early, undated]).map((entry) => entry.id);
    expect(sorted).toEqual(['prev', 'undated', 'early', 'late']);
  });

  it('breaks time ties by title and id', () => {
    const instant = localInstant(2026, 10, 3, 20, 0);
    const zebra = makeEntry({ id: 'z', title: 'Zebra', releaseDateTimeUtc: instant });
    const apple = makeEntry({ id: 'a', title: 'Apple', releaseDateTimeUtc: instant });
    expect(sortAgendaEntries([zebra, apple]).map((entry) => entry.id)).toEqual(['a', 'z']);
  });

  it('groups entries into day-ordered buckets', () => {
    const firstNov = makeEntry({ id: 'a', releaseDate: '2026-11-01' });
    const thirdNovLate = makeEntry({ id: 'b', releaseDateTimeUtc: localInstant(2026, 10, 3, 18, 0) });
    const thirdNovEarly = makeEntry({ id: 'c', releaseDateTimeUtc: localInstant(2026, 10, 3, 9, 0) });

    const groups = groupEntriesByDay([thirdNovLate, firstNov, thirdNovEarly]);
    expect([...groups.keys()]).toEqual(['2026-11-01', '2026-11-03']);
    expect(groups.get('2026-11-01')?.map((entry) => entry.id)).toEqual(['a']);
    expect(groups.get('2026-11-03')?.map((entry) => entry.id)).toEqual(['c', 'b']);
  });
});

describe('calendar release type filter', () => {
  const episode = makeEntry({ id: 'ep', releaseDate: '2026-11-03' });
  const cinema = makeEntry({ id: 'cin', application: 'Radarr', releaseType: 'Cinema', releaseDate: '2026-11-03' });
  const digital = makeEntry({ id: 'dig', application: 'Radarr', releaseType: 'Digital', releaseDate: '2026-11-03' });
  const physical = makeEntry({ id: 'phy', application: 'Radarr', releaseType: 'Physical', releaseDate: '2026-11-03' });
  const all = [episode, cinema, digital, physical];

  it('keeps every entry when nothing is hidden', () => {
    expect(filterEntriesByTypes(all, new Set<CalendarReleaseType>()).map((entry) => entry.id)).toEqual(['ep', 'cin', 'dig', 'phy']);
  });

  it('hides exactly the entries of the hidden release types', () => {
    expect(filterEntriesByTypes(all, new Set<CalendarReleaseType>(['Cinema'])).map((entry) => entry.id)).toEqual(['ep', 'dig', 'phy']);
    expect(filterEntriesByTypes(all, new Set<CalendarReleaseType>(['Cinema', 'Physical'])).map((entry) => entry.id)).toEqual(['ep', 'dig']);
  });

  it('leaves only episodes when all movie release types are hidden', () => {
    const hidden = new Set<CalendarReleaseType>(['Cinema', 'Digital', 'Physical']);
    expect(filterEntriesByTypes(all, hidden).map((entry) => entry.id)).toEqual(['ep']);
  });

  it('hides everything when all types are hidden', () => {
    const hidden = new Set<CalendarReleaseType>(['Episode', 'Cinema', 'Digital', 'Physical']);
    expect(filterEntriesByTypes(all, hidden)).toEqual([]);
  });

  it('does not mutate its input when nothing is hidden', () => {
    const filtered = filterEntriesByTypes(all, new Set<CalendarReleaseType>());
    expect(filtered).not.toBe(all);
    expect(all.length).toBe(4);
  });
});
