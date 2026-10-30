import { FormControl, FormGroup } from '@angular/forms';

export interface SeerrInstanceControls {
  clientKey: FormControl<string>;
  id: FormControl<string>;
  baseUrl: FormControl<string>;
  apiKey: FormControl<string>;
  testSucceeded: FormControl<boolean>;
  testMessage: FormControl<string>;
  lastTestedFingerprint: FormControl<string>;
}

export type SeerrInstanceFormGroup = FormGroup<SeerrInstanceControls>;
