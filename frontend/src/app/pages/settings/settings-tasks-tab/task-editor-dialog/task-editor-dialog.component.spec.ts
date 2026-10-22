import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { AppI18nService } from '../../../../services/app-i18n.service';
import { ScheduledTaskService } from '../../../../services/scheduled-task.service';
import { TaskEditorDialogComponent } from './task-editor-dialog.component';

describe('TaskEditorDialogComponent', () => {
  let fixture: ComponentFixture<TaskEditorDialogComponent>;
  let dialogRefSpy: jasmine.SpyObj<MatDialogRef<TaskEditorDialogComponent>>;
  let settingsServiceSpy: jasmine.SpyObj<ScheduledTaskService>;

  beforeEach(async () => {
    dialogRefSpy = jasmine.createSpyObj<MatDialogRef<TaskEditorDialogComponent>>('MatDialogRef', ['close']);
    settingsServiceSpy = jasmine.createSpyObj<ScheduledTaskService>('ScheduledTaskService', ['save']);
    settingsServiceSpy.save.and.resolveTo();

    await TestBed.configureTestingModule({
      imports: [TaskEditorDialogComponent],
      providers: [
        { provide: MatDialogRef, useValue: dialogRefSpy },
        {
          provide: MAT_DIALOG_DATA,
          useValue: {
            taskKey: 'thirdTask',
            revision: 3,
            canRunManually: true,
            enabled: true,
            readOnly: false,
            scheduleMode: 'Interval',
            intervalUnit: 'Hour',
            intervalValue: 1,
            timeHour: 0,
            timeMinute: 0,
            everyNDays: 1,
            lastRunAt: '2026-05-25T10:00:00Z',
            nextRunAt: null,
          },
        },
        {
          provide: AppI18nService,
          useValue: {
            t: (key: string) =>
              ({
                'settings.tasks.scheduleMode.label': 'Schedule type',
                'settings.tasks.scheduleMode.label.info': 'Schedule type info',
                'settings.tasks.intervalValue': 'Interval value',
                'settings.tasks.intervalValue.info': 'Interval value info',
                'settings.tasks.intervalUnit.label': 'Interval unit',
                'settings.tasks.intervalUnit.label.info': 'Interval unit info',
                'settings.tasks.timeHour': 'Hour',
                'settings.tasks.timeHour.info': 'Hour info',
                'settings.tasks.timeMinute': 'Minute',
                'settings.tasks.timeMinute.info': 'Minute info',
                'settings.tasks.everyNDays': 'Every X days',
                'settings.tasks.everyNDays.info': 'Every X days info',
                'common.info': 'info',
              })[key] ?? key,
          },
        },
        { provide: ScheduledTaskService, useValue: settingsServiceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TaskEditorDialogComponent);
  });

  it('should only save when the user clicks save', async () => {
    fixture.detectChanges();

    fixture.componentInstance['taskForm'].controls.intervalValue.setValue(3);
    expect(settingsServiceSpy.save).not.toHaveBeenCalled();

    await fixture.componentInstance['save']();

    expect(settingsServiceSpy.save).toHaveBeenCalledOnceWith(
      jasmine.objectContaining({ taskKey: 'thirdTask', revision: 3, intervalValue: 3 }),
    );
    expect(dialogRefSpy.close).toHaveBeenCalledWith(true);
  });

  it('should close without saving when canceled', () => {
    fixture.detectChanges();

    fixture.componentInstance['cancel']();

    expect(settingsServiceSpy.save).not.toHaveBeenCalled();
    expect(dialogRefSpy.close).toHaveBeenCalledWith(false);
  });
});
