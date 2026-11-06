import { signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ArrApplicationKind } from '../../../interfaces/general/arr-application-kind';
import { SymlinkCapabilityCheck, SymlinkSettings } from '../../../interfaces/symlinks/symlink-settings';
import { AppI18nService } from '../../../services/app-i18n.service';
import { AppSettingsService } from '../../../services/app-settings.service';
import { SymlinkActionState, SymlinkService } from '../../../services/symlink.service';
import { SettingsSymlinksTabWidget } from './settings-symlinks-tab.widget';

type SymlinkServiceStub = Pick<
  SymlinkService,
  'settings' | 'settingsError' | 'saving' | 'fieldErrors' | 'capabilityStates' | 'loadSettings' | 'saveSettings' | 'testCapability'
> & {
  settings: WritableSignal<SymlinkSettings | null>;
  settingsError: WritableSignal<string | null>;
  saving: WritableSignal<boolean>;
  fieldErrors: WritableSignal<Record<string, string>>;
  capabilityStates: WritableSignal<Record<string, SymlinkActionState>>;
};

function createSettings(overrides: Partial<SymlinkSettings> = {}): SymlinkSettings {
  return {
    storageAreas: [{ id: 'area-1', name: 'Media', moviesRoot: '/data/movies', seriesRoot: '/data/series' }],
    pathMappings: [
      {
        id: 'mapping-1',
        kind: 'Radarr' as ArrApplicationKind,
        instanceId: 'radarr-1',
        instanceRoot: '/movies',
        libManagarrRoot: '/data/movies',
      },
    ],
    readerViews: [{ id: 'reader-1', kind: 'Sonarr', root: '/data/series', verification: 'Verified' }],
    revision: 7,
    capabilityChecks: [
      {
        storageAreaId: 'area-1',
        status: 'Available',
        detail: 'The link was written and read back.',
        checkedAt: '2026-11-01T09:00:00.000Z',
        configRevision: 7,
        stale: false,
      },
    ],
    ...overrides,
  };
}

describe('SettingsSymlinksTabWidget', () => {
  let fixture: ComponentFixture<SettingsSymlinksTabWidget>;
  let element: HTMLElement;
  let component: SettingsSymlinksTabWidget;
  let symlinkStub: SymlinkServiceStub;
  let settingsStub: Pick<AppSettingsService, 'localeId' | 'loadInstances' | 'radarrInstances' | 'sonarrInstances'>;

  function value(id: string): string {
    return (element.querySelector('#' + id) as HTMLInputElement).value;
  }

  function click(target: Element | null): void {
    (target as HTMLButtonElement).click();
  }

  /** The row of one card, in the order the form shows them: areas, mappings, reading applications. */
  function row(cardIndex: number): HTMLElement {
    return element.querySelectorAll<HTMLElement>('.card-content .symlink-row')[cardIndex] as HTMLElement;
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  beforeEach(async () => {
    symlinkStub = {
      settings: signal<SymlinkSettings | null>(createSettings()),
      settingsError: signal<string | null>(null),
      saving: signal(false),
      fieldErrors: signal<Record<string, string>>({}),
      capabilityStates: signal<Record<string, SymlinkActionState>>({}),
      loadSettings: jasmine.createSpy('loadSettings').and.resolveTo(),
      saveSettings: jasmine.createSpy('saveSettings').and.callFake(async (draft: SymlinkSettings): Promise<SymlinkSettings> => {
        const saved: SymlinkSettings = { ...draft, revision: 8, capabilityChecks: createSettings().capabilityChecks };
        symlinkStub.settings.set(saved);
        return saved;
      }),
      testCapability: jasmine.createSpy('testCapability').and.callFake(async (): Promise<SymlinkCapabilityCheck> => {
        const answer: SymlinkCapabilityCheck = {
          storageAreaId: 'area-1',
          status: 'PermissionDenied',
          detail: 'The process may not create links here.',
          checkedAt: '2026-11-02T09:00:00.000Z',
          configRevision: 7,
          stale: false,
        };
        // The service replaces the stored answer of that area, which is what the row then reads.
        symlinkStub.settings.update((settings) =>
          settings === null ? settings : { ...settings, capabilityChecks: [answer] },
        );
        return answer;
      }),
    };
    settingsStub = {
      localeId: () => 'en-US',
      loadInstances: jasmine.createSpy('loadInstances').and.resolveTo(),
      radarrInstances: signal([{ id: 'radarr-1', name: 'Movies on the NAS', baseUrl: 'http://localhost:7878', apiKey: 'secret' }]),
      sonarrInstances: signal([{ id: 'sonarr-1', name: 'Series on the NAS', baseUrl: 'http://localhost:8989', apiKey: 'secret' }]),
    } as unknown as Pick<AppSettingsService, 'localeId' | 'loadInstances' | 'radarrInstances' | 'sonarrInstances'>;

    await TestBed.configureTestingModule({
      imports: [SettingsSymlinksTabWidget],
      providers: [
        { provide: SymlinkService, useValue: symlinkStub },
        { provide: AppSettingsService, useValue: settingsStub },
      ],
    }).compileComponents();

    TestBed.inject(AppI18nService).setLanguage('en');
    fixture = TestBed.createComponent(SettingsSymlinksTabWidget);
    component = fixture.componentInstance;
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle();
  });

  it('reads the configuration and the instances it validates against', () => {
    // A mapping names its instance by id, so the choices have to be the ones the server knows.
    expect(symlinkStub.loadSettings).toHaveBeenCalledTimes(1);
    expect(settingsStub.loadInstances).toHaveBeenCalledTimes(1);
  });

  it('fills every row from the saved configuration', () => {
    expect(value('area-name-0')).toBe('Media');
    expect(value('area-movies-0')).toBe('/data/movies');
    expect(value('area-series-0')).toBe('/data/series');
    expect(value('mapping-instance-root-0')).toBe('/movies');
    expect(value('mapping-libmanagarr-root-0')).toBe('/data/movies');
    expect(value('reader-root-0')).toBe('/data/series');
  });

  it('shows the verification of a reader view as a translation nobody can type into', () => {
    const verification: HTMLInputElement = element.querySelector('#reader-verification-0') as HTMLInputElement;

    expect(verification.value).toBe('Verified');
    expect(verification.disabled).toBeTrue();
  });

  it('retires the verified answer of the row whose root was edited', () => {
    const root: HTMLInputElement = element.querySelector('#reader-root-0') as HTMLInputElement;
    root.value = '/data/series-two';
    root.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // The server keeps the state it is sent, so a saved 'Verified' would otherwise certify a root nobody read.
    expect((element.querySelector('#reader-verification-0') as HTMLInputElement).value).toBe('Not verified');
  });

  it('offers the reading application that is still free', () => {
    const addButton: HTMLButtonElement = element.querySelectorAll('.card-header-actions button')[2] as HTMLButtonElement;
    click(addButton);
    fixture.detectChanges();

    const kinds = component['readerViews'].controls.map((view) => view.controls.kind.value);
    expect(kinds).toEqual(['Sonarr', 'MediaPlayer']);
    expect(component['form'].dirty).toBeTrue();
  });

  it('drops the instance chosen under a path mapping whose application moved', () => {
    const mapping = component['pathMappings'].controls[0];
    component['handleMappingKindChanged'](mapping);
    fixture.detectChanges();

    expect(mapping.controls.instanceId.value).toBe('');
  });

  it('sends the rows as typed and keeps the identity of the saved ones', async () => {
    const name: HTMLInputElement = element.querySelector('#area-name-0') as HTMLInputElement;
    name.value = 'Media shelf';
    name.dispatchEvent(new Event('input'));
    click(element.querySelectorAll('.card-header-actions button')[0] as HTMLButtonElement);
    fixture.detectChanges();

    await component['saveAsync']();
    await settle();

    const draft: SymlinkSettings = (symlinkStub.saveSettings as jasmine.Spy).calls.argsFor(0)[0] as SymlinkSettings;
    expect(draft.storageAreas).toEqual([
      { id: 'area-1', name: 'Media shelf', moviesRoot: '/data/movies', seriesRoot: '/data/series' },
      { id: '', name: '', moviesRoot: '', seriesRoot: '' },
    ]);
    // The new row carries an empty id, which is what makes the server give it one.
    expect(draft.readerViews).toEqual([{ id: 'reader-1', kind: 'Sonarr', root: '/data/series', verification: 'Verified' }]);
    expect(draft.revision).toBe(7);
  });

  it('says so when the saved configuration came back', async () => {
    await component['saveAsync']();
    await settle();

    expect(element.querySelector('.save-feedback')?.textContent).toContain('The path configuration was saved.');
    expect(component['form'].dirty).toBeFalse();
  });

  it('puts the refused field of a save answer beside the control it belongs to', async () => {
    (symlinkStub.saveSettings as jasmine.Spy).and.callFake(async (): Promise<SymlinkSettings> => {
      symlinkStub.fieldErrors.set({ 'symlinks.storageAreas[0].moviesRoot': 'The movie root is required.' });
      symlinkStub.settingsError.set('The path configuration could not be saved.');
      throw new Error('The path configuration could not be saved.');
    });

    await component['saveAsync']();
    await settle();

    expect(row(0).querySelector('.field-error')?.textContent).toContain('The movie root is required.');
    expect(element.querySelector('.feedback.error')?.textContent).toContain('The path configuration could not be saved.');
  });

  it('keeps the draft when a save was refused', async () => {
    (symlinkStub.saveSettings as jasmine.Spy).and.callFake(async (): Promise<SymlinkSettings> => {
      symlinkStub.settingsError.set('The path configuration could not be saved.');
      throw new Error('The path configuration could not be saved.');
    });
    const name: HTMLInputElement = element.querySelector('#area-name-0') as HTMLInputElement;
    name.value = 'Media shelf';
    name.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    await component['saveAsync']();
    await settle();

    // Rebuilding the form from the server would throw away the edit that was meant to fix the refusal.
    expect(value('area-name-0')).toBe('Media shelf');
  });

  it('puts the saved rows back when the draft is discarded', async () => {
    const name: HTMLInputElement = element.querySelector('#area-name-0') as HTMLInputElement;
    name.value = 'Media shelf';
    name.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    await component['discardAsync']();
    await settle();

    expect(value('area-name-0')).toBe('Media');
    expect(component['form'].dirty).toBeFalse();
  });

  it('names the tab in the warning before a draft is left behind', () => {
    component['form'].markAsDirty();

    expect(component['collectUnsavedChangeDescriptions']()).toEqual(['Symlink paths']);
  });

  it('runs the symlink test for its own storage area and shows the answer it brought back', async () => {
    click(element.querySelector('.capability-actions button'));
    await settle();

    expect(symlinkStub.testCapability).toHaveBeenCalledWith('area-1');
    expect(element.querySelector('.capability-status')?.textContent).toContain('No permission for symlinks');
    expect(element.querySelector('.capability-detail')?.textContent).toContain('The process may not create links here.');
  });

  it('shows the answer that came with a failed test beside its storage area', async () => {
    (symlinkStub.testCapability as jasmine.Spy).and.callFake(async (): Promise<never> => {
      symlinkStub.capabilityStates.set({ 'area-1': { busy: false, error: 'The storage area is unreachable.' } });
      throw new Error('The storage area is unreachable.');
    });

    await component['testCapabilityAsync'](createSettings().capabilityChecks[0]);
    await settle();

    expect(element.querySelector('.capability-detail')?.textContent).toContain('The storage area is unreachable.');
  });

  it('keeps a failed load visible above the form', async () => {
    symlinkStub.settingsError.set('The path configuration could not be loaded.');
    fixture.detectChanges();

    expect(element.querySelector('.feedback.error')?.textContent).toContain('The path configuration could not be loaded.');
  });
});
