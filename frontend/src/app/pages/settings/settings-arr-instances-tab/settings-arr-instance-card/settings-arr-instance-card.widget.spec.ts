import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ARR_APPLICATION_KINDS, ArrApplicationKind } from '../../../../interfaces/general/arr-application-kind';
import { ArrInstanceFormGroup } from '../../../../interfaces/settings/arr-instance-form-group';
import { ArrInstanceSettings } from '../../../../interfaces/settings/arr-instance-settings';
import { signal, WritableSignal } from '@angular/core';
import { AppI18nService } from '../../../../services/app-i18n.service';
import { AppSettingsService, ValidationProblemError } from '../../../../services/app-settings.service';
import { SettingsArrInstanceCardWidget } from './settings-arr-instance-card.widget';

describe('ArrInstanceCardWidget', () => {
  let fixture: ComponentFixture<SettingsArrInstanceCardWidget>;
  let sonarrInstances: ArrInstanceSettings[];
  let saveSonarrInstanceSpy: jasmine.Spy;
  let sonarrInstancesSignal: WritableSignal<ArrInstanceSettings[]>;
  let radarrInstancesSignal: WritableSignal<ArrInstanceSettings[]>;
  let syncingArrInstancesSignal: WritableSignal<ReadonlySet<string>>;
  let appSettingsServiceStub: Pick<
    AppSettingsService,
    | 'saveSonarrInstance'
    | 'saveRadarrInstance'
    | 'deleteSonarrInstance'
    | 'deleteRadarrInstance'
    | 'testConnection'
    | 'syncInstance'
    | 'sonarrInstances'
    | 'radarrInstances'
    | 'isSyncing'
  >;

  function createFormGroup(): ArrInstanceFormGroup {
    return new FormGroup({
      clientKey: new FormControl('client-1', { nonNullable: true }),
      id: new FormControl('arr-1', { nonNullable: true }),
      name: new FormControl('Sonarr 1', { nonNullable: true }),
      baseUrl: new FormControl('http://localhost:8989', { nonNullable: true }),
      apiKey: new FormControl('secret', { nonNullable: true }),
      testSucceeded: new FormControl(false, { nonNullable: true }),
      testMessage: new FormControl('', { nonNullable: true }),
      lastTestedFingerprint: new FormControl('', { nonNullable: true }),
      syncMessage: new FormControl('', { nonNullable: true }),
    });
  }

  function setCommonInputs(): void {
    fixture.componentRef.setInput('instanceGroup', createFormGroup());
    fixture.componentRef.setInput('instanceKind', ARR_APPLICATION_KINDS.SONARR);
    fixture.componentRef.setInput('instanceIndex', 0);
    fixture.componentRef.setInput('isDeletingState', false);
  }

  function createNewInstanceFormGroup(): ArrInstanceFormGroup {
    return new FormGroup({
      clientKey: new FormControl('client-new', { nonNullable: true }),
      id: new FormControl('', { nonNullable: true }),
      name: new FormControl('Sonarr 2', { nonNullable: true }),
      baseUrl: new FormControl('http://localhost:8990', { nonNullable: true }),
      apiKey: new FormControl('secret-2', { nonNullable: true }),
      testSucceeded: new FormControl(false, { nonNullable: true }),
      testMessage: new FormControl('', { nonNullable: true }),
      lastTestedFingerprint: new FormControl('', { nonNullable: true }),
      syncMessage: new FormControl('', { nonNullable: true }),
    });
  }

  beforeEach(async () => {
    sonarrInstances = [
      {
        id: 'sonarr-1',
        name: 'Sonarr 1',
        baseUrl: 'http://localhost:8989',
        apiKey: 'secret',
      },
      {
        id: 'sonarr-2',
        name: 'Sonarr 2',
        baseUrl: 'http://localhost:8990',
        apiKey: 'secret-2',
      },
    ];
    saveSonarrInstanceSpy = jasmine.createSpy('saveSonarrInstance').and.callFake(async (index: number, instance: ArrInstanceSettings) => {
      sonarrInstances[index] = {
        ...instance,
        id: 'sonarr-2',
      };
    });
    sonarrInstancesSignal = signal([...sonarrInstances]);
    radarrInstancesSignal = signal<ArrInstanceSettings[]>([]);
    syncingArrInstancesSignal = signal<ReadonlySet<string>>(new Set<string>());
    appSettingsServiceStub = {
      saveSonarrInstance: saveSonarrInstanceSpy,
      saveRadarrInstance: jasmine.createSpy('saveRadarrInstance').and.resolveTo(),
      deleteSonarrInstance: jasmine.createSpy('deleteSonarrInstance').and.resolveTo(),
      deleteRadarrInstance: jasmine.createSpy('deleteRadarrInstance').and.resolveTo(),
      testConnection: jasmine.createSpy('testConnection').and.resolveTo({
        message: '',
        testedAt: null,
        isSuccess: true,
      }),
      syncInstance: jasmine.createSpy('syncInstance').and.resolveTo({
        message: '',
        syncedAt: null,
        itemCount: 0,
      }),
      sonarrInstances: sonarrInstancesSignal,
      radarrInstances: radarrInstancesSignal,
      isSyncing: (kind: ArrApplicationKind, instanceId: string): boolean => syncingArrInstancesSignal().has(`${kind}:${instanceId}`),
    };

    await TestBed.configureTestingModule({
      imports: [ReactiveFormsModule, SettingsArrInstanceCardWidget],
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
                'settings.sync': 'Sync',
                'settings.remove': 'Remove',
                'settings.saving': 'Saving...',
                'settings.saveError': 'Save error',
                'settings.connectionChanged': 'Connection changed',
                'settings.syncError': 'Sync error',
                'common.tableLoadError': 'Load error',
                'common.info': 'info',
              })[key] ?? key,
          },
        },
      ],
    }).compileComponents();
  });

  it('should render the instance fields and emit actions', () => {
    fixture = TestBed.createComponent(SettingsArrInstanceCardWidget);
    setCommonInputs();
    fixture.detectChanges();

    const inputs: NodeListOf<HTMLInputElement> = fixture.nativeElement.querySelectorAll('input');
    expect(inputs).toHaveSize(3);
    expect(inputs[0].value).toBe('Sonarr 1');
    expect(inputs[1].value).toBe('http://localhost:8989');
  });

  it('should emit button actions', () => {
    fixture = TestBed.createComponent(SettingsArrInstanceCardWidget);
    const instanceGroup: ArrInstanceFormGroup = createFormGroup();
    instanceGroup.controls.testSucceeded.setValue(true);
    instanceGroup.controls.lastTestedFingerprint.setValue(
      JSON.stringify({
        name: instanceGroup.controls.name.getRawValue(),
        baseUrl: instanceGroup.controls.baseUrl.getRawValue(),
        apiKey: instanceGroup.controls.apiKey.getRawValue(),
      }),
    );
    fixture.componentRef.setInput('instanceGroup', instanceGroup);
    fixture.componentRef.setInput('instanceKind', ARR_APPLICATION_KINDS.SONARR);
    fixture.componentRef.setInput('instanceIndex', 0);
    fixture.componentRef.setInput('isDeletingState', false);
    fixture.detectChanges();

    spyOn(fixture.componentInstance.removeInstance, 'emit');
    spyOn(fixture.componentInstance as unknown as Record<string, () => Promise<void>>, 'testConnectionAsync').and.resolveTo();
    spyOn(fixture.componentInstance as unknown as Record<string, () => Promise<void>>, 'syncInstanceAsync').and.resolveTo();

    const visibilityButton: HTMLButtonElement = fixture.nativeElement.querySelector('.password-wrap button');
    const actionButtons: NodeListOf<HTMLButtonElement> = fixture.nativeElement.querySelectorAll('.instance-actions button');

    visibilityButton.click();
    actionButtons[0].click();
    actionButtons[1].click();
    actionButtons[2].click();

    expect(fixture.componentInstance.removeInstance.emit).toHaveBeenCalled();
    expect((fixture.componentInstance as unknown as Record<string, jasmine.Spy>)['testConnectionAsync']).toHaveBeenCalled();
    expect((fixture.componentInstance as unknown as Record<string, jasmine.Spy>)['syncInstanceAsync']).toHaveBeenCalled();
  });

  it('should toggle the api key visibility locally', () => {
    fixture = TestBed.createComponent(SettingsArrInstanceCardWidget);
    setCommonInputs();
    fixture.detectChanges();

    const apiKeyInput: HTMLInputElement = fixture.nativeElement.querySelectorAll('input')[2];
    expect(apiKeyInput.type).toBe('password');

    const visibilityButton: HTMLButtonElement = fixture.nativeElement.querySelector('.password-wrap button');
    visibilityButton.click();
    fixture.detectChanges();

    expect(apiKeyInput.type).toBe('text');
  });

  it('shows the retest hint after changing the api key', () => {
    fixture = TestBed.createComponent(SettingsArrInstanceCardWidget);
    setCommonInputs();
    fixture.detectChanges();

    fixture.componentInstance['instanceGroup']().controls.apiKey.setValue('changed-secret');
    fixture.detectChanges();

    expect(fixture.componentInstance['connectionFeedbackMessage']()).toBe('Connection changed');
  });

  it('should restore the saved id after a successful first save', async () => {
    fixture = TestBed.createComponent(SettingsArrInstanceCardWidget);
    fixture.componentRef.setInput('instanceGroup', createNewInstanceFormGroup());
    fixture.componentRef.setInput('instanceKind', ARR_APPLICATION_KINDS.SONARR);
    fixture.componentRef.setInput('instanceIndex', 1);
    fixture.componentRef.setInput('isDeletingState', false);
    fixture.detectChanges();

    await fixture.componentInstance['testConnectionAsync']();
    await fixture.whenStable();
    fixture.detectChanges();

    const instanceGroup: ArrInstanceFormGroup = fixture.componentInstance['instanceGroup']();

    expect(instanceGroup.controls.id.getRawValue()).toBe('sonarr-2');
    expect(instanceGroup.dirty).toBeFalse();
    expect(fixture.nativeElement.querySelectorAll('.instance-actions button')[1].disabled).toBeFalse();
    expect(fixture.nativeElement.querySelector('.instance-card').classList.contains('has-success')).toBeTrue();
  });

  it('should auto-save the name on blur for a persisted instance', async () => {
    fixture = TestBed.createComponent(SettingsArrInstanceCardWidget);
    setCommonInputs();
    fixture.detectChanges();

    const nameInput: HTMLInputElement = fixture.nativeElement.querySelector(`#${fixture.componentInstance.nameFieldId}`);
    nameInput.value = 'Sonarr 1 renamed';
    nameInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(fixture.componentInstance['connectionFeedbackMessage']()).toBeNull();

    nameInput.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(saveSonarrInstanceSpy).toHaveBeenCalledTimes(1);
    expect(saveSonarrInstanceSpy).toHaveBeenCalledWith(
      0,
      jasmine.objectContaining({
        id: 'arr-1',
        name: 'Sonarr 1 renamed',
        baseUrl: 'http://localhost:8989',
        apiKey: 'secret',
      }),
    );
    expect(fixture.componentInstance['nameFeedbackState']()).toBe('success');
  });

  it('shows server validation errors on the affected input', async () => {
    saveSonarrInstanceSpy.and.rejectWith(
      new ValidationProblemError('Validation failed', {
        'sonarrInstances[0].name': ['The name must be unique.'],
      }),
    );

    fixture = TestBed.createComponent(SettingsArrInstanceCardWidget);
    setCommonInputs();
    fixture.detectChanges();

    await fixture.componentInstance['testConnectionAsync']();
    await fixture.whenStable();
    fixture.detectChanges();

    const nameInput: HTMLInputElement = fixture.nativeElement.querySelector(`#${fixture.componentInstance.nameFieldId}`);
    expect(nameInput.classList.contains('has-error')).toBeTrue();
    expect(fixture.componentInstance['instanceFieldErrorMessage']('name')).toBe('The name must be unique.');
    expect(fixture.nativeElement.textContent).toContain('The name must be unique.');
  });

  it('disables the remove button while this instance is synchronizing', () => {
    fixture = TestBed.createComponent(SettingsArrInstanceCardWidget);
    setCommonInputs();
    fixture.detectChanges();

    const actionButtonsBefore: NodeListOf<HTMLButtonElement> = fixture.nativeElement.querySelectorAll('.instance-actions button');
    expect(actionButtonsBefore[2].disabled).toBeFalse();

    syncingArrInstancesSignal.set(new Set([`${ARR_APPLICATION_KINDS.SONARR}:arr-1`]));
    fixture.detectChanges();

    const actionButtons: NodeListOf<HTMLButtonElement> = fixture.nativeElement.querySelectorAll('.instance-actions button');
    expect(actionButtons[1].disabled).toBeTrue();
    expect(actionButtons[2].disabled).toBeTrue();
  });

  it('keeps the remove button usable while another instance is synchronizing', () => {
    fixture = TestBed.createComponent(SettingsArrInstanceCardWidget);
    setCommonInputs();
    fixture.detectChanges();

    syncingArrInstancesSignal.set(new Set([`${ARR_APPLICATION_KINDS.RADARR}:sonarr-2`]));
    fixture.detectChanges();

    const actionButtons: NodeListOf<HTMLButtonElement> = fixture.nativeElement.querySelectorAll('.instance-actions button');
    expect(actionButtons[2].disabled).toBeFalse();
  });
});
