import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { ScheduledTaskSettings } from '../../../interfaces/settings/scheduled-task-settings';
import { AppI18nService } from '../../../services/app-i18n.service';
import { AppSettingsService } from '../../../services/app-settings.service';
import { ScheduledTaskService, ScheduledTaskState } from '../../../services/scheduled-task.service';
import { SettingsTasksTabWidget } from './settings-tasks-tab.widget';

describe('SettingsTasksTabWidget', () => {
  let fixture: ComponentFixture<SettingsTasksTabWidget>;
  const task = (taskKey: string, readOnly = false): ScheduledTaskSettings => ({
    taskKey,
    revision: 0,
    canRunManually: true,
    enabled: true,
    readOnly,
    scheduleMode: 'Interval',
    intervalUnit: 'Hour',
    intervalValue: 1,
    timeHour: 0,
    timeMinute: 0,
    everyNDays: 1,
    lastRunAt: null,
    nextRunAt: null,
  });
  let service: Pick<ScheduledTaskService, 'tasks' | 'states' | 'loading' | 'loadError' | 'load' | 'save' | 'run'>;
  let dialog: jasmine.SpyObj<MatDialog>;
  const states = signal<Record<string, ScheduledTaskState>>({});

  beforeEach(async () => {
    states.set({});
    service = {
      tasks: signal([task('arrDataSync'), task('cleanup', true), task('thirdTask')]),
      states,
      loading: signal(false),
      loadError: signal(null),
      load: jasmine.createSpy('load').and.resolveTo(),
      save: jasmine.createSpy('save').and.resolveTo(),
      run: jasmine.createSpy('run').and.resolveTo(),
    };
    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    dialog.open.and.returnValue({ afterClosed: () => of(true) } as never);
    await TestBed.configureTestingModule({
      imports: [SettingsTasksTabWidget],
      providers: [
        {
          provide: AppI18nService,
          useValue: {
            t: (key: string) =>
              ({
                'settings.tasks.dataSync.title': 'Data sync',
                'settings.tasks.cleanup.title': 'Cleanup',
              })[key] ?? key,
          },
        },
        { provide: AppSettingsService, useValue: { localeId: () => 'en-US' } },
        { provide: ScheduledTaskService, useValue: service },
        { provide: MatDialog, useValue: dialog },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(SettingsTasksTabWidget);
    await fixture.whenStable();
  });

  it('renders all server tasks with a readable fallback and server capabilities', () => {
    const element = fixture.nativeElement as HTMLElement;
    const rows = element.querySelectorAll('tr.mat-mdc-row');
    expect(rows).toHaveSize(3);
    expect(rows[0].textContent).toContain('Cleanup');
    expect(rows[1].textContent).toContain('Data sync');
    expect(rows[2].textContent).toContain('third Task');
    expect(element.querySelectorAll('mat-slide-toggle')).toHaveSize(2);
    expect(element.querySelectorAll('.task-actions > button')).toHaveSize(5);
    expect(service.load).toHaveBeenCalledTimes(1);
  });

  it('runs cleanup manually while keeping its schedule read-only', async () => {
    const row = (fixture.nativeElement as HTMLElement).querySelectorAll('tr.mat-mdc-row')[0];
    const buttons = row.querySelectorAll<HTMLButtonElement>('.task-actions > button');
    expect(buttons).toHaveSize(1);
    expect(buttons[0].textContent).toContain('play_arrow');
    expect(row.querySelector('mat-slide-toggle')).toBeNull();
    buttons[0].click();
    await fixture.whenStable();
    expect(service.run).toHaveBeenCalledOnceWith('cleanup');
    expect(service.save).not.toHaveBeenCalled();

    states.set({ cleanup: { running: true } });
    await fixture.whenStable();
    expect(buttons[0].disabled).toBeTrue();
  });

  it('updates and runs the selected third task', async () => {
    const third = service.tasks()[2];
    await fixture.componentInstance['toggleTaskEnabledAsync'](third, false);
    expect(service.save).toHaveBeenCalledOnceWith({ ...third, enabled: false });
    await fixture.componentInstance['runTaskNowAsync'](third);
    expect(service.run).toHaveBeenCalledOnceWith('thirdTask');
    const activeDialog = fixture.debugElement.injector.get(MatDialog);
    spyOn(activeDialog, 'open').and.returnValue({ afterClosed: () => of(true) } as never);
    await fixture.componentInstance['openTaskEditorAsync'](third);
    expect(activeDialog.open).toHaveBeenCalledWith(jasmine.any(Function), jasmine.objectContaining({ data: third }));
  });

  it('keeps other tasks usable while a single task runs and displays its error', async () => {
    states.set({ arrDataSync: { running: true, error: 'Task conflict' } });
    await fixture.whenStable();
    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tr.mat-mdc-row');
    expect(rows[1].querySelectorAll('button.material-symbols-outlined')[1].hasAttribute('disabled')).toBeTrue();
    expect(rows[1].querySelector('mat-slide-toggle button')?.hasAttribute('disabled')).toBeFalse();
    expect(rows[2].querySelector('button')?.disabled).toBeFalse();
    expect(rows[1].textContent).toContain('Task conflict');
  });
});
