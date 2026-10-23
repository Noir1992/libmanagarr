import { signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ARR_APPLICATION_KINDS } from '../../../../interfaces/general/arr-application-kind';
import { SeerrInstanceFormGroup } from '../../../../interfaces/settings/seerr-instance-form-group';
import { SeerrInstanceSettings } from '../../../../interfaces/settings/seerr-instance-settings';
import { AppI18nService } from '../../../../services/app-i18n.service';
import { AppSettingsService, ValidationProblemError } from '../../../../services/app-settings.service';
import { SettingsSeerrCardWidget } from './settings-seerr-card.widget';

describe('SettingsSeerrCardWidget', () => {
  let fixture: ComponentFixture<SettingsSeerrCardWidget>;
  let seerrInstanceSignal: WritableSignal<SeerrInstanceSettings | null>;
  let testConnectionSpy: jasmine.Spy;
  let saveSeerrInstanceSpy: jasmine.Spy;
  let appSettingsServiceStub: Pick<AppSettingsService, 'testConnection' | 'saveSeerrInstance' | 'seerrInstance'>;

  const configuredInstance: SeerrInstanceSettings = {
    id: 'seerr-1',
    baseUrl: 'http://localhost:5055',
    apiKey: 'secret',
  };

  function createFormGroup(instance?: SeerrInstanceSettings): SeerrInstanceFormGroup {
    return new FormGroup({
      clientKey: new FormControl('seerr', { nonNullable: true }),
      id: new FormControl(instance?.id ?? '', { nonNullable: true }),
      baseUrl: new FormControl(instance?.baseUrl ?? '', { nonNullable: true, validators: [Validators.required] }),
      apiKey: new FormControl(instance?.apiKey ?? '', { nonNullable: true, validators: [Validators.required] }),
      testSucceeded: new FormControl(instance !== undefined, { nonNullable: true }),
      testMessage: new FormControl('', { nonNullable: true }),
      lastTestedFingerprint: new FormControl('', { nonNullable: true }),
    });
  }

  beforeEach(async () => {
    seerrInstanceSignal = signal<SeerrInstanceSettings | null>(null);
    testConnectionSpy = jasmine.createSpy('testConnection').and.resolveTo({ message: '', testedAt: null, isSuccess: true });
    saveSeerrInstanceSpy = jasmine.createSpy('saveSeerrInstance').and.callFake(async (instance: SeerrInstanceSettings) => {
      seerrInstanceSignal.set({ ...instance });
    });
    appSettingsServiceStub = {
      testConnection: testConnectionSpy,
      saveSeerrInstance: saveSeerrInstanceSpy,
      seerrInstance: seerrInstanceSignal,
    };

    await TestBed.configureTestingModule({
      imports: [ReactiveFormsModule, SettingsSeerrCardWidget],
      providers: [
        { provide: AppSettingsService, useValue: appSettingsServiceStub },
        {
          provide: AppI18nService,
          useValue: {
            t: (key: string): string =>
              ({
                'settings.instanceName': 'Name',
                'settings.instanceUrl': 'URL',
                'settings.instanceUrl.info': 'URL info',
                'settings.instanceApiKey': 'API key',
                'settings.instanceApiKey.info': 'API key info',
                'settings.testConnection': 'Test connection',
                'settings.remove': 'Remove',
                'settings.saveError': 'Save error',
                'settings.connectionChanged': 'Connection changed',
                'settings.instanceNameRequired': 'Name required',
                'settings.instanceApiKeyRequired': 'API key required',
                'settings.tasks.validation.required': 'Required',
                'common.tableLoadError': 'Load error',
                'common.info': 'info',
              })[key] ?? key,
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SettingsSeerrCardWidget);
    fixture.componentRef.setInput('instanceGroup', createFormGroup());
    fixture.componentRef.setInput('isConfigured', false);
    fixture.componentRef.setInput('isDeletingState', false);
    fixture.detectChanges();
  });

  it('renders the two connection fields without a name field and without a remove button while unconfigured', () => {
    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('input[formControlName="name"]')).toBeNull();
    expect(compiled.querySelector('input[formControlName="baseUrl"]')).not.toBeNull();
    expect(compiled.querySelector('input[formControlName="apiKey"]')).not.toBeNull();
    expect(compiled.querySelector('.danger-button')).toBeNull();
  });

  it('shows the remove button when configured and emits removeInstance on click', () => {
    fixture.componentRef.setInput('isConfigured', true);
    fixture.detectChanges();
    const removeSpy: jasmine.Spy = spyOn(fixture.componentInstance.removeInstance, 'emit');

    (fixture.nativeElement.querySelector('.danger-button') as HTMLButtonElement).click();

    expect(removeSpy).toHaveBeenCalled();
  });

  it('saves the instance through the service after a successful connection test', async () => {
    seerrInstanceSignal.set(configuredInstance);
    const group: SeerrInstanceFormGroup = createFormGroup(configuredInstance);
    fixture.componentRef.setInput('instanceGroup', group);
    fixture.componentRef.setInput('isConfigured', true);
    fixture.detectChanges();

    await fixture.componentInstance['testConnectionAsync']();

    expect(testConnectionSpy).toHaveBeenCalledOnceWith(ARR_APPLICATION_KINDS.SEERR, {
      id: 'seerr-1',
      baseUrl: 'http://localhost:5055',
      apiKey: 'secret',
    });
    expect(saveSeerrInstanceSpy).toHaveBeenCalledOnceWith(configuredInstance);
    expect(group.controls.testSucceeded.getRawValue()).toBeTrue();
    expect(group.dirty).toBeFalse();
  });

  it('does not save the instance when the connection test fails', async () => {
    const group: SeerrInstanceFormGroup = createFormGroup(configuredInstance);
    fixture.componentRef.setInput('instanceGroup', group);
    fixture.componentRef.setInput('isConfigured', true);
    fixture.detectChanges();
    testConnectionSpy.and.callFake(async (): Promise<never> => {
      throw new Error('Connection failed');
    });

    await fixture.componentInstance['testConnectionAsync']();

    expect(saveSeerrInstanceSpy).not.toHaveBeenCalled();
    expect(group.controls.testSucceeded.getRawValue()).toBeFalse();
    expect(group.controls.testMessage.getRawValue()).toBe('Connection failed');
  });

  it('maps server validation problems from the connection test onto the fields', async () => {
    const group: SeerrInstanceFormGroup = createFormGroup(configuredInstance);
    fixture.componentRef.setInput('instanceGroup', group);
    fixture.componentRef.setInput('isConfigured', true);
    fixture.detectChanges();
    testConnectionSpy.and.callFake(async (): Promise<never> => {
      throw new ValidationProblemError('invalid', { 'seerr.baseUrl': ['The baseUrl must be a valid absolute URL.'] });
    });

    await fixture.componentInstance['testConnectionAsync']();

    expect(group.controls.baseUrl.errors?.['server']).toEqual(['The baseUrl must be a valid absolute URL.']);
    expect(saveSeerrInstanceSpy).not.toHaveBeenCalled();
  });

  it('reports a changed connection without testing when required fields are empty', async () => {
    const group: SeerrInstanceFormGroup = createFormGroup();

    await fixture.componentInstance['testConnectionAsync']();

    expect(testConnectionSpy).not.toHaveBeenCalled();
    expect(group.controls.testSucceeded.getRawValue()).toBeFalse();
    expect(fixture.componentInstance['connectionFeedbackState']()).toBe('error');
  });
});
