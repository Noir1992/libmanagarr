import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, Signal, signal, WritableSignal, ChangeDetectionStrategy } from '@angular/core';
import { FormControl, FormGroup, NonNullableFormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatInput } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { ScheduledTaskSettings } from '../../../../interfaces/settings/scheduled-task-settings';
import { SettingsIntervalUnit } from '../../../../interfaces/settings/settings-interval-unit';
import { SettingsScheduleMode } from '../../../../interfaces/settings/settings-schedule-mode';
import { AppI18nService } from '../../../../services/app-i18n.service';
import { ScheduledTaskService } from '../../../../services/scheduled-task.service';
import { InfoIconWidget } from '../../../../widgets/info-icon/info-icon.widget';

type TaskFormGroup = FormGroup<{
  enabled: FormControl<boolean>;
  scheduleMode: FormControl<SettingsScheduleMode>;
  intervalUnit: FormControl<SettingsIntervalUnit | null>;
  intervalValue: FormControl<number | null>;
  timeHour: FormControl<number | null>;
  timeMinute: FormControl<number | null>;
  everyNDays: FormControl<number | null>;
}>;

type TaskFormValue = {
  enabled: boolean;
  scheduleMode: SettingsScheduleMode;
  intervalUnit: SettingsIntervalUnit | null;
  intervalValue: number | null;
  timeHour: number | null;
  timeMinute: number | null;
  everyNDays: number | null;
};

@Component({
  selector: 'app-task-editor-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatSelectModule,
    MatSlideToggleModule,
    InfoIconWidget,
    MatInput,
    NgTemplateOutlet,
  ],
  templateUrl: './task-editor-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './task-editor-dialog.component.scss',
})
export class TaskEditorDialogComponent {
  private readonly formBuilder: NonNullableFormBuilder = inject(NonNullableFormBuilder);
  protected readonly i18n: AppI18nService = inject(AppI18nService);
  private readonly taskService: ScheduledTaskService = inject(ScheduledTaskService);
  private readonly dialogRef: MatDialogRef<TaskEditorDialogComponent, boolean> = inject(MatDialogRef<TaskEditorDialogComponent, boolean>);
  protected readonly task: ScheduledTaskSettings = inject<ScheduledTaskSettings>(MAT_DIALOG_DATA);

  protected readonly taskScheduleModes: readonly SettingsScheduleMode[] = ['Interval', 'DailyAtTime', 'EveryNDaysAtTime'];
  protected readonly taskIntervalUnits: readonly SettingsIntervalUnit[] = ['Minute', 'Hour', 'Day', 'Week', 'Month', 'Year'];

  protected readonly title: Signal<string> = computed((): string => this.i18n.t('settings.tasks.editDialog.title'));
  protected readonly taskEnabledLabel: Signal<string> = computed((): string => this.i18n.t('settings.tasks.enabled'));
  protected readonly taskScheduleModeHeading: Signal<string> = computed((): string => this.i18n.t('settings.tasks.scheduleMode.label'));
  protected readonly taskScheduleModeInfoMessage: Signal<string> = computed((): string =>
    this.i18n.t('settings.tasks.scheduleMode.label.info'),
  );
  protected readonly taskIntervalValueLabel: Signal<string> = computed((): string => this.i18n.t('settings.tasks.intervalValue'));
  protected readonly taskIntervalValueInfoMessage: Signal<string> = computed((): string =>
    this.i18n.t('settings.tasks.intervalValue.info'),
  );
  protected readonly taskIntervalUnitHeading: Signal<string> = computed((): string => this.i18n.t('settings.tasks.intervalUnit.label'));
  protected readonly taskIntervalUnitInfoMessage: Signal<string> = computed((): string =>
    this.i18n.t('settings.tasks.intervalUnit.label.info'),
  );
  protected readonly taskTimeHourLabel: Signal<string> = computed((): string => this.i18n.t('settings.tasks.timeHour'));
  protected readonly taskTimeHourInfoMessage: Signal<string> = computed((): string => this.i18n.t('settings.tasks.timeHour.info'));
  protected readonly taskTimeMinuteLabel: Signal<string> = computed((): string => this.i18n.t('settings.tasks.timeMinute'));
  protected readonly taskTimeMinuteInfoMessage: Signal<string> = computed((): string => this.i18n.t('settings.tasks.timeMinute.info'));
  protected readonly taskEveryNDaysLabel: Signal<string> = computed((): string => this.i18n.t('settings.tasks.everyNDays'));
  protected readonly taskEveryNDaysInfoMessage: Signal<string> = computed((): string => this.i18n.t('settings.tasks.everyNDays.info'));
  protected readonly saveLabel: Signal<string> = computed((): string => this.i18n.t('settings.tasks.save'));
  protected readonly savingLabel: Signal<string> = computed((): string => this.i18n.t('settings.tasks.saving'));
  protected readonly cancelLabel: Signal<string> = computed((): string => this.i18n.t('settings.tasks.cancel'));
  protected readonly taskScheduleModeLabels: Signal<Record<SettingsScheduleMode, string>> = computed(() => ({
    Interval: this.i18n.t('settings.tasks.scheduleMode.interval'),
    DailyAtTime: this.i18n.t('settings.tasks.scheduleMode.dailyAtTime'),
    EveryNDaysAtTime: this.i18n.t('settings.tasks.scheduleMode.everyNDaysAtTime'),
  }));
  protected readonly taskIntervalUnitLabels: Signal<Record<SettingsIntervalUnit, string>> = computed(
    (): Record<SettingsIntervalUnit, string> => ({
      Minute: this.i18n.t('settings.tasks.intervalUnit.minute'),
      Hour: this.i18n.t('settings.tasks.intervalUnit.hour'),
      Day: this.i18n.t('settings.tasks.intervalUnit.day'),
      Week: this.i18n.t('settings.tasks.intervalUnit.week'),
      Month: this.i18n.t('settings.tasks.intervalUnit.month'),
      Year: this.i18n.t('settings.tasks.intervalUnit.year'),
    }),
  );
  protected readonly taskSaveErrorState: WritableSignal<string | null> = signal<string | null>(null);
  protected readonly savingState: WritableSignal<boolean> = signal<boolean>(false);

  protected readonly scheduledTasksForm: FormGroup<{
    task: TaskFormGroup;
  }> = this.formBuilder.group({
    task: this.formBuilder.group({
      enabled: this.formBuilder.control<boolean>(this.task.enabled),
      scheduleMode: this.formBuilder.control<SettingsScheduleMode>(this.task.scheduleMode),
      intervalUnit: new FormControl<SettingsIntervalUnit | null>(this.task.intervalUnit),
      intervalValue: new FormControl<number | null>(this.task.intervalValue, [
        Validators.required,
        Validators.min(1),
        Validators.max(1000),
      ]),
      timeHour: new FormControl<number | null>(this.task.timeHour, [Validators.required, Validators.min(0), Validators.max(23)]),
      timeMinute: new FormControl<number | null>(this.task.timeMinute, [Validators.required, Validators.min(0), Validators.max(59)]),
      everyNDays: new FormControl<number | null>(this.task.everyNDays, [Validators.required, Validators.min(1), Validators.max(3650)]),
    }),
  });

  constructor() {
    this.configureTaskScheduleValidators(this.taskForm.controls.scheduleMode.getRawValue());

    this.taskForm.controls.scheduleMode.valueChanges.subscribe((mode: SettingsScheduleMode) => {
      this.applyTaskScheduleMode(mode);
    });
  }

  protected get taskForm(): TaskFormGroup {
    return this.scheduledTasksForm.controls.task;
  }

  protected taskControlError(control: FormControl<number | string | boolean | null>): string | null {
    if (!control.touched || control.errors === null) {
      return null;
    }

    if (control.errors['required']) {
      return this.i18n.t('settings.tasks.validation.required');
    }

    if (control.errors['min'] || control.errors['max']) {
      return this.i18n.t('settings.tasks.validation.range');
    }

    return this.i18n.t('settings.tasks.validation.invalid');
  }

  protected cancel(): void {
    this.dialogRef.close(false);
  }

  protected async save(): Promise<void> {
    if (this.savingState() || this.task.readOnly) {
      return;
    }

    if (this.scheduledTasksForm.invalid) {
      this.scheduledTasksForm.markAllAsTouched();
      return;
    }

    this.savingState.set(true);
    this.taskSaveErrorState.set(null);

    try {
      await this.taskService.save(this.mapTaskFormValue(this.scheduledTasksForm.getRawValue().task));
      this.dialogRef.close(true);
    } catch (error) {
      this.taskSaveErrorState.set(
        error instanceof Error && error.message.trim().length > 0 ? error.message.trim() : this.i18n.t('settings.tasks.saveError'),
      );
    } finally {
      this.savingState.set(false);
    }
  }

  private applyTaskScheduleMode(mode: SettingsScheduleMode): void {
    this.clearTaskScheduleControls();
    this.configureTaskScheduleValidators(mode);
  }

  private clearTaskScheduleControls(): void {
    this.taskForm.controls.intervalUnit.setValue(null, { emitEvent: false });
    this.taskForm.controls.intervalValue.setValue(null, { emitEvent: false });
    this.taskForm.controls.timeHour.setValue(null, { emitEvent: false });
    this.taskForm.controls.timeMinute.setValue(null, { emitEvent: false });
    this.taskForm.controls.everyNDays.setValue(null, { emitEvent: false });
  }

  private configureTaskScheduleValidators(mode: SettingsScheduleMode): void {
    const intervalUnitValidators: ValidatorFn[] = [];
    const intervalValueValidators: ValidatorFn[] = [];
    const timeHourValidators: ValidatorFn[] = [];
    const timeMinuteValidators: ValidatorFn[] = [];
    const everyNDaysValidators: ValidatorFn[] = [];

    switch (mode) {
      case 'Interval':
        intervalUnitValidators.push(Validators.required);
        intervalValueValidators.push(Validators.required, Validators.min(1), Validators.max(1000));
        break;
      case 'DailyAtTime':
        timeHourValidators.push(Validators.required, Validators.min(0), Validators.max(23));
        timeMinuteValidators.push(Validators.required, Validators.min(0), Validators.max(59));
        break;
      case 'EveryNDaysAtTime':
        everyNDaysValidators.push(Validators.required, Validators.min(1), Validators.max(3650));
        timeHourValidators.push(Validators.required, Validators.min(0), Validators.max(23));
        timeMinuteValidators.push(Validators.required, Validators.min(0), Validators.max(59));
        break;
    }

    this.taskForm.controls.intervalUnit.setValidators(intervalUnitValidators);
    this.taskForm.controls.intervalValue.setValidators(intervalValueValidators);
    this.taskForm.controls.timeHour.setValidators(timeHourValidators);
    this.taskForm.controls.timeMinute.setValidators(timeMinuteValidators);
    this.taskForm.controls.everyNDays.setValidators(everyNDaysValidators);

    for (const control of [
      this.taskForm.controls.intervalUnit,
      this.taskForm.controls.intervalValue,
      this.taskForm.controls.timeHour,
      this.taskForm.controls.timeMinute,
      this.taskForm.controls.everyNDays,
    ]) {
      control.updateValueAndValidity({ emitEvent: false });
    }
  }

  private mapTaskFormValue(formValue: TaskFormValue): ScheduledTaskSettings {
    return {
      ...this.task,
      enabled: formValue.enabled,
      scheduleMode: formValue.scheduleMode,
      intervalUnit: formValue.intervalUnit ?? 'Hour',
      intervalValue: formValue.intervalValue ?? 1,
      timeHour: formValue.timeHour ?? 0,
      timeMinute: formValue.timeMinute ?? 0,
      everyNDays: formValue.everyNDays ?? 1,
    };
  }
}
