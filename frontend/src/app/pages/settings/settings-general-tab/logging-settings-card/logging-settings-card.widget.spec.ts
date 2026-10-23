import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule } from '@angular/forms';
import { signal, WritableSignal } from '@angular/core';
import { LoggingSettings } from '../../../../interfaces/settings/logging-settings';
import { AppI18nService } from '../../../../services/app-i18n.service';
import { AppSettingsService, ValidationProblemError } from '../../../../services/app-settings.service';
import { LoggingSettingsCardWidget } from './logging-settings-card.widget';

describe('LoggingSettingsCardWidget', () => {
  let fixture: ComponentFixture<LoggingSettingsCardWidget>;
  let saveLoggingSettingsSpy: jasmine.Spy;
  let loggingSignal: WritableSignal<LoggingSettings>;

  beforeEach(async () => {
    loggingSignal = signal<LoggingSettings>({
      path: './logs',
      retentionDays: 30,
      minimumLevel: 'Information',
    });
    saveLoggingSettingsSpy = jasmine.createSpy('saveLoggingSettings').and.callFake(async (logging: LoggingSettings) => {
      loggingSignal.set(logging);
    });

    await TestBed.configureTestingModule({
      imports: [ReactiveFormsModule, LoggingSettingsCardWidget],
      providers: [
        {
          provide: AppI18nService,
          useValue: {
            t: (key: string): string =>
              ({
                'settings.logs.title': 'Logs',
                'settings.logs.path': 'Log path',
                'settings.logs.path.info': 'Log path info',
                'settings.logs.retentionDays': 'Retention days',
                'settings.logs.retentionDays.info': 'Retention days info',
                'settings.logs.minimumLevel': 'Minimum level',
                'settings.logs.minimumLevel.info': 'Minimum level info',
                'settings.logs.validation.required': 'This value is required.',
                'settings.logs.validation.range': 'Enter a value within the allowed range.',
                'settings.logs.validation.invalid': 'Enter a valid value.',
                'settings.logs.saving': 'Saving',
                'settings.saveError': 'Save error',
                'common.info': 'info',
              })[key] ?? key,
          },
        },
        {
          provide: AppSettingsService,
          useValue: {
            saveLoggingSettings: saveLoggingSettingsSpy,
            logging: loggingSignal,
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LoggingSettingsCardWidget);
    fixture.detectChanges();
  });

  it('renders the logging settings card', () => {
    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('#logging-path')).toBeNull();
    expect(compiled.textContent).toContain('Retention days');
    expect(compiled.textContent).toContain('Minimum level');
  });

  it('autosaves the retention field through the backend service', async () => {
    fixture.componentInstance['loggingForm'].controls.retentionDays.setValue(7);
    await fixture.componentInstance['onSaveLoggingFieldAsync']('retentionDays');
    await fixture.whenStable();
    fixture.detectChanges();

    expect(saveLoggingSettingsSpy).toHaveBeenCalledOnceWith(
      jasmine.objectContaining({
        path: './logs',
        retentionDays: 7,
        minimumLevel: 'Information',
      }),
    );
    expect(fixture.componentInstance['loggingFieldFeedback']('retentionDays')).toBe('success');
  });

  it('shows a frontend validation error below retention days when the value is too large', async () => {
    fixture.componentInstance['loggingForm'].controls.retentionDays.setValue(99999);
    await fixture.componentInstance['onSaveLoggingFieldAsync']('retentionDays');
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;

    expect(saveLoggingSettingsSpy).not.toHaveBeenCalled();
    expect(compiled.querySelector('#logging-retention.has-error')).toBeTruthy();
    expect(compiled.textContent).toContain('Enter a value within the allowed range.');
  });

  it('shows a server validation error below the retention input', async () => {
    saveLoggingSettingsSpy.and.rejectWith(
      new ValidationProblemError('Validation failed', {
        'settings.logging.retentionDays': ['Retention not allowed.'],
      }),
    );

    fixture.componentInstance['loggingForm'].controls.retentionDays.setValue(7);
    await fixture.componentInstance['onSaveLoggingFieldAsync']('retentionDays');
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;

    expect(fixture.componentInstance['loggingFieldError']('retentionDays')).toBe('Retention not allowed.');
    expect(compiled.querySelector('#logging-retention.has-error')).toBeTruthy();
    expect(compiled.querySelector('.field-error')).toBeTruthy();
  });
});
