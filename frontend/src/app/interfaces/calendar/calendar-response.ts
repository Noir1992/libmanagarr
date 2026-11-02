import { CalendarApplication, CalendarEntry } from './calendar-entry';

export interface CalendarInstanceStatus {
  readonly instanceId: string;
  readonly name: string;
  readonly application: CalendarApplication;
  readonly loaded: boolean;
  readonly error: string | null;
}

export interface CalendarResponse {
  readonly fetchedAt: string;
  readonly entries: readonly CalendarEntry[];
  readonly instances: readonly CalendarInstanceStatus[];
}
