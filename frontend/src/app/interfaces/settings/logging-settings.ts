import { AppLogLevel } from '../general/app-log-level';

export interface LoggingSettings {
  path: string;
  retentionDays: number;
  minimumLevel: AppLogLevel;
}
