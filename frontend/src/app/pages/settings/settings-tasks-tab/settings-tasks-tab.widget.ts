import { Component, computed, inject, OnInit, Signal, ChangeDetectionStrategy } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTableModule } from '@angular/material/table';
import { firstValueFrom } from 'rxjs';
import { CanDeactivateWithPendingChanges } from '../../../interfaces/general/can-deactivate-with-pending-changes';
import { FeedbackState } from '../../../interfaces/general/feedback-state';
import { ScheduledTaskRow } from '../../../interfaces/settings/scheduled-task-row';
import { ScheduledTaskSettings } from '../../../interfaces/settings/scheduled-task-settings';
import { AppDatePipe } from '../../../pipes/app-date.pipe';
import { AppI18nService } from '../../../services/app-i18n.service';
import { ScheduledTaskService } from '../../../services/scheduled-task.service';
import { FeedbackIconWidget } from '../../../widgets/feedback-icon/feedback-icon.widget';
import { InfoIconWidget } from '../../../widgets/info-icon/info-icon.widget';
import { TaskEditorDialogComponent } from './task-editor-dialog/task-editor-dialog.component';
import { buildTaskRows, describeTaskRunSchedule } from './settings-tasks-tab.util';

@Component({
  selector: 'app-settings-tasks-tab-widget',
  imports: [
    MatButtonModule,
    MatIconModule,
    MatDialogModule,
    MatSlideToggleModule,
    MatTableModule,
    FeedbackIconWidget,
    InfoIconWidget,
    AppDatePipe,
  ],
  templateUrl: './settings-tasks-tab.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './settings-tasks-tab.widget.scss',
})
export class SettingsTasksTabWidget implements OnInit, CanDeactivateWithPendingChanges {
  private readonly i18n: AppI18nService = inject(AppI18nService);
  protected readonly taskService: ScheduledTaskService = inject(ScheduledTaskService);
  private readonly dialog: MatDialog = inject(MatDialog);

  protected readonly taskNameColumnLabel: Signal<string> = computed((): string => this.i18n.t('settings.tasks.table.name'));
  protected readonly taskLastRunLabel: Signal<string> = computed((): string => this.i18n.t('settings.tasks.lastRun'));
  protected readonly taskNextRunLabel: Signal<string> = computed((): string => this.i18n.t('settings.tasks.nextRun'));
  protected readonly taskEnabledColumnLabel: Signal<string> = computed((): string => this.i18n.t('settings.tasks.enabled'));
  protected readonly taskActionsColumnLabel: Signal<string> = computed((): string => this.i18n.t('settings.tasks.actions'));
  protected readonly taskEditLabel: Signal<string> = computed((): string => this.i18n.t('settings.tasks.edit'));
  protected readonly taskRunNowLabel: Signal<string> = computed((): string => this.i18n.t('settings.tasks.runNow'));
  protected readonly taskNoValueLabel: Signal<string> = computed((): string => this.i18n.t('common.na'));
  protected readonly taskRows: Signal<ScheduledTaskRow[]> = computed(() =>
    buildTaskRows(this.taskService.tasks(), this.i18n.t.bind(this.i18n)),
  );

  readonly taskColumns: readonly string[] = ['name', 'lastRunAt', 'nextRunAt', 'enabled', 'actions'];
  readonly tasksDescription: Signal<string> = computed((): string => this.i18n.t('settings.tasksTab.description'));
  readonly readOnlyTaskReason: Signal<string> = computed((): string => this.i18n.t('settings.tasks.readOnly.infoMessage'));

  ngOnInit(): void {
    void this.initializeScheduledTasksAsync();
  }

  canDeactivate(): boolean {
    return true;
  }

  protected isTaskDateValue(value: string | Date | null): value is Date {
    return value instanceof Date;
  }

  protected async openTaskEditorAsync(row: ScheduledTaskSettings): Promise<void> {
    const dialogRef: MatDialogRef<TaskEditorDialogComponent, boolean | undefined> = this.dialog.open<
      TaskEditorDialogComponent,
      ScheduledTaskSettings,
      boolean | undefined
    >(TaskEditorDialogComponent, {
      width: '720px',
      maxWidth: 'calc(100vw - 24px)',
      data: row,
    });

    await firstValueFrom(dialogRef.afterClosed());
  }

  protected async toggleTaskEnabledAsync(row: ScheduledTaskSettings, enabled: boolean): Promise<void> {
    if (row.readOnly || this.taskService.states()[row.taskKey]?.saving) return;
    try {
      await this.taskService.save({ ...row, enabled });
    } catch {
      /* Per-task error is rendered beside the row. */
    }
  }

  protected async runTaskNowAsync(row: ScheduledTaskSettings): Promise<void> {
    try {
      await this.taskService.run(row.taskKey);
    } catch {
      /* Per-task error is rendered beside the row. */
    }
  }

  protected taskInfoMessage(row: ScheduledTaskRow): string {
    return `${this.readOnlyTaskReason()} ${describeTaskRunSchedule(row, this.i18n.t.bind(this.i18n))}`;
  }

  protected taskFeedback(row: ScheduledTaskRow): FeedbackState {
    const state = this.taskService.states()[row.taskKey];
    if (state?.saving || state?.running) return 'loading';
    if (state?.error) return 'error';
    return state?.success ? 'success' : null;
  }

  private async initializeScheduledTasksAsync(): Promise<void> {
    try {
      await this.taskService.load();
    } catch {
      /* The load error is rendered above the table. */
    }
  }
}
