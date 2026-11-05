import { ChangeDetectionStrategy, Component, computed, effect, inject, OnDestroy, Signal, signal, WritableSignal } from '@angular/core';
import { FormControl, FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatInput } from '@angular/material/input';
import { AuthProtectionSettings } from '../../../../interfaces/settings/auth-protection-settings';
import { FeedbackState } from '../../../../interfaces/general/feedback-state';
import { AppI18nService } from '../../../../services/app-i18n.service';
import { APP_SETTINGS_AUTH_PROTECTION_LIMITS, AppSettingsService, ValidationProblemError } from '../../../../services/app-settings.service';
import { FeedbackIconWidget } from '../../../../widgets/feedback-icon/feedback-icon.widget';
import { InfoIconWidget } from '../../../../widgets/info-icon/info-icon.widget';
import {
  applyAuthProtectionValidationProblemErrors,
  buildAuthProtectionPayload,
  clearAuthProtectionFieldValidationError,
  clearAuthProtectionFieldValidationErrors,
  hasAuthProtectionFieldChanges,
  syncSavedAuthProtectionField,
  type AuthProtectionField,
} from './auth-protection-settings-card.util';

type AuthProtectionFormGroup = FormGroup<{
  requestsPerMinute: FormControl<number>;
  maxFailedAttempts: FormControl<number>;
}>;

type AuthProtectionFieldFeedbackState = Record<AuthProtectionField, FeedbackState>;

type AuthProtectionFieldFeedbackMessageState = Record<AuthProtectionField, string | null>;

const DEFAULT_AUTH_PROTECTION: AuthProtectionSettings = {
  requestsPerMinute: 10,
  maxFailedAttempts: 5,
};

@Component({
  selector: 'app-auth-protection-settings-card-widget',
  imports: [ReactiveFormsModule, FeedbackIconWidget, InfoIconWidget, MatInput],
  templateUrl: './auth-protection-settings-card.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './auth-protection-settings-card.widget.scss',
})
export class AuthProtectionSettingsCardWidget implements OnDestroy {
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly formBuilder: NonNullableFormBuilder = inject(NonNullableFormBuilder);
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);

  protected readonly authProtectionTitle: Signal<string> = computed((): string => this.i18n.t('settings.authProtection.title'));
  protected readonly requestsPerMinuteLabel: Signal<string> = computed((): string =>
    this.i18n.t('settings.authProtection.requestsPerMinute'),
  );
  protected readonly requestsPerMinuteInfoMessage: Signal<string> = computed((): string =>
    this.i18n.t('settings.authProtection.requestsPerMinute.info'),
  );
  protected readonly maxFailedAttemptsLabel: Signal<string> = computed((): string =>
    this.i18n.t('settings.authProtection.maxFailedAttempts'),
  );
  protected readonly maxFailedAttemptsInfoMessage: Signal<string> = computed((): string =>
    this.i18n.t('settings.authProtection.maxFailedAttempts.info'),
  );
  protected readonly requestsPerMinuteLimits = APP_SETTINGS_AUTH_PROTECTION_LIMITS.requestsPerMinute;
  protected readonly maxFailedAttemptsLimits = APP_SETTINGS_AUTH_PROTECTION_LIMITS.maxFailedAttempts;

  private readonly authProtectionFeedbackResetTimeouts: Map<AuthProtectionField, ReturnType<typeof setTimeout>> = new Map();
  private readonly authProtectionSavedSettingsState: WritableSignal<AuthProtectionSettings> =
    signal<AuthProtectionSettings>(DEFAULT_AUTH_PROTECTION);
  private readonly authProtectionSavingState: WritableSignal<boolean> = signal<boolean>(false);
  private readonly authProtectionFeedbackState: WritableSignal<AuthProtectionFieldFeedbackState> = signal({
    requestsPerMinute: null,
    maxFailedAttempts: null,
  });
  private readonly authProtectionFeedbackMessageState: WritableSignal<AuthProtectionFieldFeedbackMessageState> = signal({
    requestsPerMinute: null,
    maxFailedAttempts: null,
  });
  private readonly authProtectionSavingFieldState: WritableSignal<AuthProtectionField | null> = signal<AuthProtectionField | null>(null);
  private readonly authProtectionAutoSaveVersion: WritableSignal<number> = signal<number>(0);
  private preserveFeedbackOnNextSettingsSync: boolean = false;
  protected readonly authProtectionForm: AuthProtectionFormGroup = this.formBuilder.group({
    requestsPerMinute: this.formBuilder.control<number>(DEFAULT_AUTH_PROTECTION.requestsPerMinute, [
      Validators.required,
      Validators.min(APP_SETTINGS_AUTH_PROTECTION_LIMITS.requestsPerMinute.min),
      Validators.max(APP_SETTINGS_AUTH_PROTECTION_LIMITS.requestsPerMinute.max),
    ]),
    maxFailedAttempts: this.formBuilder.control<number>(DEFAULT_AUTH_PROTECTION.maxFailedAttempts, [
      Validators.required,
      Validators.min(APP_SETTINGS_AUTH_PROTECTION_LIMITS.maxFailedAttempts.min),
      Validators.max(APP_SETTINGS_AUTH_PROTECTION_LIMITS.maxFailedAttempts.max),
    ]),
  });
  private readonly settingsSyncEffect = effect(() => {
    const authProtection: AuthProtectionSettings = this.appSettingsService.authProtection();
    if (this.authProtectionForm.dirty) {
      return;
    }

    if (this.preserveFeedbackOnNextSettingsSync) {
      this.preserveFeedbackOnNextSettingsSync = false;
      this.hydrateForm(authProtection, false, false);
      this.authProtectionSavedSettingsState.set(authProtection);
      return;
    }

    this.hydrateForm(authProtection);
    this.authProtectionSavedSettingsState.set(authProtection);
  });

  ngOnDestroy(): void {
    this.clearAuthProtectionFieldFeedback();
  }

  public isBusy(): boolean {
    return this.authProtectionSavingState();
  }

  public hasUnsavedChanges(): boolean {
    return this.authProtectionForm.dirty;
  }

  public discardUnsavedChanges(): void {
    this.hydrateForm(this.authProtectionSavedSettingsState());
    this.authProtectionSavingState.set(false);
    this.authProtectionSavingFieldState.set(null);
    this.clearAuthProtectionFieldFeedback();
  }

  public collectUnsavedChangeDescriptions(): string[] {
    const changedFields: string[] = [];

    if (this.authProtectionForm.controls.requestsPerMinute.dirty) {
      changedFields.push(this.i18n.t('settings.authProtection.requestsPerMinute'));
    }

    if (this.authProtectionForm.controls.maxFailedAttempts.dirty) {
      changedFields.push(this.i18n.t('settings.authProtection.maxFailedAttempts'));
    }

    return changedFields;
  }

  protected authProtectionFieldSavingFor(field: AuthProtectionField): boolean {
    return this.authProtectionSavingFieldState() === field;
  }

  protected authProtectionFieldFeedback(field: AuthProtectionField): FeedbackState {
    return this.authProtectionFeedbackState()[field];
  }

  protected authProtectionFieldError(field: AuthProtectionField): string | null {
    const control: FormControl<number> = this.authProtectionForm.controls[field];

    if (control.errors?.['server']) {
      return control.errors['server'][0] ?? null;
    }

    if (!control.dirty && !control.touched) {
      return null;
    }

    if (control.errors === null) {
      return null;
    }

    if (control.errors['required']) {
      return this.i18n.t('settings.authProtection.validation.required');
    }

    if (control.errors['min'] || control.errors['max']) {
      return this.i18n.t('settings.authProtection.validation.range');
    }

    return this.i18n.t('settings.authProtection.validation.invalid');
  }

  protected authProtectionFieldFeedbackMessage(field: AuthProtectionField): string | null {
    return this.authProtectionForm.controls[field].errors?.['server']?.[0] ?? null;
  }

  protected async onSaveAuthProtectionFieldAsync(field: AuthProtectionField): Promise<void> {
    if (!this.canProceedWithSave(field)) {
      return;
    }

    await this.executeAuthProtectionSave(field);
  }

  private canProceedWithSave(field: AuthProtectionField): boolean {
    if (this.authProtectionSavingState()) {
      return false;
    }

    const savedAuthProtection: AuthProtectionSettings = this.authProtectionSavedSettingsState();
    const currentAuthProtection: AuthProtectionSettings = { ...this.authProtectionForm.getRawValue() };
    if (!hasAuthProtectionFieldChanges(savedAuthProtection, currentAuthProtection, field)) {
      return false;
    }

    clearAuthProtectionFieldValidationError(this.authProtectionForm.controls[field]);

    const control: FormControl<number> = this.authProtectionForm.controls[field];
    if (control.invalid) {
      control.markAsTouched();
      return false;
    }

    return true;
  }

  private async executeAuthProtectionSave(changedField: AuthProtectionField): Promise<void> {
    const version: number = this.incrementAutoSaveVersion();
    const payload: AuthProtectionSettings = buildAuthProtectionPayload(
      this.authProtectionSavedSettingsState(),
      { ...this.authProtectionForm.getRawValue() },
      changedField,
    );
    this.beginSaving(changedField);

    try {
      await this.appSettingsService.saveAuthProtection(payload);
      if (!this.isCurrentVersion(version)) {
        return;
      }

      this.completeSavingSuccess(changedField, this.appSettingsService.authProtection());
    } catch (error) {
      if (!this.isCurrentVersion(version)) {
        return;
      }

      if (error instanceof ValidationProblemError) {
        for (const appliedError of applyAuthProtectionValidationProblemErrors(
          {
            requestsPerMinute: this.authProtectionForm.controls.requestsPerMinute,
            maxFailedAttempts: this.authProtectionForm.controls.maxFailedAttempts,
          },
          error.fieldErrors,
        )) {
          this.setAuthProtectionFieldFeedback(appliedError.field, 'error', appliedError.message);
        }
        return;
      }

      this.completeSavingError(changedField, error);
    } finally {
      if (this.isCurrentVersion(version)) {
        this.endSaving();
      }
    }
  }

  private incrementAutoSaveVersion(): number {
    const next: number = this.authProtectionAutoSaveVersion() + 1;
    this.authProtectionAutoSaveVersion.set(next);
    return next;
  }

  private isCurrentVersion(version: number): boolean {
    return this.authProtectionAutoSaveVersion() === version;
  }

  private beginSaving(field: AuthProtectionField): void {
    this.authProtectionSavingState.set(true);
    this.authProtectionSavingFieldState.set(field);
    this.setAuthProtectionFieldFeedback(field, 'loading', this.i18n.t('settings.authProtection.saving'));
  }

  private endSaving(): void {
    this.authProtectionSavingState.set(false);
    this.authProtectionSavingFieldState.set(null);
  }

  private completeSavingSuccess(field: AuthProtectionField, authProtection: AuthProtectionSettings): void {
    this.authProtectionSavedSettingsState.set(authProtection);
    syncSavedAuthProtectionField(
      {
        requestsPerMinute: this.authProtectionForm.controls.requestsPerMinute,
        maxFailedAttempts: this.authProtectionForm.controls.maxFailedAttempts,
      },
      field,
      authProtection,
    );
    clearAuthProtectionFieldValidationError(this.authProtectionForm.controls[field]);
    this.preserveFeedbackOnNextSettingsSync = true;
    this.setAuthProtectionFieldFeedback(field, 'success', null);
  }

  private completeSavingError(field: AuthProtectionField, error: unknown): void {
    const message: string =
      error instanceof Error && error.message.trim().length > 0 ? error.message.trim() : this.i18n.t('settings.saveError');
    this.setAuthProtectionFieldFeedback(field, 'error', message);
  }

  private hydrateForm(authProtection: AuthProtectionSettings, resetFeedback: boolean = true, clearValidationErrors: boolean = true): void {
    if (clearValidationErrors) {
      clearAuthProtectionFieldValidationErrors({
        requestsPerMinute: this.authProtectionForm.controls.requestsPerMinute,
        maxFailedAttempts: this.authProtectionForm.controls.maxFailedAttempts,
      });
    }

    this.authProtectionForm.setValue(
      {
        requestsPerMinute: authProtection.requestsPerMinute,
        maxFailedAttempts: authProtection.maxFailedAttempts,
      },
      { emitEvent: false },
    );
    this.authProtectionForm.markAsPristine();
    if (resetFeedback) {
      this.clearAuthProtectionFieldFeedback();
    }
  }

  private setAuthProtectionFieldFeedback(field: AuthProtectionField, feedback: FeedbackState, message: string | null): void {
    this.clearAuthProtectionFieldFeedbackTimeout(field);
    const currentFeedbackState: AuthProtectionFieldFeedbackState = this.authProtectionFeedbackState();
    const currentMessageState: AuthProtectionFieldFeedbackMessageState = this.authProtectionFeedbackMessageState();
    const nextFeedbackState: AuthProtectionFieldFeedbackState = {
      ...currentFeedbackState,
      [field]: feedback,
    };
    const nextMessageState: AuthProtectionFieldFeedbackMessageState = {
      ...currentMessageState,
      [field]: message,
    };

    this.authProtectionFeedbackState.set(nextFeedbackState);
    this.authProtectionFeedbackMessageState.set(nextMessageState);

    if (feedback === 'success') {
      this.authProtectionFeedbackResetTimeouts.set(
        field,
        setTimeout(() => {
          this.clearAuthProtectionFieldFeedback(field);
        }, 2200),
      );
    }
  }

  private clearAuthProtectionFieldFeedback(field?: AuthProtectionField): void {
    if (field !== undefined) {
      this.clearAuthProtectionFieldFeedbackTimeout(field);
      const currentFeedbackState: AuthProtectionFieldFeedbackState = this.authProtectionFeedbackState();
      const currentMessageState: AuthProtectionFieldFeedbackMessageState = this.authProtectionFeedbackMessageState();
      const nextFeedbackState: AuthProtectionFieldFeedbackState = {
        ...currentFeedbackState,
        [field]: null,
      };
      const nextMessageState: AuthProtectionFieldFeedbackMessageState = {
        ...currentMessageState,
        [field]: null,
      };
      this.authProtectionFeedbackState.set(nextFeedbackState);
      this.authProtectionFeedbackMessageState.set(nextMessageState);
      return;
    }

    for (const authProtectionField of ['requestsPerMinute', 'maxFailedAttempts'] as const) {
      this.clearAuthProtectionFieldFeedbackTimeout(authProtectionField);
    }

    this.authProtectionFeedbackState.set({
      requestsPerMinute: null,
      maxFailedAttempts: null,
    });
    this.authProtectionFeedbackMessageState.set({
      requestsPerMinute: null,
      maxFailedAttempts: null,
    });
    this.preserveFeedbackOnNextSettingsSync = false;
  }

  private clearAuthProtectionFieldFeedbackTimeout(field: AuthProtectionField): void {
    const timeout: ReturnType<typeof setTimeout> | undefined = this.authProtectionFeedbackResetTimeouts.get(field);
    if (timeout !== undefined) {
      clearTimeout(timeout);
      this.authProtectionFeedbackResetTimeouts.delete(field);
    }
  }
}
