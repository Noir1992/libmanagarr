import { signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AppI18nService } from '../../../services/app-i18n.service';
import { AppSettingsService } from '../../../services/app-settings.service';
import { SettingsArrInstancesTabWidget } from './settings-arr-instances-tab.widget';

describe('SettingsInstancesTabWidget', () => {
  let fixture: ComponentFixture<SettingsArrInstancesTabWidget>;
  let snackBarOpenFromComponentSpy: jasmine.Spy;
  let sonarrInstancesSignal: WritableSignal<
    {
      id: string;
      name: string;
      baseUrl: string;
      apiKey: string;
    }[]
  >;
  let radarrInstancesSignal: WritableSignal<
    {
      id: string;
      name: string;
      baseUrl: string;
      apiKey: string;
    }[]
  >;
  let seerrInstanceSignal: WritableSignal<{
    id: string;
    baseUrl: string;
    apiKey: string;
  } | null>;

  const sonarrInstances = [
    {
      id: 'sonarr-1',
      name: 'Instance',
      baseUrl: 'http://localhost',
      apiKey: 'secret',
    },
  ];

  const radarrInstances = [
    {
      id: 'radarr-1',
      name: 'Instance',
      baseUrl: 'http://localhost',
      apiKey: 'secret',
    },
  ];

  const i18nStub: Pick<AppI18nService, 't'> = {
    t: (key: string): string =>
      ({
        'settings.instancesTab': 'Instances',
        'settings.instancesTab.description': 'Instance settings',
        'settings.sonarr': 'Sonarr',
        'settings.radarr': 'Radarr',
        'settings.seerr': 'Seerr',
        'settings.addInstance': 'Add instance',
        'common.noInstanceConfigured': 'No instance configured',
        'settings.instanceName': 'Name',
        'settings.instanceUrl': 'URL',
        'settings.instanceUrl.info': 'URL info',
        'settings.instanceApiKey': 'API key',
        'settings.instanceApiKey.info': 'API key info',
        'settings.deleteSuccess': 'Deleted',
        'settings.deleteError': 'Delete error',
        'settings.unsavedChangesNewInstance': 'New instance',
        'common.info': 'info',
      })[key] ?? key,
  } as Pick<AppI18nService, 't'>;

  let settingsServiceStub: Pick<
    AppSettingsService,
    | 'loadInstances'
    | 'loadSeerrInstance'
    | 'deleteSonarrInstance'
    | 'deleteRadarrInstance'
    | 'deleteSeerrInstance'
    | 'sonarrInstances'
    | 'radarrInstances'
    | 'seerrInstance'
    | 'isSyncing'
  >;

  beforeEach(async () => {
    sonarrInstancesSignal = signal([...sonarrInstances]);
    radarrInstancesSignal = signal([...radarrInstances]);
    seerrInstanceSignal = signal(null);
    settingsServiceStub = {
      loadInstances: jasmine.createSpy('loadInstances').and.resolveTo(),
      loadSeerrInstance: jasmine.createSpy('loadSeerrInstance').and.resolveTo(),
      deleteSonarrInstance: jasmine.createSpy('deleteSonarrInstance').and.callFake(async (index: number) => {
        sonarrInstancesSignal.update((current) => current.filter((_, currentIndex) => currentIndex !== index));
      }),
      deleteRadarrInstance: jasmine.createSpy('deleteRadarrInstance').and.callFake(async (index: number) => {
        radarrInstancesSignal.update((current) => current.filter((_, currentIndex) => currentIndex !== index));
      }),
      deleteSeerrInstance: jasmine.createSpy('deleteSeerrInstance').and.callFake(async () => {
        seerrInstanceSignal.set(null);
      }),
      sonarrInstances: sonarrInstancesSignal,
      radarrInstances: radarrInstancesSignal,
      seerrInstance: seerrInstanceSignal,
      isSyncing: () => false,
    };

    await TestBed.configureTestingModule({
      imports: [SettingsArrInstancesTabWidget],
      providers: [
        { provide: AppI18nService, useValue: i18nStub },
        { provide: AppSettingsService, useValue: settingsServiceStub },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SettingsArrInstancesTabWidget);
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    snackBarOpenFromComponentSpy = spyOn(fixture.debugElement.injector.get(MatSnackBar), 'openFromComponent');
  });

  it('renders both instance sections', () => {
    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;

    expect(compiled.textContent).toContain('Instance settings');
    expect(compiled.textContent).toContain('Sonarr');
    expect(compiled.textContent).toContain('Radarr');
  });

  it('shows an empty state in both instance containers when no instances are configured', async () => {
    sonarrInstancesSignal.set([]);
    radarrInstancesSignal.set([]);
    const emptyFixture: ComponentFixture<SettingsArrInstancesTabWidget> = TestBed.createComponent(SettingsArrInstancesTabWidget);
    emptyFixture.detectChanges();
    await emptyFixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    emptyFixture.detectChanges();

    const compiled: HTMLElement = emptyFixture.nativeElement as HTMLElement;
    const emptyStates: NodeListOf<HTMLElement> = compiled.querySelectorAll('.instance-empty-state');

    expect(emptyStates).toHaveSize(2);
    expect(emptyStates[0]?.textContent).toContain('No instance configured');
    expect(emptyStates[1]?.textContent).toContain('No instance configured');
    expect(compiled.querySelector('app-settings-arr-instance-card')).toBeNull();
  });

  it('adds a Sonarr instance locally', async () => {
    const initialLength: number = fixture.componentInstance['sonarrInstancesForm'].length;
    const addButton: HTMLButtonElement = fixture.nativeElement.querySelectorAll('.card-header .secondary-button')[1];
    addButton.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance['sonarrInstancesForm']).toHaveSize(initialLength + 1);
  });

  it('removes a Radarr instance through the backend service', async () => {
    await fixture.componentInstance['removeRadarrInstanceAsync'](0);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(settingsServiceStub.deleteRadarrInstance).toHaveBeenCalledTimes(1);
    expect(snackBarOpenFromComponentSpy).toHaveBeenCalled();
    expect(fixture.componentInstance['radarrInstancesForm']).toHaveSize(0);
  });

  it('removes a new Sonarr instance locally without calling the backend', async () => {
    await fixture.componentInstance['addSonarrInstanceAsync']();

    await fixture.componentInstance['removeSonarrInstanceAsync'](1);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(settingsServiceStub.deleteSonarrInstance).not.toHaveBeenCalled();
    expect(fixture.componentInstance['sonarrInstancesForm']).toHaveSize(1);
  });

  it('discards pending instance edits when navigation is confirmed', () => {
    const confirmTarget: typeof globalThis & {
      confirm: (message?: string) => boolean;
    } = globalThis as typeof globalThis & {
      confirm: (message?: string) => boolean;
    };
    spyOn(confirmTarget, 'confirm').and.returnValue(true);

    fixture.componentInstance['sonarrInstancesForm'].at(0).controls.name.setValue('Changed');
    fixture.componentInstance['sonarrInstancesForm'].at(0).controls.name.markAsDirty();

    expect(fixture.componentInstance.canDeactivate()).toBeTrue();
    expect(fixture.componentInstance['sonarrInstancesForm'].at(0).controls.name.getRawValue()).toBe('Instance');
    expect(fixture.componentInstance['sonarrInstancesForm'].dirty).toBeFalse();
  });

  it('renders the Seerr section with its own configuration card', () => {
    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;

    expect(compiled.textContent).toContain('Seerr');
    expect(compiled.querySelectorAll('app-settings-seerr-card')).toHaveSize(1);
  });

  it('removes the Seerr configuration through the backend service', async () => {
    seerrInstanceSignal.set({ id: 'seerr-1', baseUrl: 'http://localhost:5055', apiKey: 'secret' });

    await fixture.componentInstance['removeSeerrInstanceAsync']();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(settingsServiceStub.deleteSeerrInstance).toHaveBeenCalledTimes(1);
    expect(snackBarOpenFromComponentSpy).toHaveBeenCalled();
    expect(fixture.componentInstance['seerrFormState']().controls.id.getRawValue()).toBe('');
  });
});
