import { MatDialog } from '@angular/material/dialog';
import type { Mock } from 'vitest';
import { of } from 'rxjs';
import { signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SymlinkSettings } from '../../../interfaces/symlinks/symlink-settings';
import { AppI18nService } from '../../../services/app-i18n.service';
import { AppSettingsService } from '../../../services/app-settings.service';
import { SymlinkActionState, SymlinkService } from '../../../services/symlink.service';
import { SettingsSymlinksTabWidget } from './settings-symlinks-tab.widget';

type SymlinkServiceStub = Pick<
  SymlinkService,
  | 'settings'
  | 'settingsError'
  | 'saving'
  | 'fieldErrors'
  | 'capabilityStates'
  | 'loadSettings'
  | 'saveSettings'
  | 'checkCapabilities'
  | 'capabilityChecking'
  | 'capabilityError'
  | 'settingsLoading'
> & {
  settings: WritableSignal<SymlinkSettings | null>;
  settingsError: WritableSignal<string | null>;
  saving: WritableSignal<boolean>;
  fieldErrors: WritableSignal<Record<string, string>>;
  capabilityStates: WritableSignal<Record<string, SymlinkActionState>>;
  capabilityChecking: WritableSignal<boolean>;
  capabilityError: WritableSignal<string | null>;
  settingsLoading: WritableSignal<boolean>;
};

function createSettings(overrides: Partial<SymlinkSettings> = {}): SymlinkSettings {
  return {
    mediaRoot: '/media',
    storageAreas: [{ id: 'area-1', name: 'Media', moviesRoot: '/data/movies', seriesRoot: '/data/series' }],
    pathMappings: [],
    readerViews: [],
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
  let dialogStub: { open: Mock };
  let settingsStub: Pick<AppSettingsService, 'localeId' | 'loadInstances' | 'radarrInstances' | 'sonarrInstances'>;

  function value(id: string): string {
    return (element.querySelector('#' + id) as HTMLInputElement).value;
  }

  function click(target: Element | null): void {
    (target as HTMLButtonElement).click();
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
      capabilityChecking: signal(false),
      capabilityError: signal<string | null>(null),
      settingsLoading: signal(false),
      fieldErrors: signal<Record<string, string>>({}),
      capabilityStates: signal<Record<string, SymlinkActionState>>({}),
      loadSettings: vi.fn().mockResolvedValue(undefined),
      saveSettings: vi.fn().mockImplementation(async (draft: SymlinkSettings): Promise<SymlinkSettings> => {
        const saved: SymlinkSettings = { ...createSettings(), ...draft, revision: 8, capabilityChecks: createSettings().capabilityChecks };
        symlinkStub.settings.set(saved);
        return saved;
      }),
      checkCapabilities: vi.fn().mockResolvedValue(undefined),
    };
    dialogStub = { open: vi.fn().mockReturnValue({ afterClosed: () => of(undefined) }) };
    settingsStub = {
      localeId: () => 'en-US',
      loadInstances: vi.fn().mockResolvedValue(undefined),
      radarrInstances: signal([{ id: 'radarr-1', name: 'Movies on the NAS', baseUrl: 'http://localhost:7878', apiKey: 'secret' }]),
      sonarrInstances: signal([{ id: 'sonarr-1', name: 'Series on the NAS', baseUrl: 'http://localhost:8989', apiKey: 'secret' }]),
    } as unknown as Pick<AppSettingsService, 'localeId' | 'loadInstances' | 'radarrInstances' | 'sonarrInstances'>;

    await TestBed.configureTestingModule({
      imports: [SettingsSymlinksTabWidget],
      providers: [
        { provide: MatDialog, useValue: dialogStub },
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

  function editRoot(path: string): void {
    const input = element.querySelector('#symlink-media-root') as HTMLInputElement;
    input.value = path;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('preserves saved activation during startup verification and recovers automatically', async () => {
    symlinkStub.settings.set(createSettings({ enabled: true, capabilityChecks: [] }));
    symlinkStub.capabilityChecking.set(true);
    fixture.detectChanges();
    let toggle = element.querySelector('mat-slide-toggle button') as HTMLButtonElement;
    expect(toggle.disabled).toBe(true);
    expect(toggle.getAttribute('aria-checked')).toBe('true');
    symlinkStub.capabilityChecking.set(false);
    symlinkStub.capabilityError.set('Sonarr is starting');
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-checked')).toBe('true');
    expect(symlinkStub.saveSettings).not.toHaveBeenCalled();
    symlinkStub.capabilityError.set(null);
    symlinkStub.settings.set(createSettings({ enabled: true }));
    fixture.detectChanges();
    await settle();
    toggle = element.querySelector('mat-slide-toggle button') as HTMLButtonElement;
    expect(toggle.disabled).toBe(false);
    expect(toggle.getAttribute('aria-checked')).toBe('true');
  });

  it('loads one media directory and explains the shared mount requirement', () => {
    expect(symlinkStub.loadSettings).toHaveBeenCalledTimes(1);
    expect(value('symlink-media-root')).toBe('/media');
    const hint = element.querySelector('#symlink-structure-hint')?.textContent;
    expect(hint).toContain('same path');
    expect(hint).toContain('Sonarr, Radarr, LibManagarr');
    expect(hint).toContain('media player');
    expect(hint).toContain('root folders');
    expect(element.querySelectorAll('input').length).toBe(1);
    expect(element.textContent).not.toContain('Add storage area');
    expect(element.textContent).not.toContain('Add path mapping');
  });

  it('defaults to /media when no configuration has been loaded', () => {
    symlinkStub.settings.set(null);
    fixture.detectChanges();
    expect(value('symlink-media-root')).toBe('/media');
  });

  it('autosaves the media directory on blur and shows feedback beside the field', async () => {
    editRoot('/data/media');
    element.querySelector('#symlink-media-root')!.dispatchEvent(new Event('blur'));
    await settle();
    expect(symlinkStub.saveSettings).toHaveBeenCalledWith({ mediaRoot: '/data/media', enabled: true });
    expect(value('symlink-media-root')).toBe('/data/media');
    expect(component['form'].dirty).toBe(false);
    expect(element.querySelector('.field-control-wrap .feedback-icon.success')).not.toBeNull();
  });

  it('has no manual save or discard buttons and skips unchanged values', async () => {
    expect(element.textContent).not.toContain('Save settings');
    expect(element.textContent).not.toContain('Discard changes');
    element.querySelector('#symlink-media-root')!.dispatchEvent(new Event('blur'));
    await settle();
    expect(symlinkStub.saveSettings).not.toHaveBeenCalled();
    editRoot(' /media ');
    element.querySelector('#symlink-media-root')!.dispatchEvent(new Event('blur'));
    await settle();
    expect(symlinkStub.saveSettings).not.toHaveBeenCalled();
    expect(component['form'].dirty).toBe(false);
  });

  it('autosaves on Enter without submitting the form or saving again on blur', async () => {
    editRoot('/new/media');
    const input = element.querySelector('#symlink-media-root')!;
    const enter = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
    input.dispatchEvent(enter);
    await settle();
    expect(enter.defaultPrevented).toBe(true);
    expect(symlinkStub.saveSettings).toHaveBeenCalledExactlyOnceWith({ mediaRoot: '/new/media', enabled: true });
    input.dispatchEvent(new Event('blur'));
    await settle();
    expect(symlinkStub.saveSettings).toHaveBeenCalledTimes(1);
  });

  it('shows loading feedback and prevents edits and duplicate saves while saving', async () => {
    let finish!: () => void;
    vi.mocked(symlinkStub.saveSettings).mockImplementation(() => {
      symlinkStub.saving.set(true);
      return new Promise<SymlinkSettings>((resolve) => {
        finish = () => {
          const saved = createSettings({ mediaRoot: '/new/media', enabled: true });
          symlinkStub.settings.set(saved);
          symlinkStub.saving.set(false);
          resolve(saved);
        };
      });
    });
    editRoot('/new/media');
    const input = element.querySelector('#symlink-media-root') as HTMLInputElement;
    input.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    expect(input.disabled).toBe(true);
    expect(element.querySelector('.field-control-wrap .feedback-icon.spinning')).not.toBeNull();
    input.dispatchEvent(new Event('blur'));
    expect(symlinkStub.saveSettings).toHaveBeenCalledTimes(1);
    finish();
    await settle();
    expect(input.disabled).toBe(false);
    expect(component['form'].dirty).toBe(false);
    expect(element.querySelector('.field-control-wrap .feedback-icon.success')).not.toBeNull();
  });

  it('places validation errors beside the media directory and keeps the draft', async () => {
    editRoot('relative/path');
    vi.mocked(symlinkStub.saveSettings).mockImplementation(async (): Promise<never> => {
      symlinkStub.fieldErrors.set({ 'symlinks.mediaRoot': 'An absolute media directory is required.' });
      symlinkStub.settingsError.set('The path configuration could not be saved.');
      throw new Error('validation');
    });
    await component['saveAsync']();
    await settle();
    expect(element.querySelector('.field-error')?.textContent).toContain('An absolute media directory is required.');
    expect(element.querySelector('.feedback.error')).toBeNull();
    expect(element.querySelector('.field-control-wrap .feedback-icon.error')).not.toBeNull();
    expect(value('symlink-media-root')).toBe('relative/path');
    expect(component['form'].dirty).toBe(true);
    editRoot('/media');
    expect(element.querySelector('.field-error')).toBeNull();
    expect(element.querySelector('.feedback.error')).toBeNull();
  });

  it('restores the saved directory when changes are discarded', async () => {
    editRoot('/other/media');
    component['discardUnsavedChanges']();
    await settle();
    expect(value('symlink-media-root')).toBe('/media');
    expect(component['form'].dirty).toBe(false);
  });

  it('keeps an unsaved directory when a background result arrives', () => {
    editRoot('/draft/media');
    symlinkStub.settings.set(createSettings({ revision: 8 }));
    fixture.detectChanges();
    expect(value('symlink-media-root')).toBe('/draft/media');
  });

  it('names the tab before leaving unsaved changes', () => {
    component['form'].markAsDirty();
    expect(component['collectUnsavedChangeDescriptions']()).toEqual(['Symlink paths']);
  });

  it('checks automatically and shows no separate test section', () => {
    expect(symlinkStub.checkCapabilities).toHaveBeenCalledTimes(1);
    expect(element.querySelectorAll('form > .card').length).toBe(2);
    expect(element.textContent).not.toContain('Symlink test');
    expect(element.querySelector('.capability-row')).toBeNull();
  });

  for (const status of ['Unsupported', 'PermissionDenied', 'StorageUnavailable', 'Failed', 'NotTested'] as const) {
    it(`keeps the switch off and disabled for ${status}`, async () => {
      const check = { ...createSettings().capabilityChecks[0], status, detail: 'This media directory cannot create symlinks.' };
      symlinkStub.settings.set(createSettings({ enabled: false, capabilityChecks: [check] }));
      await settle();
      const toggle = element.querySelector('mat-slide-toggle button') as HTMLButtonElement;
      expect(toggle.disabled).toBe(true);
      expect(toggle.getAttribute('aria-checked')).toBe('false');
      expect(element.querySelector('.enable-hint')?.textContent).toContain('cannot create symlinks');
      expect((element.querySelector('#symlink-media-root') as HTMLInputElement).disabled).toBe(false);
    });
  }

  it('prevents enabling while checking and unlocks only after a successful check', async () => {
    symlinkStub.capabilityChecking.set(true);
    await settle();
    expect((element.querySelector('mat-slide-toggle button') as HTMLButtonElement).disabled).toBe(true);
    expect(element.querySelector('.enable-hint')?.textContent).toContain('Checking symlink support');
    symlinkStub.capabilityChecking.set(false);
    await settle();
    expect((element.querySelector('mat-slide-toggle button') as HTMLButtonElement).disabled).toBe(false);
  });

  it('keeps the switch disabled when automatic verification fails', async () => {
    symlinkStub.capabilityError.set('The server could not verify symlink support.');
    await settle();
    expect((element.querySelector('mat-slide-toggle button') as HTMLButtonElement).disabled).toBe(true);
    expect(element.querySelector('.enable-hint')?.textContent).toContain('could not verify');
  });

  it('keeps failed loads visible', () => {
    symlinkStub.settingsError.set('The path configuration could not be loaded.');
    fixture.detectChanges();
    expect(element.querySelector('.feedback.error')?.textContent).toContain('The path configuration could not be loaded.');
  });
  it('autosaves the enable switch and keeps existing paths', async () => {
    click(element.querySelector('mat-slide-toggle button'));
    await settle();
    expect(symlinkStub.saveSettings).toHaveBeenCalledExactlyOnceWith({ mediaRoot: '/media', enabled: false });
    expect(symlinkStub.settings()?.enabled).toBe(false);
    expect(element.querySelector('.enabled-control app-feedback-icon .material-symbols-outlined')).not.toBeNull();
  });

  it('allows enabling after a positive automatic check without enabling it automatically', async () => {
    symlinkStub.settings.set(createSettings({ enabled: false }));
    await settle();
    const toggle = element.querySelector('mat-slide-toggle button') as HTMLButtonElement;
    expect(toggle.disabled).toBe(false);
    expect(toggle.getAttribute('aria-checked')).toBe('false');
    click(toggle);
    await settle();
    expect(symlinkStub.saveSettings).toHaveBeenCalledExactlyOnceWith({ mediaRoot: '/media', enabled: true });
  });

  it('shows a formatted tree with a relative special link', () => {
    const tree = element.querySelector('pre')?.textContent ?? '';
    expect(tree).toContain('/media/');
    expect(tree).toContain('movies/');
    expect(tree).toContain('series/');
    expect(tree).toContain('../../../movies/Example Movie/movie.mkv');
  });

  it('autosaves a directory selected in the browser', async () => {
    dialogStub.open.mockReturnValue({ afterClosed: () => of('/selected/media') });
    click(element.querySelector('.media-root-controls button'));
    await settle();
    expect(value('symlink-media-root')).toBe('/selected/media');
    expect(symlinkStub.saveSettings).toHaveBeenCalledExactlyOnceWith({ mediaRoot: '/selected/media', enabled: true });
  });

  for (const result of [undefined, null, '', '   ']) {
    it(`keeps the directory when the browser returns ${JSON.stringify(result)}`, async () => {
      dialogStub.open.mockReturnValue({ afterClosed: () => of(result) });
      editRoot('/my/media');
      click(element.querySelector('.media-root-controls button'));
      await settle();
      expect(value('symlink-media-root')).toBe('/my/media');
      expect(symlinkStub.saveSettings).not.toHaveBeenCalled();
    });
  }
});
