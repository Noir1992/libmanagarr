import { FormControl, ValidationErrors } from '@angular/forms';
import { LoggingSettings } from '../../../../interfaces/settings/logging-settings';

export type LoggingField = 'retentionDays' | 'minimumLevel';

export function resolveLoggingFieldFromPath(fieldPath: string): LoggingField | null {
  if (fieldPath.endsWith('.retentionDays')) {
    return 'retentionDays';
  }

  if (fieldPath.endsWith('.minimumLevel')) {
    return 'minimumLevel';
  }

  return null;
}

export function setLoggingFieldValidationError(control: FormControl<string | number>, message: string): void {
  const currentErrors: ValidationErrors = control.errors ?? {};
  control.setErrors({
    ...currentErrors,
    server: [message],
  });
}

export function clearLoggingFieldValidationError(control: FormControl<string | number>): void {
  const errors: ValidationErrors | null = control.errors;
  if (!errors?.['server']) {
    return;
  }

  const nextErrors: ValidationErrors = { ...errors };
  delete nextErrors['server'];
  control.setErrors(Object.keys(nextErrors).length > 0 ? nextErrors : null);
}

export function clearLoggingFieldValidationErrors(controls: Record<LoggingField, FormControl<string | number>>): void {
  for (const field of ['retentionDays', 'minimumLevel'] as const) {
    clearLoggingFieldValidationError(controls[field]);
  }
}

export function applyValidationProblemErrors(
  controls: Record<LoggingField, FormControl<string | number>>,
  fieldErrors: Record<string, string[]>,
): Array<{ field: LoggingField; message: string }> {
  const appliedErrors: Array<{ field: LoggingField; message: string }> = [];

  for (const [fieldPath, messages] of Object.entries(fieldErrors)) {
    const field: LoggingField | null = resolveLoggingFieldFromPath(fieldPath);
    if (!field) {
      continue;
    }

    const message: string | undefined = messages.find((value) => value.trim().length > 0);
    if (!message) {
      continue;
    }

    setLoggingFieldValidationError(controls[field], message);
    appliedErrors.push({ field, message });
  }

  return appliedErrors;
}

export function syncSavedLoggingField(
  controls: Record<LoggingField, FormControl<string | number>>,
  field: LoggingField,
  logging: LoggingSettings,
): void {
  switch (field) {
    case 'retentionDays':
      controls.retentionDays.setValue(logging.retentionDays, { emitEvent: false });
      controls.retentionDays.markAsPristine();
      return;
    default:
      controls.minimumLevel.setValue(logging.minimumLevel, { emitEvent: false });
      controls.minimumLevel.markAsPristine();
  }
}

export function buildLoggingSettingsPayload(
  savedLogging: LoggingSettings,
  currentLogging: LoggingSettings,
  field: LoggingField,
): LoggingSettings {
  switch (field) {
    case 'retentionDays':
      return {
        ...savedLogging,
        retentionDays: currentLogging.retentionDays,
      };
    default:
      return {
        ...savedLogging,
        minimumLevel: currentLogging.minimumLevel,
      };
  }
}

export function hasLoggingFieldChanges(savedLogging: LoggingSettings, currentLogging: LoggingSettings, field: LoggingField): boolean {
  switch (field) {
    case 'retentionDays':
      return savedLogging.retentionDays !== currentLogging.retentionDays;
    default:
      return savedLogging.minimumLevel !== currentLogging.minimumLevel;
  }
}
