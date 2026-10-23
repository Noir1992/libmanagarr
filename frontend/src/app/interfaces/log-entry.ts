import { AppLogLevel } from './general/app-log-level';

export interface LogEntry {
  timestamp: string;
  level: AppLogLevel;
  source: string;
  message: string;
}
