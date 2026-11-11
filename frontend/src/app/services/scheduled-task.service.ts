import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable, OnDestroy, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ScheduledTaskSettings } from '../interfaces/settings/scheduled-task-settings';
import { AppI18nService } from './app-i18n.service';

export interface ScheduledTaskState {
  saving?: boolean;
  running?: boolean;
  error?: string | null;
  success?: boolean;
}

@Injectable({ providedIn: 'root' })
export class ScheduledTaskService implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly i18n = inject(AppI18nService);
  private readonly tasksState = signal<ScheduledTaskSettings[]>([]);
  private readonly state = signal<Record<string, ScheduledTaskState>>({});
  private readonly successTimeouts = new Map<string, ReturnType<typeof setTimeout>>();
  readonly tasks = this.tasksState.asReadonly();
  readonly states = this.state.asReadonly();
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  private readonly url = '/api/settings/tasks';

  ngOnDestroy(): void {
    for (const timeout of this.successTimeouts.values()) {
      clearTimeout(timeout);
    }
    this.successTimeouts.clear();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);
    try {
      this.tasksState.set((await firstValueFrom(this.http.get<ScheduledTaskSettings[]>(this.url))).map((task) => this.mapTask(task)));
    } catch (error) {
      this.loadError.set(this.message(error));
      throw error;
    } finally {
      this.loading.set(false);
    }
  }

  async save(task: ScheduledTaskSettings): Promise<void> {
    const { revision, enabled, scheduleMode, intervalUnit, intervalValue, timeHour, timeMinute, everyNDays } = task;
    await this.perform(task.taskKey, 'saving', () =>
      firstValueFrom(
        this.http.put<ScheduledTaskSettings>(`${this.url}/${encodeURIComponent(task.taskKey)}`, {
          revision,
          enabled,
          scheduleMode,
          intervalUnit,
          intervalValue,
          timeHour,
          timeMinute,
          everyNDays,
        }),
      ),
    );
  }

  async run(taskKey: string): Promise<void> {
    await this.perform(taskKey, 'running', () =>
      firstValueFrom(this.http.post<ScheduledTaskSettings>(`${this.url}/${encodeURIComponent(taskKey)}/run`, null)),
    );
  }

  private async perform(taskKey: string, operation: 'saving' | 'running', request: () => Promise<ScheduledTaskSettings>): Promise<void> {
    clearTimeout(this.successTimeouts.get(taskKey));
    this.successTimeouts.delete(taskKey);
    this.updateState(taskKey, { [operation]: true, error: null, success: false });
    try {
      const updated = this.mapTask(await request());
      this.tasksState.update((tasks) => tasks.map((task) => (task.taskKey === taskKey ? updated : task)));
      this.updateState(taskKey, { success: true });
      this.successTimeouts.set(
        taskKey,
        setTimeout(() => {
          this.updateState(taskKey, { success: false });
          this.successTimeouts.delete(taskKey);
        }, 2200),
      );
    } catch (error) {
      const message = this.message(error);
      if (error instanceof HttpErrorResponse && error.status === 409) {
        // The next edit uses fresh configuration/revision while the conflict stays visible.
        try {
          await this.load();
        } catch {
          /* loadError retains the refresh failure. */
        }
      }
      this.updateState(taskKey, { error: message });
      throw new Error(message, { cause: error });
    } finally {
      this.updateState(taskKey, { [operation]: false });
    }
  }

  private updateState(key: string, value: ScheduledTaskState): void {
    this.state.update((states) => ({ ...states, [key]: { ...states[key], ...value } }));
  }

  private mapTask(task: ScheduledTaskSettings): ScheduledTaskSettings {
    return {
      ...task,
      lastRunAt: task.lastRunAt ? new Date(task.lastRunAt) : null,
      nextRunAt: task.nextRunAt ? new Date(task.nextRunAt) : null,
    };
  }

  private message(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const problem = error.error as { title?: string; errors?: Record<string, string[]> } | null;
      return (
        Object.values(problem?.errors ?? {})
          .flat()
          .join(' ') ||
        problem?.title ||
        this.i18n.t('settings.tasks.saveError')
      );
    }
    return error instanceof Error ? error.message : this.i18n.t('settings.tasks.saveError');
  }
}
