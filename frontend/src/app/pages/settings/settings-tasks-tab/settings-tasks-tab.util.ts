import { ScheduledTaskRow } from '../../../interfaces/settings/scheduled-task-row';
import { ScheduledTasks } from '../../../interfaces/settings/scheduled-tasks';
import { SettingsIntervalUnit } from '../../../interfaces/settings/settings-interval-unit';
import { TranslationKey } from '../../../services/i18n/_i18n-key';

type TranslateFn = (key: TranslationKey, params?: Record<string, string>) => string;

const TASK_NAME_KEYS: Readonly<Record<string, TranslationKey>> = {
  arrDataSync: 'settings.tasks.dataSync.title',
  cleanup: 'settings.tasks.cleanup.title',
  releaseCheck: 'settings.tasks.releaseCheck.title',
  symlinkHealthCheck: 'settings.tasks.healthCheck.title',
};

export function buildTaskRows(scheduledTasks: ScheduledTasks, translate: TranslateFn): ScheduledTaskRow[] {
  // The table is read by name, so rows arrive in alphabetical order of the displayed name.
  return scheduledTasks
    .map((task) => ({
      ...task,
      name: TASK_NAME_KEYS[task.taskKey]
        ? translate(TASK_NAME_KEYS[task.taskKey])
        : task.taskKey.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/[-_]/g, ' '),
    }))
    .sort((left, right) => left.name.localeCompare(right.name, undefined, { sensitivity: 'base' }));
}

export function describeTaskRunSchedule(task: ScheduledTaskRow, translate: TranslateFn): string {
  switch (task.scheduleMode) {
    case 'DailyAtTime':
      if (task.timeHour === 0 && task.timeMinute === 0) {
        return translate('settings.tasks.scheduleInfo.dailyAtMidnight');
      }
      return translate('settings.tasks.scheduleInfo.dailyAtTime', {
        time: formatTime(task.timeHour, task.timeMinute),
      });
    case 'EveryNDaysAtTime':
      return translate('settings.tasks.scheduleInfo.everyNDaysAtTime', {
        days: task.everyNDays.toString(),
        time: formatTime(task.timeHour, task.timeMinute),
      });
    case 'Interval':
    default:
      return translate('settings.tasks.scheduleInfo.interval', {
        value: task.intervalValue.toString(),
        unit: translate(intervalUnitKey(task.intervalUnit)),
      });
  }
}

export function formatTime(hour: number, minute: number): string {
  return `${hour.toString().padStart(2, '0')}:${minute.toString().padStart(2, '0')}`;
}

export function intervalUnitKey(unit: SettingsIntervalUnit): TranslationKey {
  switch (unit) {
    case 'Minute':
      return 'settings.tasks.intervalUnit.minute';
    case 'Hour':
      return 'settings.tasks.intervalUnit.hour';
    case 'Day':
      return 'settings.tasks.intervalUnit.day';
    case 'Week':
      return 'settings.tasks.intervalUnit.week';
    case 'Month':
      return 'settings.tasks.intervalUnit.month';
    case 'Year':
      return 'settings.tasks.intervalUnit.year';
  }
}
