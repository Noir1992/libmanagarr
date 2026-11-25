import { SizeUnitSystem } from '../../../interfaces/general/size-unit-system';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  OnDestroy,
  OnInit,
  Signal,
  signal,
  ViewChild,
  WritableSignal,
} from '@angular/core';
import { FormControl, FormGroup, NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatSelectModule } from '@angular/material/select';
import { debounceTime, Subscription } from 'rxjs';
import { AppLanguage } from '../../../interfaces/general/app-language';
import { AppTheme } from '../../../interfaces/general/app-theme';
import { FeedbackState } from '../../../interfaces/general/feedback-state';
import { AppSettingsService, ValidationProblemError } from '../../../services/app-settings.service';
import { FeedbackIconWidget } from '../../../widgets/feedback-icon/feedback-icon.widget';
import { InfoIconWidget } from '../../../widgets/info-icon/info-icon.widget';
import { SettingsUnsavedChangesBase } from '../settings-unsaved-changes.base';
import { AuthProtectionSettingsCardWidget } from './auth-protection-settings-card/auth-protection-settings-card.widget';
import { LoggingSettingsCardWidget } from './logging-settings-card/logging-settings-card.widget';
import { TitleSortingSettingsCardWidget } from './title-sorting-settings-card/title-sorting-settings-card.widget';
import {
  applyGeneralValidationProblemErrors,
  clearGeneralFieldValidationError,
  clearGeneralFieldValidationErrors,
  type GeneralField,
} from './settings-general-tab.util';

type GeneralSettingsFormGroup = FormGroup<{
  language: FormControl<AppLanguage>;
  theme: FormControl<AppTheme>;
  sizeUnitSystem: FormControl<SizeUnitSystem>;
}>;

@Component({
  selector: 'app-settings-general-tab-widget',
  imports: [
    ReactiveFormsModule,
    MatSelectModule,
    FeedbackIconWidget,
    InfoIconWidget,
    AuthProtectionSettingsCardWidget,
    LoggingSettingsCardWidget,
    TitleSortingSettingsCardWidget,
  ],
  templateUrl: './settings-general-tab.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './settings-general-tab.widget.scss',
})
export class SettingsGeneralTabWidget extends SettingsUnsavedChangesBase implements OnInit, OnDestroy {
  private readonly appSettingsService: AppSettingsService = inject(AppSettingsService);
  private readonly formBuilder: NonNullableFormBuilder = inject(NonNullableFormBuilder);
  private readonly subscriptions: Subscription = new Subscription();
  private readonly feedbackTimeouts: Partial<Record<GeneralField, ReturnType<typeof setTimeout>>> = {};
  private readonly generalFieldAutoSaveVersion: Record<GeneralField, number> = { language: 0, theme: 0, sizeUnitSystem: 0 };
  @ViewChild(LoggingSettingsCardWidget)
  private readonly loggingSettingsCardWidget?: LoggingSettingsCardWidget;
  @ViewChild(AuthProtectionSettingsCardWidget)
  private readonly authProtectionSettingsCardWidget?: AuthProtectionSettingsCardWidget;
  @ViewChild(TitleSortingSettingsCardWidget)
  private readonly titleSortingSettingsCardWidget?: TitleSortingSettingsCardWidget;

  readonly generalDescription: Signal<string> = computed((): string => this.i18n.t('settings.general.description'));
  readonly languageHeading: Signal<string> = computed((): string => this.i18n.t('settings.language'));
  readonly languageInfoMessage: Signal<string> = computed((): string => this.i18n.t('settings.general.language.info'));
  readonly themeHeading: Signal<string> = computed((): string => this.i18n.t('settings.theme'));
  readonly themeInfoMessage: Signal<string> = computed((): string => this.i18n.t('settings.general.theme.info'));
  protected readonly saveErrorLabel: Signal<string> = computed((): string => this.i18n.t('settings.saveError'));
  readonly languageOptions: readonly AppLanguage[] = ['en', 'de', 'fr'];
  readonly sizeUnitOptions: readonly SizeUnitSystem[] = ['Decimal', 'Binary'];
  readonly sizeUnitHeading = computed(() => this.i18n.t('settings.sizeUnits'));
  readonly sizeUnitInfo = computed(() => this.i18n.t('settings.sizeUnits.info'));
  protected sizeUnitLabel(value: SizeUnitSystem): string {
    return this.i18n.t(value === 'Binary' ? 'settings.sizeUnits.binary' : 'settings.sizeUnits.decimal');
  }
  readonly themeOptions: readonly AppTheme[] = ['light', 'dark', 'system'];

  protected readonly generalFieldSavingState: WritableSignal<Record<GeneralField, boolean>> = signal({
    language: false,
    theme: false,
    sizeUnitSystem: false,
  });
  protected readonly generalFieldFeedbackState: WritableSignal<Record<GeneralField, FeedbackState>> = signal({
    language: null,
    theme: null,
    sizeUnitSystem: null,
  });
  protected readonly generalFieldErrorState: WritableSignal<Record<GeneralField, string | null>> = signal({
    language: null,
    theme: null,
    sizeUnitSystem: null,
  });
  protected readonly settingsForm: GeneralSettingsFormGroup = this.formBuilder.group({
    language: this.formBuilder.control<AppLanguage>('en'),
    theme: this.formBuilder.control<AppTheme>('system'),
    sizeUnitSystem: this.formBuilder.control<SizeUnitSystem>('Decimal'),
  });
  private readonly settingsSyncEffect = effect(() => {
    const language: AppLanguage = this.appSettingsService.language();
    const theme: AppTheme = this.appSettingsService.theme();
    const sizeUnitSystem = this.appSettingsService.sizeUnitSystem();
    if (this.settingsForm.dirty) {
      return;
    }

    this.hydrateForm(language, theme, sizeUnitSystem);
  });

  ngOnInit(): void {
    void this.initializeLoggingSettingsAsync();
    this.subscriptions.add(
      this.settingsForm.controls.sizeUnitSystem.valueChanges.pipe(debounceTime(300)).subscribe(() => {
        void this.autoSaveGeneralField('sizeUnitSystem');
      }),
    );

    this.subscriptions.add(
      this.settingsForm.controls.language.valueChanges.pipe(debounceTime(300)).subscribe(() => {
        void this.autoSaveGeneralField('language');
      }),
    );

    this.subscriptions.add(
      this.settingsForm.controls.theme.valueChanges.pipe(debounceTime(300)).subscribe(() => {
        void this.autoSaveGeneralField('theme');
      }),
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
    for (const timeout of Object.values(this.feedbackTimeouts)) {
      if (timeout) {
        clearTimeout(timeout);
      }
    }
  }

  protected languageLabel(language: AppLanguage): string {
    return this.i18n.tForLanguage(language, 'settings.language.self');
  }

  protected themeLabel(theme: AppTheme): string {
    switch (theme) {
      case 'light':
        return this.i18n.t('theme.light');
      case 'dark':
        return this.i18n.t('theme.dark');
      default:
        return this.i18n.t('theme.system');
    }
  }

  protected fieldFeedback(field: GeneralField): FeedbackState {
    return this.generalFieldFeedbackState()[field];
  }

  protected fieldFeedbackMessage(field: GeneralField): string | null {
    return this.generalFieldErrorState()[field] ?? this.settingsForm.controls[field].errors?.['server']?.[0] ?? null;
  }

  private async autoSaveGeneralField(field: GeneralField): Promise<void> {
    const settingsForm: GeneralSettingsFormGroup = this.settingsForm;
    clearGeneralFieldValidationError(settingsForm.controls[field]);
    if (settingsForm.controls[field].invalid) {
      return;
    }

    this.generalFieldAutoSaveVersion[field] += 1;
    const currentVersion: number = this.generalFieldAutoSaveVersion[field];

    this.generalFieldSavingState.update((current) => ({ ...current, [field]: true }));

    try {
      await this.saveGeneralSettingsAsync();
      if (currentVersion !== this.generalFieldAutoSaveVersion[field]) {
        return;
      }

      this.triggerGeneralFieldSuccessFeedback(field);
    } catch (error) {
      if (currentVersion !== this.generalFieldAutoSaveVersion[field]) {
        return;
      }

      if (error instanceof ValidationProblemError) {
        for (const appliedError of applyGeneralValidationProblemErrors(
          {
            language: this.settingsForm.controls.language,
            theme: this.settingsForm.controls.theme,
            sizeUnitSystem: this.settingsForm.controls.sizeUnitSystem,
          },
          error.fieldErrors,
        )) {
          this.setGeneralFieldFeedback(appliedError.field, 'error', appliedError.message);
        }
        return;
      }

      const message: string = error instanceof Error && error.message.trim().length > 0 ? error.message.trim() : this.saveErrorLabel();
      this.setGeneralFieldFeedback(field, 'error', message);
    } finally {
      if (currentVersion === this.generalFieldAutoSaveVersion[field]) {
        this.generalFieldSavingState.update((current) => ({ ...current, [field]: false }));
      }
    }
  }

  private triggerGeneralFieldSuccessFeedback(field: GeneralField): void {
    this.setGeneralFieldFeedback(field, 'success', null);
    this.scheduleFieldFeedbackClear(field);
  }

  private async saveGeneralSettingsAsync(): Promise<void> {
    await this.appSettingsService.saveGeneral({
      language: this.settingsForm.controls.language.getRawValue(),
      theme: this.settingsForm.controls.theme.getRawValue(),
      sizeUnitSystem: this.settingsForm.controls.sizeUnitSystem.getRawValue(),
    });

    this.settingsForm.controls.language.markAsPristine();
    this.settingsForm.controls.theme.markAsPristine();
    this.settingsForm.controls.sizeUnitSystem.markAsPristine();
  }

  private async initializeLoggingSettingsAsync(): Promise<void> {
    try {
      await this.appSettingsService.loadLoggingSettings();
    } catch {
      // Keep the existing/default logging state until the next successful load.
    }
  }

  private setGeneralFieldFeedback(field: GeneralField, feedback: FeedbackState, message: string | null): void {
    const timeout: ReturnType<typeof setTimeout> | undefined = this.feedbackTimeouts[field];
    if (timeout) {
      clearTimeout(timeout);
      delete this.feedbackTimeouts[field];
    }

    this.generalFieldFeedbackState.update((current) => ({ ...current, [field]: feedback }));
    this.generalFieldErrorState.update((current) => ({ ...current, [field]: message }));
  }

  private scheduleFieldFeedbackClear(field: GeneralField): void {
    this.feedbackTimeouts[field] = setTimeout(() => {
      this.setGeneralFieldFeedback(field, null, null);
    }, 3000);
  }

  protected override isBusy(): boolean {
    return (
      this.generalFieldSavingState().language ||
      this.generalFieldSavingState().theme ||
      this.generalFieldSavingState().sizeUnitSystem ||
      this.authProtectionSettingsCardWidget?.isBusy() === true ||
      this.loggingSettingsCardWidget?.isBusy() === true ||
      this.titleSortingSettingsCardWidget?.isBusy() === true
    );
  }

  protected override hasUnsavedChanges(): boolean {
    return (
      this.settingsForm.dirty ||
      this.authProtectionSettingsCardWidget?.hasUnsavedChanges() === true ||
      this.loggingSettingsCardWidget?.hasUnsavedChanges() === true ||
      this.titleSortingSettingsCardWidget?.hasUnsavedChanges() === true
    );
  }

  protected override discardUnsavedChanges(): void {
    this.hydrateForm(this.appSettingsService.language(), this.appSettingsService.theme(), this.appSettingsService.sizeUnitSystem());
    this.authProtectionSettingsCardWidget?.discardUnsavedChanges();
    this.loggingSettingsCardWidget?.discardUnsavedChanges();
    this.titleSortingSettingsCardWidget?.discardUnsavedChanges();
    this.generalFieldSavingState.set({ language: false, theme: false, sizeUnitSystem: false });
    this.generalFieldFeedbackState.set({ language: null, theme: null, sizeUnitSystem: null });
    this.generalFieldErrorState.set({ language: null, theme: null, sizeUnitSystem: null });
  }

  protected override collectUnsavedChangeDescriptions(): string[] {
    const changedFields: string[] = [];

    if (this.settingsForm.controls.language.dirty) {
      changedFields.push(this.i18n.t('settings.language'));
    }

    if (this.settingsForm.controls.theme.dirty) {
      changedFields.push(this.i18n.t('settings.theme'));
    }

    if (this.settingsForm.controls.sizeUnitSystem.dirty) {
      changedFields.push(this.i18n.t('settings.sizeUnits'));
    }

    changedFields.push(...(this.loggingSettingsCardWidget?.collectUnsavedChangeDescriptions() ?? []));
    changedFields.push(...(this.titleSortingSettingsCardWidget?.collectUnsavedChangeDescriptions() ?? []));

    return changedFields;
  }

  private hydrateForm(language: AppLanguage, theme: AppTheme, sizeUnitSystem: SizeUnitSystem): void {
    this.settingsForm.controls.language.setValue(language, { emitEvent: false });
    this.settingsForm.controls.theme.setValue(theme, { emitEvent: false });
    this.settingsForm.controls.sizeUnitSystem.setValue(sizeUnitSystem, { emitEvent: false });
    this.settingsForm.markAsPristine();
    clearGeneralFieldValidationErrors({
      language: this.settingsForm.controls.language,
      theme: this.settingsForm.controls.theme,
      sizeUnitSystem: this.settingsForm.controls.sizeUnitSystem,
    });
  }
}
