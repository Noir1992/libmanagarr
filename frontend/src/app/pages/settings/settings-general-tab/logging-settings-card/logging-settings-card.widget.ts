import { Component, computed, effect, inject, OnDestroy, Signal, signal, WritableSignal, ChangeDetectionStrategy } from '@angular/core';
import { FormControl, FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatInput } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { AppLogLevel } from '../../../../interfaces/general/app-log-level';
import { FeedbackState } from '../../../../interfaces/general/feedback-state';
import { LoggingSettings } from '../../../../interfaces/settings/logging-settings';
import { AppI18nService } from '../../../../services/app-i18n.service';
import { AppSettingsService, ValidationProblemError } from '../../../../services/app-settings.service';
import { FeedbackIconWidget } from '../../../../widgets/feedback-icon/feedback-icon.widget';
import { InfoIconWidget } from '../../../../widgets/info-icon/info-icon.widget';
import {
  applyValidationProblemErrors,
  buildLoggingSettingsPayload,
  clearLoggingFieldValidationError,
  clearLoggingFieldValidationErrors,
  hasLoggingFieldChanges,
  syncSavedLoggingField,
  type LoggingField,
} from './logging-settings-card.util';

type LoggingSettingsFormGroup = FormGroup<{
  retentionDays: FormControl<number>;
  minimumLevel: FormControl<AppLogLevel>;
}>;

type LoggingFieldFeedbackState = Record<LoggingField, FeedbackState>;

type LoggingFieldFeedbackMessageState = Record<LoggingField, string | null>;

const LOG_LEVELS: readonly AppLogLevel[] = ['Debug', 'Information', 'Warning', 'Error'];

@Component({
  selector: 'app-settings-logging-card-widget',
  imports: [ReactiveFormsModule, MatSelectModule, FeedbackIconWidget, InfoIconWidget, MatInput],
  templateUrl: './logging-settings-card.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './logging-settings-card.widget.scss',
})
export class LoggingSettingsCardWidget implements OnDestroy {
  private readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly formBuilder: NonNullableFormBuilder = inject(NonNullableFormBuilder);
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);

  protected readonly loggingTitle: Signal<string> = computed((): string => this.i18n.t('settings.logs.title'));
  protected readonly loggingRetentionLabel: Signal<string> = computed((): string => this.i18n.t('settings.logs.retentionDays'));
  protected readonly loggingRetentionInfoMessage: Signal<string> = computed((): string => this.i18n.t('settings.logs.retentionDays.info'));
  protected readonly loggingMinimumLevelLabel: Signal<string> = computed((): string => this.i18n.t('settings.logs.minimumLevel'));
  protected readonly loggingMinimumLevelInfoMessage: Signal<string> = computed((): string =>
    this.i18n.t('settings.logs.minimumLevel.info'),
  );
  protected readonly loggingLevelOptions: Signal<readonly AppLogLevel[]> = computed((): readonly AppLogLevel[] => LOG_LEVELS);

  private readonly loggingFeedbackResetTimeouts: Map<LoggingField, ReturnType<typeof setTimeout>> = new Map();
  private readonly loggingSavedSettingsState: WritableSignal<LoggingSettings> = signal<LoggingSettings>({
    path: './logs',
    retentionDays: 30,
    minimumLevel: 'Information',
  });
  private readonly loggingSavingState: WritableSignal<boolean> = signal<boolean>(false);
  private readonly loggingFeedbackState: WritableSignal<LoggingFieldFeedbackState> = signal({
    retentionDays: null,
    minimumLevel: null,
  });
  private readonly loggingFeedbackMessageState: WritableSignal<LoggingFieldFeedbackMessageState> = signal({
    retentionDays: null,
    minimumLevel: null,
  });
  private readonly loggingSavingFieldState: WritableSignal<LoggingField | null> = signal<LoggingField | null>(null);
  private readonly loggingAutoSaveVersion: WritableSignal<number> = signal<number>(0);
  private preserveFeedbackOnNextSettingsSync: boolean = false;
  protected readonly loggingForm: LoggingSettingsFormGroup = this.formBuilder.group({
    retentionDays: this.formBuilder.control<number>(30, [Validators.required, Validators.min(1), Validators.max(3650)]),
    minimumLevel: this.formBuilder.control<AppLogLevel>('Information'),
  });
  private readonly settingsSyncEffect = effect(() => {
    const logging: LoggingSettings = this.appSettingsService.logging();
    if (this.loggingForm.dirty) {
      return;
    }

    if (this.preserveFeedbackOnNextSettingsSync) {
      this.preserveFeedbackOnNextSettingsSync = false;
      this.hydrateForm(logging, false, false);
      this.loggingSavedSettingsState.set(logging);
      return;
    }

    this.hydrateForm(logging);
    this.loggingSavedSettingsState.set(logging);
  });

  ngOnDestroy(): void {
    this.clearLoggingFieldFeedback();
  }

  public isBusy(): boolean {
    return this.loggingSavingState();
  }

  public hasUnsavedChanges(): boolean {
    return this.loggingForm.dirty;
  }

  public discardUnsavedChanges(): void {
    this.hydrateForm(this.loggingSavedSettingsState());
    this.loggingSavingState.set(false);
    this.loggingSavingFieldState.set(null);
    this.clearLoggingFieldFeedback();
  }

  public collectUnsavedChangeDescriptions(): string[] {
    const changedFields: string[] = [];

    if (this.loggingForm.controls.retentionDays.dirty) {
      changedFields.push(this.i18n.t('settings.logs.retentionDays'));
    }

    if (this.loggingForm.controls.minimumLevel.dirty) {
      changedFields.push(this.i18n.t('settings.logs.minimumLevel'));
    }

    return changedFields;
  }

  protected loggingFieldSavingFor(field: LoggingField): boolean {
    return this.loggingSavingFieldState() === field;
  }

  protected loggingFieldFeedback(field: LoggingField): FeedbackState {
    return this.loggingFeedbackState()[field];
  }

  protected loggingFieldError(field: LoggingField): string | null {
    const control: FormControl<string | number> = this.loggingForm.controls[field];

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
      return this.i18n.t('settings.logs.validation.required');
    }

    if (control.errors['min'] || control.errors['max']) {
      return this.i18n.t('settings.logs.validation.range');
    }

    return this.i18n.t('settings.logs.validation.invalid');
  }

  protected loggingFieldFeedbackMessage(field: LoggingField): string | null {
    return this.loggingForm.controls[field].errors?.['server']?.[0] ?? null;
  }

  protected async onSaveLoggingFieldAsync(field: LoggingField): Promise<void> {
    if (!this.canProceedWithSave(field)) {
      return;
    }

    await this.executeLoggingSave(field);
  }

  private canProceedWithSave(field: LoggingField): boolean {
    if (this.loggingSavingState()) {
      return false;
    }

    const savedLogging: LoggingSettings = this.loggingSavedSettingsState();
    const currentLogging: LoggingSettings = { ...this.loggingForm.getRawValue(), path: './logs' };
    if (!hasLoggingFieldChanges(savedLogging, currentLogging, field)) {
      return false;
    }

    clearLoggingFieldValidationError(this.loggingForm.controls[field]);

    const control: FormControl<string | number> = this.loggingForm.controls[field];
    if (control.invalid) {
      control.markAsTouched();
      return false;
    }

    return true;
  }

  private async executeLoggingSave(changedField: LoggingField): Promise<void> {
    const version: number = this.incrementAutoSaveVersion();
    const payload: LoggingSettings = buildLoggingSettingsPayload(
      this.loggingSavedSettingsState(),
      { ...this.loggingForm.getRawValue(), path: './logs' },
      changedField,
    );
    this.beginSaving(changedField);

    try {
      await this.appSettingsService.saveLoggingSettings(payload);
      if (!this.isCurrentVersion(version)) {
        return;
      }

      this.completeSavingSuccess(changedField, this.appSettingsService.logging());
    } catch (error) {
      if (!this.isCurrentVersion(version)) {
        return;
      }

      if (error instanceof ValidationProblemError) {
        for (const appliedError of applyValidationProblemErrors(
          {
            retentionDays: this.loggingForm.controls.retentionDays,
            minimumLevel: this.loggingForm.controls.minimumLevel,
          },
          error.fieldErrors,
        )) {
          this.setLoggingFieldFeedback(appliedError.field, 'error', appliedError.message);
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
    const next: number = this.loggingAutoSaveVersion() + 1;
    this.loggingAutoSaveVersion.set(next);
    return next;
  }

  private isCurrentVersion(version: number): boolean {
    return this.loggingAutoSaveVersion() === version;
  }

  private beginSaving(field: LoggingField): void {
    this.loggingSavingState.set(true);
    this.loggingSavingFieldState.set(field);
    this.setLoggingFieldFeedback(field, 'loading', this.i18n.t('settings.logs.saving'));
  }

  private endSaving(): void {
    this.loggingSavingState.set(false);
    this.loggingSavingFieldState.set(null);
  }

  private completeSavingSuccess(field: LoggingField, logging: LoggingSettings): void {
    this.loggingSavedSettingsState.set(logging);
    syncSavedLoggingField(
      {
        retentionDays: this.loggingForm.controls.retentionDays,
        minimumLevel: this.loggingForm.controls.minimumLevel,
      },
      field,
      logging,
    );
    clearLoggingFieldValidationError(this.loggingForm.controls[field]);
    this.preserveFeedbackOnNextSettingsSync = true;
    this.setLoggingFieldFeedback(field, 'success', null);
  }

  private completeSavingError(field: LoggingField, error: unknown): void {
    const message: string =
      error instanceof Error && error.message.trim().length > 0 ? error.message.trim() : this.i18n.t('settings.saveError');
    this.setLoggingFieldFeedback(field, 'error', message);
  }

  private hydrateForm(logging: LoggingSettings, resetFeedback: boolean = true, clearValidationErrors: boolean = true): void {
    if (clearValidationErrors) {
      clearLoggingFieldValidationErrors({
        retentionDays: this.loggingForm.controls.retentionDays,
        minimumLevel: this.loggingForm.controls.minimumLevel,
      });
    }

    this.loggingForm.setValue(
      {
        retentionDays: logging.retentionDays,
        minimumLevel: logging.minimumLevel,
      },
      { emitEvent: false },
    );
    this.loggingForm.markAsPristine();
    if (resetFeedback) {
      this.clearLoggingFieldFeedback();
    }
  }

  private setLoggingFieldFeedback(field: LoggingField, feedback: FeedbackState, message: string | null): void {
    this.clearLoggingFieldFeedbackTimeout(field);
    const currentFeedbackState: LoggingFieldFeedbackState = this.loggingFeedbackState();
    const currentMessageState: LoggingFieldFeedbackMessageState = this.loggingFeedbackMessageState();
    const nextFeedbackState: LoggingFieldFeedbackState = {
      ...currentFeedbackState,
      [field]: feedback,
    };
    const nextMessageState: LoggingFieldFeedbackMessageState = {
      ...currentMessageState,
      [field]: message,
    };

    this.loggingFeedbackState.set(nextFeedbackState);
    this.loggingFeedbackMessageState.set(nextMessageState);

    if (feedback === 'success') {
      this.loggingFeedbackResetTimeouts.set(
        field,
        setTimeout(() => {
          this.clearLoggingFieldFeedback(field);
        }, 2200),
      );
    }
  }

  private clearLoggingFieldFeedback(field?: LoggingField): void {
    if (field !== undefined) {
      this.clearLoggingFieldFeedbackTimeout(field);
      const currentFeedbackState: LoggingFieldFeedbackState = this.loggingFeedbackState();
      const currentMessageState: LoggingFieldFeedbackMessageState = this.loggingFeedbackMessageState();
      const nextFeedbackState: LoggingFieldFeedbackState = {
        ...currentFeedbackState,
        [field]: null,
      };
      const nextMessageState: LoggingFieldFeedbackMessageState = {
        ...currentMessageState,
        [field]: null,
      };
      this.loggingFeedbackState.set(nextFeedbackState);
      this.loggingFeedbackMessageState.set(nextMessageState);
      return;
    }

    for (const loggingField of ['retentionDays', 'minimumLevel'] as const) {
      this.clearLoggingFieldFeedbackTimeout(loggingField);
    }

    this.loggingFeedbackState.set({
      retentionDays: null,
      minimumLevel: null,
    });
    this.loggingFeedbackMessageState.set({
      retentionDays: null,
      minimumLevel: null,
    });
    this.preserveFeedbackOnNextSettingsSync = false;
  }

  private clearLoggingFieldFeedbackTimeout(field: LoggingField): void {
    const timeout: ReturnType<typeof setTimeout> | undefined = this.loggingFeedbackResetTimeouts.get(field);
    if (timeout !== undefined) {
      clearTimeout(timeout);
      this.loggingFeedbackResetTimeouts.delete(field);
    }
  }
}
