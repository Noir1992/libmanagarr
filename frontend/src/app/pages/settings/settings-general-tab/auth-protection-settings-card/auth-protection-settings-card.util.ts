import { FormControl, ValidationErrors } from '@angular/forms';
import { AuthProtectionSettings } from '../../../../interfaces/settings/auth-protection-settings';

export type AuthProtectionField = 'requestsPerMinute' | 'maxFailedAttempts';

export function resolveAuthProtectionFieldFromPath(fieldPath: string): AuthProtectionField | null {
  if (fieldPath.endsWith('.requestsPerMinute')) {
    return 'requestsPerMinute';
  }

  if (fieldPath.endsWith('.maxFailedAttempts')) {
    return 'maxFailedAttempts';
  }

  return null;
}

export function setAuthProtectionFieldValidationError(control: FormControl<number>, message: string): void {
  const currentErrors: ValidationErrors = control.errors ?? {};
  control.setErrors({
    ...currentErrors,
    server: [message],
  });
}

export function clearAuthProtectionFieldValidationError(control: FormControl<number>): void {
  const errors: ValidationErrors | null = control.errors;
  if (!errors?.['server']) {
    return;
  }

  const nextErrors: ValidationErrors = { ...errors };
  delete nextErrors['server'];
  control.setErrors(Object.keys(nextErrors).length > 0 ? nextErrors : null);
}

export function clearAuthProtectionFieldValidationErrors(controls: Record<AuthProtectionField, FormControl<number>>): void {
  for (const field of ['requestsPerMinute', 'maxFailedAttempts'] as const) {
    clearAuthProtectionFieldValidationError(controls[field]);
  }
}

export function applyAuthProtectionValidationProblemErrors(
  controls: Record<AuthProtectionField, FormControl<number>>,
  fieldErrors: Record<string, string[]>,
): Array<{ field: AuthProtectionField; message: string }> {
  const appliedErrors: Array<{ field: AuthProtectionField; message: string }> = [];

  for (const [fieldPath, messages] of Object.entries(fieldErrors)) {
    const field: AuthProtectionField | null = resolveAuthProtectionFieldFromPath(fieldPath);
    if (!field) {
      continue;
    }

    const message: string | undefined = messages.find((value) => value.trim().length > 0);
    if (!message) {
      continue;
    }

    setAuthProtectionFieldValidationError(controls[field], message);
    appliedErrors.push({ field, message });
  }

  return appliedErrors;
}

export function syncSavedAuthProtectionField(
  controls: Record<AuthProtectionField, FormControl<number>>,
  field: AuthProtectionField,
  authProtection: AuthProtectionSettings,
): void {
  switch (field) {
    case 'requestsPerMinute':
      controls.requestsPerMinute.setValue(authProtection.requestsPerMinute, { emitEvent: false });
      controls.requestsPerMinute.markAsPristine();
      return;
    default:
      controls.maxFailedAttempts.setValue(authProtection.maxFailedAttempts, { emitEvent: false });
      controls.maxFailedAttempts.markAsPristine();
  }
}

export function buildAuthProtectionPayload(
  savedAuthProtection: AuthProtectionSettings,
  currentAuthProtection: AuthProtectionSettings,
  field: AuthProtectionField,
): AuthProtectionSettings {
  switch (field) {
    case 'requestsPerMinute':
      return {
        ...savedAuthProtection,
        requestsPerMinute: currentAuthProtection.requestsPerMinute,
      };
    default:
      return {
        ...savedAuthProtection,
        maxFailedAttempts: currentAuthProtection.maxFailedAttempts,
      };
  }
}

export function hasAuthProtectionFieldChanges(
  savedAuthProtection: AuthProtectionSettings,
  currentAuthProtection: AuthProtectionSettings,
  field: AuthProtectionField,
): boolean {
  switch (field) {
    case 'requestsPerMinute':
      return savedAuthProtection.requestsPerMinute !== currentAuthProtection.requestsPerMinute;
    default:
      return savedAuthProtection.maxFailedAttempts !== currentAuthProtection.maxFailedAttempts;
  }
}
