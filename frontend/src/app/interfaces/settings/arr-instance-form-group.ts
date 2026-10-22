import { FormControl, FormGroup } from '@angular/forms';

export interface ArrInstanceControls {
  clientKey: FormControl<string>;
  id: FormControl<string>;
  name: FormControl<string>;
  baseUrl: FormControl<string>;
  apiKey: FormControl<string>;
  testSucceeded: FormControl<boolean>;
  testMessage: FormControl<string>;
  lastTestedFingerprint: FormControl<string>;
  syncMessage: FormControl<string>;
}

export type ArrInstanceFormGroup = FormGroup<ArrInstanceControls>;
