export type CalendarApplication = 'Sonarr' | 'Radarr';

export type CalendarReleaseType = 'Episode' | 'Cinema' | 'Digital' | 'Physical';

export interface CalendarEntry {
  readonly id: string;
  readonly application: CalendarApplication;
  readonly instanceId: string;
  readonly instanceName: string;
  readonly mediaId: number;
  readonly seriesId: number | null;
  readonly title: string;
  readonly episodeTitle: string | null;
  readonly seasonNumber: number | null;
  readonly episodeNumber: number | null;
  readonly releaseType: CalendarReleaseType;
  // Pure calendar day (yyyy-MM-dd) when no clock time is known; null for timed entries.
  readonly releaseDate: string | null;
  // UTC timestamp when the clock time is known; null for pure-date entries.
  readonly releaseDateTimeUtc: string | null;
  readonly hasFile: boolean;
}
