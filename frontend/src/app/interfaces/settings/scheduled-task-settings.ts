import { SettingsIntervalUnit } from './settings-interval-unit';
import { SettingsScheduleMode } from './settings-schedule-mode';

export interface ScheduledTaskSettings {
  taskKey: string;
  revision: number;
  canRunManually: boolean;
  enabled: boolean;
  readOnly: boolean;
  scheduleMode: SettingsScheduleMode;
  intervalUnit: SettingsIntervalUnit;
  intervalValue: number;
  timeHour: number;
  timeMinute: number;
  everyNDays: number;
  lastRunAt: string | Date | null;
  nextRunAt: string | Date | null;
}
