import { FormControl, ValidationErrors } from '@angular/forms';
import { ArrInstanceSettings } from '../../../../interfaces/settings/arr-instance-settings';

export type InstanceField = 'name' | 'baseUrl' | 'apiKey';

/**
 * Structural view over any instance form group exposing the server-validated
 * fields, so the mapping helpers work for both Arr and Seerr form groups.
 * Seerr has no instance name, hence the optional name control.
 */
export interface InstanceValidationControls {
  controls: {
    name?: FormControl<string>;
    baseUrl: FormControl<string>;
    apiKey: FormControl<string>;
  };
}

export function buildInstanceFingerprint(instance: { name?: string; baseUrl: string; apiKey: string }): string {
  return JSON.stringify({
    name: instance.name,
    baseUrl: instance.baseUrl,
    apiKey: instance.apiKey,
  });
}

export function mapInstanceToSettings(instance: { id: string; name: string; baseUrl: string; apiKey: string }): ArrInstanceSettings {
  return {
    id: instance.id,
    name: instance.name,
    baseUrl: instance.baseUrl,
    apiKey: instance.apiKey,
  };
}

export function clearServerValidationErrors(instanceGroup: InstanceValidationControls): void {
  const controls: (FormControl<string> | undefined)[] = [
    instanceGroup.controls.name,
    instanceGroup.controls.baseUrl,
    instanceGroup.controls.apiKey,
  ];
  for (const control of controls) {
    if (!control) {
      continue;
    }

    const errors: ValidationErrors | null = control.errors;
    if (!errors?.['server']) {
      continue;
    }

    const nextErrors: ValidationErrors = { ...errors };
    delete nextErrors['server'];
    control.setErrors(Object.keys(nextErrors).length > 0 ? nextErrors : null);
  }
}

export function applyValidationProblemErrors(instanceGroup: InstanceValidationControls, fieldErrors: Record<string, string[]>): void {
  const fieldMap: Partial<Record<InstanceField, FormControl<string>>> = {
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

    const control: FormControl<string> | undefined = fieldMap[fieldName];
    if (!control) {
      continue;
    }

    const message: string | undefined = messages.find((value) => value.trim().length > 0);
    if (!message) {
      continue;
    }

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
