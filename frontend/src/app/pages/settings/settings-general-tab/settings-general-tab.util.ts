import { SizeUnitSystem } from '../../../interfaces/general/size-unit-system';
import { FormControl, ValidationErrors } from '@angular/forms';
import { AppLanguage } from '../../../interfaces/general/app-language';
import { AppTheme } from '../../../interfaces/general/app-theme';

export type GeneralField = 'language' | 'theme' | 'sizeUnitSystem';

export function resolveGeneralFieldFromPath(fieldPath: string): GeneralField | null {
  if (fieldPath.endsWith('.language')) {
    return 'language';
  }

  if (fieldPath.endsWith('.theme')) {
    return 'theme';
  }

  if (fieldPath === 'sizeUnitSystem' || fieldPath.endsWith('.sizeUnitSystem')) {
    return 'sizeUnitSystem';
  }

  return null;
}

export function setGeneralFieldValidationError(control: FormControl<AppLanguage | AppTheme | SizeUnitSystem>, message: string): void {
  const currentErrors: ValidationErrors = control.errors ?? {};
  control.setErrors({
    ...currentErrors,
    server: [message],
  });
}

export function clearGeneralFieldValidationError(control: FormControl<AppLanguage | AppTheme | SizeUnitSystem>): void {
  const errors: ValidationErrors | null = control.errors;
  if (!errors?.['server']) {
    return;
  }

  const nextErrors: ValidationErrors = { ...errors };
  delete nextErrors['server'];
  control.setErrors(Object.keys(nextErrors).length > 0 ? nextErrors : null);
}

export function clearGeneralFieldValidationErrors(
  controls: Record<GeneralField, FormControl<AppLanguage | AppTheme | SizeUnitSystem>>,
): void {
  clearGeneralFieldValidationError(controls.language);
  clearGeneralFieldValidationError(controls.theme);
  clearGeneralFieldValidationError(controls.sizeUnitSystem);
}

export function applyGeneralValidationProblemErrors(
  controls: Record<GeneralField, FormControl<AppLanguage | AppTheme | SizeUnitSystem>>,
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
