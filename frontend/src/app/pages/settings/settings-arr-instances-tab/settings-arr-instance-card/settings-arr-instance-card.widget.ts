import {
  Component,
  computed,
  inject,
  input,
  InputSignal,
  OnDestroy,
  OnInit,
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
import { ARR_APPLICATION_KINDS, ArrApplicationKind } from '../../../../interfaces/general/arr-application-kind';
import { FeedbackState } from '../../../../interfaces/general/feedback-state';
import { ArrInstanceFormGroup } from '../../../../interfaces/settings/arr-instance-form-group';
import { ArrInstanceSettings } from '../../../../interfaces/settings/arr-instance-settings';
import { AppI18nService } from '../../../../services/app-i18n.service';
import { AppSettingsService, ValidationProblemError } from '../../../../services/app-settings.service';
import { FeedbackIconWidget } from '../../../../widgets/feedback-icon/feedback-icon.widget';
import { InfoIconWidget } from '../../../../widgets/info-icon/info-icon.widget';
import {
  applyValidationProblemErrors,
  buildInstanceFingerprint,
  clearServerValidationErrors,
  mapInstanceToSettings,
  type InstanceField,
} from './settings-arr-instance-card.util';

@Component({
  selector: 'app-settings-arr-instance-card',
  imports: [ReactiveFormsModule, MatButtonModule, MatInputModule, MatIconModule, FeedbackIconWidget, InfoIconWidget],
  templateUrl: './settings-arr-instance-card.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './settings-arr-instance-card.widget.scss',
})
export class SettingsArrInstanceCardWidget implements OnInit, OnDestroy {
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);
  private readonly initialInstanceFingerprintState: WritableSignal<string> = signal<string>('');
  private readonly subscriptions: Subscription = new Subscription();
  private readonly feedbackTimeouts: Partial<Record<'connection' | 'name' | 'sync', ReturnType<typeof setTimeout>>> = {};
  private static nextFieldId: number = 0;

  readonly instanceGroup: InputSignal<ArrInstanceFormGroup> = input.required<ArrInstanceFormGroup>();
  readonly instanceKind: InputSignal<ArrApplicationKind> = input.required<ArrApplicationKind>();
  readonly instanceIndex: InputSignal<number> = input.required<number>();
  readonly isDeletingState: InputSignal<boolean> = input.required<boolean>();

  readonly instanceSaved: OutputEmitterRef<void> = output<void>();
  readonly removeInstance: OutputEmitterRef<void> = output<void>();

  readonly nameFieldId: string = `arr-instance-name-${SettingsArrInstanceCardWidget.nextFieldId++}`;
  readonly baseUrlFieldId: string = `arr-instance-base-url-${SettingsArrInstanceCardWidget.nextFieldId++}`;
  readonly apiKeyFieldId: string = `arr-instance-api-key-${SettingsArrInstanceCardWidget.nextFieldId++}`;

  readonly instanceNameLabel: Signal<string> = computed((): string => this.i18n.t('settings.instanceName'));
  readonly instanceUrlLabel: Signal<string> = computed((): string => this.i18n.t('settings.instanceUrl'));
  readonly instanceUrlInfoMessage: Signal<string> = computed((): string => this.i18n.t('settings.instanceUrl.info'));
  readonly instanceApiKeyLabel: Signal<string> = computed((): string => this.i18n.t('settings.instanceApiKey'));
  readonly instanceApiKeyInfoMessage: Signal<string> = computed((): string => this.i18n.t('settings.instanceApiKey.info'));
  readonly testConnectionLabel: Signal<string> = computed((): string => this.i18n.t('settings.testConnection'));
  readonly syncLabel: Signal<string> = computed((): string => this.i18n.t('settings.sync'));
  readonly removeLabel: Signal<string> = computed((): string => this.i18n.t('settings.remove'));
  readonly isSyncingThisInstance: Signal<boolean> = computed((): boolean => {
    const instanceId: string = this.instanceGroup().controls.id.getRawValue().trim();
    return this.appSettingsService.isSyncing(this.instanceKind(), instanceId);
  });
  readonly connectionFeedbackMessage: Signal<string | null> = computed(
    (): string | null => this.connectionFeedbackMessageState() ?? this.connectionHintMessageState(),
  );
  protected readonly requiredFieldMessages: Signal<Record<InstanceField, string>> = computed((): Record<InstanceField, string> => ({
    name: this.i18n.t('settings.instanceNameRequired'),
    baseUrl: this.i18n.t('settings.tasks.validation.required'),
    apiKey: this.i18n.t('settings.instanceApiKeyRequired'),
  }));
  protected readonly maxLengthFieldMessages: Signal<Record<InstanceField, string>> = computed((): Record<InstanceField, string> => ({
    name: this.i18n.t('settings.instanceNameTooLong'),
    baseUrl: this.i18n.t('settings.instanceUrlTooLong'),
    apiKey: this.i18n.t('settings.instanceApiKeyTooLong'),
  }));

  protected readonly isApiKeyVisibleState: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly isNameSavingState: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly isTestingState: WritableSignal<boolean> = signal<boolean>(false);
  protected readonly connectionFeedbackState: WritableSignal<FeedbackState> = signal<FeedbackState>(null);
  protected readonly connectionFeedbackMessageState: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly connectionHintMessageState: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly nameFeedbackState: WritableSignal<FeedbackState> = signal<FeedbackState>(null);
  protected readonly nameFeedbackMessageState: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly syncFeedbackState: WritableSignal<FeedbackState> = signal<FeedbackState>(null);
  protected readonly syncFeedbackMessageState: WritableSignal<string | null> = signal<string | null>(null);

  ngOnInit(): void {
    const instanceGroup: ArrInstanceFormGroup = this.instanceGroup();
    this.initialInstanceFingerprintState.set(buildInstanceFingerprint(instanceGroup.getRawValue()));

    this.subscriptions.add(instanceGroup.controls.name.valueChanges.subscribe(() => this.handleNameChange()));
    this.subscriptions.add(instanceGroup.controls.baseUrl.valueChanges.subscribe(() => this.handleConnectionSensitiveChange()));
    this.subscriptions.add(instanceGroup.controls.apiKey.valueChanges.subscribe(() => this.handleConnectionSensitiveChange()));
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
    for (const timeout of Object.values(this.feedbackTimeouts)) {
      if (timeout) {
        clearTimeout(timeout);
      }
    }
  }

  protected toggleApiKeyVisibility(): void {
    this.isApiKeyVisibleState.update((current) => !current);
  }

  protected canSync(): boolean {
    const instanceGroup: ArrInstanceFormGroup = this.instanceGroup();
    const isPersisted: boolean = instanceGroup.controls.lastTestedFingerprint.getRawValue().trim().length > 0;
    const currentFingerprint: string = buildInstanceFingerprint(instanceGroup.getRawValue());
    const requiresRetest: boolean = currentFingerprint !== this.initialInstanceFingerprintState();

    return (
      isPersisted &&
      instanceGroup.controls.testSucceeded.getRawValue() &&
      !requiresRetest &&
      !this.isTestingState() &&
      !this.isNameSavingState() &&
      !this.isSyncingThisInstance()
    );
  }

  protected hasServerError(): boolean {
    const instanceGroup: ArrInstanceFormGroup = this.instanceGroup();
    return [instanceGroup.controls.name, instanceGroup.controls.baseUrl, instanceGroup.controls.apiKey].some((control) =>
      Boolean(control.errors?.['server']),
    );
  }

  protected instanceFieldHasError(field: InstanceField): boolean {
    const control: FormControl<string> = this.instanceGroup().controls[field];
    if (control.errors?.['server']) {
      return true;
    }

    return control.invalid && (control.dirty || control.touched);
  }

  protected instanceFieldErrorMessage(field: InstanceField): string | null {
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
    const instanceGroup: ArrInstanceFormGroup = this.instanceGroup();
    this.connectionHintMessageState.set(null);
    clearServerValidationErrors(instanceGroup);
    if (instanceGroup.controls.name.invalid || instanceGroup.controls.baseUrl.invalid || instanceGroup.controls.apiKey.invalid) {
      if (this.hasServerError()) {
        return;
      }

      instanceGroup.controls.testSucceeded.setValue(false);
      instanceGroup.controls.testMessage.setValue(this.i18n.t('settings.connectionChanged'));
      this.connectionHintMessageState.set(this.i18n.t('settings.connectionChanged'));
      this.setConnectionFeedback('error', this.i18n.t('settings.connectionChanged'));
      return;
    }

    this.setTestingState(true);
    instanceGroup.controls.testMessage.setValue('');

    try {
      await this.appSettingsService.testConnection(this.instanceKind(), mapInstanceToSettings(instanceGroup.getRawValue()));
      const fingerprint: string = buildInstanceFingerprint(instanceGroup.getRawValue());

      instanceGroup.controls.testSucceeded.setValue(true);
      instanceGroup.controls.lastTestedFingerprint.setValue(fingerprint);
      instanceGroup.controls.testMessage.setValue('');
      this.connectionHintMessageState.set(null);
      await this.saveInstanceAsync();
      this.initialInstanceFingerprintState.set(fingerprint);
      this.setConnectionFeedback('success', null);
      this.instanceSaved.emit();
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
      this.setTestingState(false);
    }
  }

  protected async saveNameAsync(): Promise<void> {
    if (!this.canAutoSaveName()) {
      return;
    }

    this.isNameSavingState.set(true);

    try {
      await this.saveInstanceAsync();
      this.connectionHintMessageState.set(null);
      this.setConnectionFeedback(null, null);
      this.setNameFeedback('success', null);
    } catch (error) {
      if (error instanceof ValidationProblemError) {
        applyValidationProblemErrors(this.instanceGroup(), error.fieldErrors);
        this.setNameFeedback('error', error.message.trim().length > 0 ? error.message.trim() : this.i18n.t('settings.saveError'));
        return;
      }

      this.setNameFeedback('error', this.i18n.t('settings.saveError'));
    } finally {
      this.isNameSavingState.set(false);
    }
  }

  protected async syncInstanceAsync(): Promise<void> {
    const instanceGroup: ArrInstanceFormGroup = this.instanceGroup();
    const instanceId: string = instanceGroup.controls.id.getRawValue();

    if (!this.canSync()) {
      instanceGroup.controls.syncMessage.setValue(this.i18n.t('settings.syncError'));
      this.setSyncFeedback('error', this.i18n.t('settings.syncError'));
      return;
    }
    instanceGroup.controls.syncMessage.setValue('');

    try {
      await this.appSettingsService.syncInstance(this.instanceKind(), instanceId);
      instanceGroup.controls.syncMessage.setValue('');
      this.setSyncFeedback('success', null);
    } catch (error) {
      const message: string = error instanceof Error ? error.message : this.i18n.t('settings.syncError');
      instanceGroup.controls.syncMessage.setValue(message);
      this.setSyncFeedback('error', message);
    }
  }

  private handleNameChange(): void {
    this.setNameFeedback(null, null);
  }

  private handleConnectionSensitiveChange(): void {
    const instanceGroup: ArrInstanceFormGroup = this.instanceGroup();
    const currentFingerprint: string = buildInstanceFingerprint(instanceGroup.getRawValue());

    if (currentFingerprint === this.initialInstanceFingerprintState()) {
      return;
    }

    instanceGroup.controls.testSucceeded.setValue(false, { emitEvent: false });
    instanceGroup.controls.testMessage.setValue(this.i18n.t('settings.connectionChanged'), { emitEvent: false });
    this.connectionHintMessageState.set(this.i18n.t('settings.connectionChanged'));
    this.setConnectionFeedback(null, null);
    this.setSyncFeedback(null, null);
    this.setNameFeedback(null, null);
  }

  private async saveInstanceAsync(): Promise<void> {
    const instanceGroup: ArrInstanceFormGroup = this.instanceGroup();
    const payload: ArrInstanceSettings = mapInstanceToSettings(instanceGroup.getRawValue());

    try {
      if (this.instanceKind() === ARR_APPLICATION_KINDS.SONARR) {
        await this.appSettingsService.saveSonarrInstance(this.instanceIndex(), payload);
      } else {
        await this.appSettingsService.saveRadarrInstance(this.instanceIndex(), payload);
      }

      this.syncInstanceGroupFromSavedSettings();
    } catch (error) {
      if (error instanceof ValidationProblemError) {
        applyValidationProblemErrors(instanceGroup, error.fieldErrors);
        throw error;
      }

      throw error;
    }
  }

  private setTestingState(isTesting: boolean): void {
    this.isTestingState.set(isTesting);
  }

  private canAutoSaveName(): boolean {
    const instanceGroup: ArrInstanceFormGroup = this.instanceGroup();
    const savedInstance: ArrInstanceSettings | undefined = this.getSavedInstanceFromSettings();
    if (savedInstance === undefined) {
      return false;
    }

    if (this.isTestingState() || this.isSyncingThisInstance() || this.isNameSavingState()) {
      return false;
    }

    if (!instanceGroup.controls.name.dirty) {
      return false;
    }

    if (instanceGroup.controls.baseUrl.dirty || instanceGroup.controls.apiKey.dirty) {
      return false;
    }

    return instanceGroup.controls.name.valid;
  }

  private setNameFeedback(feedback: FeedbackState, message: string | null): void {
    const timeout: ReturnType<typeof setTimeout> | undefined = this.feedbackTimeouts.name;
    if (timeout) {
      clearTimeout(timeout);
      delete this.feedbackTimeouts.name;
    }

    this.nameFeedbackState.set(feedback);
    this.nameFeedbackMessageState.set(message);

    if (feedback === 'loading' || feedback === null) {
      return;
    }

    this.feedbackTimeouts.name = setTimeout(() => {
      this.nameFeedbackState.set(null);
      this.nameFeedbackMessageState.set(null);
      delete this.feedbackTimeouts.name;
    }, 3000);
  }

  private setConnectionFeedback(feedback: FeedbackState, message: string | null): void {
    const timeout: ReturnType<typeof setTimeout> | undefined = this.feedbackTimeouts.connection;
    if (timeout) {
      clearTimeout(timeout);
      delete this.feedbackTimeouts.connection;
    }

    this.connectionFeedbackState.set(feedback);
    this.connectionFeedbackMessageState.set(message);

    if (feedback === 'loading' || feedback === null) {
      return;
    }

    this.feedbackTimeouts.connection = setTimeout(() => {
      this.connectionFeedbackState.set(null);
      this.connectionFeedbackMessageState.set(null);
      delete this.feedbackTimeouts.connection;
    }, 3000);
  }

  private setSyncFeedback(feedback: FeedbackState, message: string | null): void {
    const timeout: ReturnType<typeof setTimeout> | undefined = this.feedbackTimeouts.sync;
    if (timeout) {
      clearTimeout(timeout);
      delete this.feedbackTimeouts.sync;
    }

    this.syncFeedbackState.set(feedback);
    this.syncFeedbackMessageState.set(message);

    if (feedback === 'loading' || feedback === null) {
      return;
    }

    this.feedbackTimeouts.sync = setTimeout(() => {
      this.syncFeedbackState.set(null);
      this.syncFeedbackMessageState.set(null);
      delete this.feedbackTimeouts.sync;
    }, 3000);
  }

  private syncInstanceGroupFromSavedSettings(): void {
    const instanceGroup: ArrInstanceFormGroup = this.instanceGroup();
    const savedInstance: ArrInstanceSettings | undefined = this.getSavedInstanceFromSettings();
    if (savedInstance === undefined) {
      return;
    }

    const fingerprint: string = buildInstanceFingerprint(savedInstance);
    instanceGroup.patchValue(
      {
        id: savedInstance.id,
        name: savedInstance.name,
        baseUrl: savedInstance.baseUrl,
        apiKey: savedInstance.apiKey,
        testSucceeded: true,
        testMessage: '',
        lastTestedFingerprint: fingerprint,
        syncMessage: '',
      },
      { emitEvent: false },
    );

    instanceGroup.markAsPristine();
    for (const control of [
      instanceGroup.controls.id,
      instanceGroup.controls.name,
      instanceGroup.controls.baseUrl,
      instanceGroup.controls.apiKey,
      instanceGroup.controls.testSucceeded,
      instanceGroup.controls.testMessage,
      instanceGroup.controls.lastTestedFingerprint,
      instanceGroup.controls.syncMessage,
    ]) {
      control.markAsPristine();
    }

    this.initialInstanceFingerprintState.set(fingerprint);
  }

  private getSavedInstanceFromSettings(): ArrInstanceSettings | undefined {
    return this.instanceKind() === ARR_APPLICATION_KINDS.SONARR
      ? this.appSettingsService.sonarrInstances()[this.instanceIndex()]
      : this.appSettingsService.radarrInstances()[this.instanceIndex()];
  }
}
