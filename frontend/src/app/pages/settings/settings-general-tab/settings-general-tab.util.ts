import { FormControl, ValidationErrors } from '@angular/forms';
import { AppLanguage } from '../../../interfaces/general/app-language';
import { AppTheme } from '../../../interfaces/general/app-theme';

export type GeneralField = 'language' | 'theme';

export function resolveGeneralFieldFromPath(fieldPath: string): GeneralField | null {
  if (fieldPath.endsWith('.language')) {
    return 'language';
  }

  if (fieldPath.endsWith('.theme')) {
    return 'theme';
  }

  return null;
}

export function setGeneralFieldValidationError(control: FormControl<AppLanguage | AppTheme>, message: string): void {
  const currentErrors: ValidationErrors = control.errors ?? {};
  control.setErrors({
    ...currentErrors,
    server: [message],
  });
}

export function clearGeneralFieldValidationError(control: FormControl<AppLanguage | AppTheme>): void {
  const errors: ValidationErrors | null = control.errors;
  if (!errors?.['server']) {
    return;
  }

  const nextErrors: ValidationErrors = { ...errors };
  delete nextErrors['server'];
  control.setErrors(Object.keys(nextErrors).length > 0 ? nextErrors : null);
}

export function clearGeneralFieldValidationErrors(controls: Record<GeneralField, FormControl<AppLanguage | AppTheme>>): void {
  clearGeneralFieldValidationError(controls.language);
  clearGeneralFieldValidationError(controls.theme);
}

export function applyGeneralValidationProblemErrors(
  controls: Record<GeneralField, FormControl<AppLanguage | AppTheme>>,
  fieldErrors: Record<string, string[]>,
): Array<{ field: GeneralField; message: string }> {
  const appliedErrors: Array<{ field: GeneralField; message: string }> = [];

  for (const [fieldPath, messages] of Object.entries(fieldErrors)) {
    const field: GeneralField | null = resolveGeneralFieldFromPath(fieldPath);
    if (!field) {
      continue;
    }

    const message: string | undefined = messages.find((value) => value.trim().length > 0);
    if (!message) {
      continue;
    }

    setGeneralFieldValidationError(controls[field], message);
    appliedErrors.push({ field, message });
  }

  return appliedErrors;
}
