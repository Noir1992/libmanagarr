import {
  Component,
  computed,
  effect,
  inject,
  input,
  InputSignal,
  OnDestroy,
  output,
  OutputEmitterRef,
  Signal,
  signal,
  WritableSignal,
  ChangeDetectionStrategy,
} from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { Subscription } from 'rxjs';
import { ARR_APPLICATION_KINDS } from '../../../../interfaces/general/arr-application-kind';
import { FeedbackState } from '../../../../interfaces/general/feedback-state';
import { SeerrInstanceFormGroup } from '../../../../interfaces/settings/seerr-instance-form-group';
import { SeerrInstanceSettings } from '../../../../interfaces/settings/seerr-instance-settings';
import { AppI18nService } from '../../../../services/app-i18n.service';
import { AppSettingsService, ValidationProblemError } from '../../../../services/app-settings.service';
import { FeedbackIconWidget } from '../../../../widgets/feedback-icon/feedback-icon.widget';
import { InfoIconWidget } from '../../../../widgets/info-icon/info-icon.widget';
import {
  applyValidationProblemErrors,
  buildInstanceFingerprint,
  clearServerValidationErrors,
} from '../settings-arr-instance-card/settings-arr-instance-card.util';

type SeerrConnectionField = 'baseUrl' | 'apiKey';

@Component({
  selector: 'app-settings-seerr-card',
  imports: [ReactiveFormsModule, MatButtonModule, MatInputModule, MatIconModule, FeedbackIconWidget, InfoIconWidget],
  templateUrl: './settings-seerr-card.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './settings-seerr-card.widget.scss',
})
export class SettingsSeerrCardWidget implements OnDestroy {
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);
  private readonly initialInstanceFingerprintState: WritableSignal<string> = signal<string>('');
  private connectionFeedbackTimeout?: ReturnType<typeof setTimeout>;
  private static nextFieldId: number = 0;

  readonly instanceGroup: InputSignal<SeerrInstanceFormGroup> = input.required<SeerrInstanceFormGroup>();
  readonly isConfigured: InputSignal<boolean> = input.required<boolean>();
  readonly isDeletingState: InputSignal<boolean> = input.required<boolean>();

  readonly removeInstance: OutputEmitterRef<void> = output<void>();

  readonly baseUrlFieldId: string = `seerr-instance-base-url-${SettingsSeerrCardWidget.nextFieldId++}`;
  readonly apiKeyFieldId: string = `seerr-instance-api-key-${SettingsSeerrCardWidget.nextFieldId++}`;

  readonly instanceUrlLabel: Signal<string> = computed((): string => this.i18n.t('settings.instanceUrl'));
  readonly instanceUrlInfoMessage: Signal<string> = computed((): string => this.i18n.t('settings.instanceUrl.info'));
  readonly instanceApiKeyLabel: Signal<string> = computed((): string => this.i18n.t('settings.instanceApiKey'));
  readonly instanceApiKeyInfoMessage: Signal<string> = computed((): string => this.i18n.t('settings.instanceApiKey.info'));
  readonly testConnectionLabel: Signal<string> = computed((): string => this.i18n.t('settings.testConnection'));
  readonly removeLabel: Signal<string> = computed((): string => this.i18n.t('settings.remove'));
  readonly connectionFeedbackMessage: Signal<string | null> = computed(
    (): string | null => this.connectionFeedbackMessageState() ?? this.connectionHintMessageState(),
  );
  protected readonly requiredFieldMessages: Signal<Record<SeerrConnectionField, string>> = computed(
    (): Record<SeerrConnectionField, string> => ({
      baseUrl: this.i18n.t('settings.tasks.validation.required'),
      apiKey: this.i18n.t('settings.instanceApiKeyRequired'),
    }),
  );
  protected readonly maxLengthFieldMessages: Signal<Record<SeerrConnectionField, string>> = computed(
    (): Record<SeerrConnectionField, string> => ({
      baseUrl: this.i18n.t('settings.instanceUrlTooLong'),
      apiKey: this.i18n.t('settings.instanceApiKeyTooLong'),
    }),
  );

  protected readonly isApiKeyVisibleState: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly isTestingState: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly connectionFeedbackState: WritableSignal<FeedbackState> = signal<FeedbackState>(null);
  protected readonly connectionFeedbackMessageState: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly connectionHintMessageState: WritableSignal<string | null> = signal<string | null>(null);

  constructor() {
    // The parent replaces the whole form group when it resyncs from the service, so the
    // subscriptions and the baseline fingerprint have to follow the current input value.
    effect(() => {
      const instanceGroup: SeerrInstanceFormGroup = this.instanceGroup();
      this.initialInstanceFingerprintState.set(buildInstanceFingerprint(instanceGroup.getRawValue()));

      const subscription: Subscription = new Subscription();
      subscription.add(instanceGroup.controls.baseUrl.valueChanges.subscribe(() => this.handleConnectionSensitiveChange()));
      subscription.add(instanceGroup.controls.apiKey.valueChanges.subscribe(() => this.handleConnectionSensitiveChange()));

      return (): void => subscription.unsubscribe();
    });
  }

  ngOnDestroy(): void {
    if (this.connectionFeedbackTimeout) {
      clearTimeout(this.connectionFeedbackTimeout);
      this.connectionFeedbackTimeout = undefined;
    }
  }

  protected toggleApiKeyVisibility(): void {
    this.isApiKeyVisibleState.update((current) => !current);
  }

  protected hasServerError(): boolean {
    const instanceGroup: SeerrInstanceFormGroup = this.instanceGroup();
    return [instanceGroup.controls.baseUrl, instanceGroup.controls.apiKey].some((control) => Boolean(control.errors?.['server']));
  }

  protected instanceFieldHasError(field: SeerrConnectionField): boolean {
    const control: FormControl<string> = this.instanceGroup().controls[field];
    if (control.errors?.['server']) {
      return true;
    }

    return control.invalid && (control.dirty || control.touched);
  }

  protected instanceFieldErrorMessage(field: SeerrConnectionField): string | null {
    const control: FormControl<string> = this.instanceGroup().controls[field];
    if (!this.instanceFieldHasError(field)) {
      return null;
    }

    const serverMessage: string | undefined = control.errors?.['server']?.find((value: string) => value.trim().length > 0);
    if (serverMessage !== undefined) {
      return serverMessage;
    }

    if (control.errors?.['required']) {
      return this.requiredFieldMessages()[field];
    }

    if (control.errors?.['maxlength']) {
      return this.maxLengthFieldMessages()[field];
    }

    return this.i18n.t('common.tableLoadError');
  }

  protected async testConnectionAsync(): Promise<void> {
    const instanceGroup: SeerrInstanceFormGroup = this.instanceGroup();
    this.connectionHintMessageState.set(null);
    clearServerValidationErrors(instanceGroup);
    if (instanceGroup.controls.baseUrl.invalid || instanceGroup.controls.apiKey.invalid) {
      if (this.hasServerError()) {
        return;
      }

      instanceGroup.controls.testSucceeded.setValue(false);
      instanceGroup.controls.testMessage.setValue(this.i18n.t('settings.connectionChanged'));
      this.connectionHintMessageState.set(this.i18n.t('settings.connectionChanged'));
      this.setConnectionFeedback('error', this.i18n.t('settings.connectionChanged'));
      return;
    }

    this.isTestingState.set(true);
    instanceGroup.controls.testMessage.setValue('');

    try {
      await this.appSettingsService.testConnection(ARR_APPLICATION_KINDS.SEERR, this.buildInstancePayload());
      const fingerprint: string = buildInstanceFingerprint(instanceGroup.getRawValue());

      instanceGroup.controls.testSucceeded.setValue(true);
      instanceGroup.controls.lastTestedFingerprint.setValue(fingerprint);
      instanceGroup.controls.testMessage.setValue('');
      this.connectionHintMessageState.set(null);
      await this.saveInstanceAsync();
      this.initialInstanceFingerprintState.set(fingerprint);
      this.setConnectionFeedback('success', null);
    } catch (error) {
      if (error instanceof ValidationProblemError) {
        applyValidationProblemErrors(instanceGroup, error.fieldErrors);
        return;
      }

      instanceGroup.controls.testSucceeded.setValue(false);
      instanceGroup.controls.lastTestedFingerprint.setValue('');
      const message: string = error instanceof Error ? error.message : this.i18n.t('common.tableLoadError');
      instanceGroup.controls.testMessage.setValue(message);
      this.connectionHintMessageState.set(null);
      this.setConnectionFeedback('error', message);
    } finally {
      this.isTestingState.set(false);
    }
  }

  private handleConnectionSensitiveChange(): void {
    const instanceGroup: SeerrInstanceFormGroup = this.instanceGroup();
    const currentFingerprint: string = buildInstanceFingerprint(instanceGroup.getRawValue());

    if (currentFingerprint === this.initialInstanceFingerprintState()) {
      return;
    }

    instanceGroup.controls.testSucceeded.setValue(false, { emitEvent: false });
    instanceGroup.controls.testMessage.setValue(this.i18n.t('settings.connectionChanged'), { emitEvent: false });
    this.connectionHintMessageState.set(this.i18n.t('settings.connectionChanged'));
    this.setConnectionFeedback(null, null);
  }

  private buildInstancePayload(): SeerrInstanceSettings {
    const instanceGroup: SeerrInstanceFormGroup = this.instanceGroup();
    return {
      id: instanceGroup.controls.id.getRawValue(),
      baseUrl: instanceGroup.controls.baseUrl.getRawValue(),
      apiKey: instanceGroup.controls.apiKey.getRawValue(),
    };
  }

  private async saveInstanceAsync(): Promise<void> {
    const instanceGroup: SeerrInstanceFormGroup = this.instanceGroup();
    const payload: SeerrInstanceSettings = this.buildInstancePayload();

    try {
      await this.appSettingsService.saveSeerrInstance(payload);
      this.syncInstanceGroupFromSavedSettings();
    } catch (error) {
      if (error instanceof ValidationProblemError) {
        applyValidationProblemErrors(instanceGroup, error.fieldErrors);
      }

      throw error;
    }
  }

  private setConnectionFeedback(feedback: FeedbackState, message: string | null): void {
    if (this.connectionFeedbackTimeout) {
      clearTimeout(this.connectionFeedbackTimeout);
      this.connectionFeedbackTimeout = undefined;
    }

    this.connectionFeedbackState.set(feedback);
    this.connectionFeedbackMessageState.set(message);

    if (feedback === 'loading' || feedback === null) {
      return;
    }

    this.connectionFeedbackTimeout = setTimeout(() => {
      this.connectionFeedbackState.set(null);
      this.connectionFeedbackMessageState.set(null);
      this.connectionFeedbackTimeout = undefined;
    }, 3000);
  }

  private syncInstanceGroupFromSavedSettings(): void {
    const instanceGroup: SeerrInstanceFormGroup = this.instanceGroup();
    const savedInstance: SeerrInstanceSettings | null = this.appSettingsService.seerrInstance();
    if (savedInstance === null) {
      return;
    }

    const fingerprint: string = buildInstanceFingerprint(savedInstance);
    instanceGroup.patchValue(
      {
        id: savedInstance.id,
        baseUrl: savedInstance.baseUrl,
        apiKey: savedInstance.apiKey,
        testSucceeded: true,
        testMessage: '',
        lastTestedFingerprint: fingerprint,
      },
      { emitEvent: false },
    );

    instanceGroup.markAsPristine();
    for (const control of [
      instanceGroup.controls.id,
      instanceGroup.controls.baseUrl,
      instanceGroup.controls.apiKey,
      instanceGroup.controls.testSucceeded,
      instanceGroup.controls.testMessage,
      instanceGroup.controls.lastTestedFingerprint,
    ]) {
      control.markAsPristine();
    }

    this.initialInstanceFingerprintState.set(fingerprint);
  }
}
