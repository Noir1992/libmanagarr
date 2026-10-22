import { FormControl, ValidationErrors } from '@angular/forms';
import { ArrInstanceFormGroup } from '../../../../interfaces/settings/arr-instance-form-group';
import { ArrInstanceSettings } from '../../../../interfaces/settings/arr-instance-settings';

export type InstanceField = 'name' | 'baseUrl' | 'apiKey';

export function buildInstanceFingerprint(instance: { name: string; baseUrl: string; apiKey: string }): string {
  return JSON.stringify({
    name: instance.name,
    baseUrl: instance.baseUrl,
    apiKey: instance.apiKey,
  });
}

export function mapInstanceToSettings(instance: {
  clientKey: string;
  id: string;
  name: string;
  baseUrl: string;
  apiKey: string;
}): ArrInstanceSettings {
  return {
    id: instance.id,
    name: instance.name,
    baseUrl: instance.baseUrl,
    apiKey: instance.apiKey,
  };
}

export function clearServerValidationErrors(instanceGroup: ArrInstanceFormGroup): void {
  for (const control of [instanceGroup.controls.name, instanceGroup.controls.baseUrl, instanceGroup.controls.apiKey]) {
    const errors: ValidationErrors | null = control.errors;
    if (!errors?.['server']) {
      continue;
    }

    const nextErrors: ValidationErrors = { ...errors };
    delete nextErrors['server'];
    control.setErrors(Object.keys(nextErrors).length > 0 ? nextErrors : null);
  }
}

export function applyValidationProblemErrors(instanceGroup: ArrInstanceFormGroup, fieldErrors: Record<string, string[]>): void {
  const fieldMap: Record<InstanceField, FormControl<string>> = {
    name: instanceGroup.controls.name,
    baseUrl: instanceGroup.controls.baseUrl,
    apiKey: instanceGroup.controls.apiKey,
  };

  clearServerValidationErrors(instanceGroup);

  for (const [fieldPath, messages] of Object.entries(fieldErrors)) {
    const fieldName: InstanceField | null = resolveInstanceFieldName(fieldPath);
    if (!fieldName) {
      continue;
    }

    const message: string | undefined = messages.find((value) => value.trim().length > 0);
    if (!message) {
      continue;
    }

    const control: FormControl<string> = fieldMap[fieldName];
    const currentErrors: ValidationErrors = control.errors ?? {};
    control.setErrors({
      ...currentErrors,
      server: [message],
    });
  }
}

export function resolveInstanceFieldName(fieldPath: string): InstanceField | null {
  if (fieldPath.endsWith('.name')) {
    return 'name';
  }

  if (fieldPath.endsWith('.baseUrl')) {
    return 'baseUrl';
  }

  if (fieldPath.endsWith('.apiKey')) {
    return 'apiKey';
  }

  return null;
}
