import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ArrInstanceSettings } from '../interfaces/settings/arr-instance-settings';
import { SeerrInstanceSettings } from '../interfaces/settings/seerr-instance-settings';
import { AppI18nService } from './app-i18n.service';
import { AppSettingsService, ValidationProblemError } from './app-settings.service';

describe('AppSettingsService', () => {
  let service: AppSettingsService;
  let http: HttpTestingController;
  let i18n: jasmine.SpyObj<AppI18nService>;
  let originalLanguage: string;
  let originalTheme: string | undefined;

  beforeEach(() => {
    originalLanguage = document.documentElement.lang;
    originalTheme = document.documentElement.dataset['theme'];
    i18n = jasmine.createSpyObj<AppI18nService>('AppI18nService', ['t', 'setLanguage']);
    i18n.t.and.callFake((key) => key);
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AppI18nService, useValue: i18n }],
    });
    service = TestBed.inject(AppSettingsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    document.documentElement.lang = originalLanguage;
    if (originalTheme === undefined) delete document.documentElement.dataset['theme'];
    else document.documentElement.dataset['theme'] = originalTheme;
  });

  function instance(id = 's1'): ArrInstanceSettings {
    return { id, name: 'Sonarr Main', baseUrl: 'http://sonarr', apiKey: 'test-key' };
  }

  async function loadInstances(): Promise<void> {
    const pending = service.loadInstances();
    http.expectOne('/api/settings/instances').flush({ sonarrInstances: [instance()], radarrInstances: [] });
    await pending;
  }

  it('shares simultaneous base settings requests and caches the loaded state', async () => {
    const first = service.load();
    const second = service.load();
    const request = http.expectOne('/api/settings/general');
    request.flush({ language: 'De', theme: 'Dark' });
    await Promise.all([first, second]);
    await service.load();

    http.expectNone('/api/settings/general');
    expect(service.language()).toBe('de');
    expect(service.localeId()).toBe('de');
    expect(service.theme()).toBe('dark');
    expect(service.loadError()).toBeNull();
    expect(i18n.setLanguage).toHaveBeenCalledOnceWith('de');
    expect(document.documentElement.lang).toBe('de');
    expect(document.documentElement.dataset['theme']).toBe('dark');
  });

  it('keeps usable defaults and a readable error when initial settings fail', async () => {
    const pending = service.load();
    http.expectOne('/api/settings/general').flush({ detail: 'Settings unavailable' }, { status: 503, statusText: 'Unavailable' });
    await pending;

    expect(service.language()).toBe('en');
    expect(service.theme()).toBe('system');
    expect(service.loadError()).toBe('Settings unavailable');
    expect(i18n.setLanguage).toHaveBeenCalledWith('en');
  });

  it('resolves the default theme from the operating-system preference', () => {
    const matchMediaSpy: jasmine.Spy = spyOn(globalThis, 'matchMedia').and.returnValue({ matches: true } as MediaQueryList);

    service.applyDefaultTheme();
    expect(document.documentElement.dataset['theme']).toBe('dark');

    matchMediaSpy.and.returnValue({ matches: false } as MediaQueryList);
    service.applyDefaultTheme();
    expect(document.documentElement.dataset['theme']).toBe('light');
  });

  it('updates language and theme only after a successful save', async () => {
    const pending = service.saveGeneral({ language: 'fr', theme: 'light' });
    const request = http.expectOne('/api/settings/general');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ language: 'Fr', theme: 'Light' });
    expect(service.language()).toBe('en');
    request.flush(null);
    await pending;

    expect(service.language()).toBe('fr');
    expect(service.localeId()).toBe('fr');
    expect(service.theme()).toBe('light');
    expect(document.documentElement.lang).toBe('fr');
  });

  it('maps validation errors without changing saved settings', async () => {
    const pending = service.saveGeneral({ language: 'de', theme: 'dark' }).then(
      (): never => {
        throw new Error('Expected request failure');
      },
      (error) => error as ValidationProblemError,
    );
    http.expectOne('/api/settings/general').flush(
      {
        title: 'Validation failed',
        detail: 'Invalid settings',
        errors: { language: ['Unsupported language', '', 42], theme: 'malformed', other: ['  '] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    const error = await pending;

    expect(error).toEqual(jasmine.any(ValidationProblemError));
    expect(error!.message).toBe('Unsupported language');
    expect(error!.fieldErrors).toEqual({ language: ['Unsupported language'] });
    expect(service.language()).toBe('en');
    expect(service.theme()).toBe('system');
    expect(i18n.setLanguage).not.toHaveBeenCalled();
  });

  it('does not replace an instance in memory when saving fails', async () => {
    await loadInstances();
    const pending = service.saveSonarrInstance(0, { ...instance(), name: 'Changed' }).then(
      (): never => {
        throw new Error('Expected request failure');
      },
      (error) => error as Error,
    );
    http.expectOne('/api/settings/Sonarr/0').flush({ detail: 'Cannot save' }, { status: 500, statusText: 'Error' });

    expect((await pending)!.message).toBe('Cannot save');
    expect(service.sonarrInstances()).toEqual([instance()]);
  });

  it('updates and deletes instances after successful responses without affecting the other application', async () => {
    await loadInstances();
    const updated = { ...instance(), name: 'Updated' };
    const saving = service.saveSonarrInstance(0, updated);
    http.expectOne('/api/settings/Sonarr/0').flush(null);
    await saving;
    expect(service.sonarrInstances()).toEqual([updated]);
    expect(service.radarrInstances()).toEqual([]);

    const deleting = service.deleteSonarrInstance(0);
    const request = http.expectOne('/api/settings/Sonarr/0');
    expect(request.request.method).toBe('DELETE');
    expect(service.sonarrInstances()).toEqual([updated]);
    request.flush(null);
    await deleting;
    expect(service.sonarrInstances()).toEqual([]);
  });

  function seerrInstance(): SeerrInstanceSettings {
    return { id: 'seerr-1', baseUrl: 'http://seerr', apiKey: 'test-key' };
  }

  it('loads the Seerr instance and clears the state when none is configured', async () => {
    const pending = service.loadSeerrInstance();
    http.expectOne('/api/settings/seerr').flush(seerrInstance());
    await pending;
    expect(service.seerrInstance()).toEqual(seerrInstance());

    const pendingEmpty = service.loadSeerrInstance();
    http.expectOne('/api/settings/seerr').flush(null, { status: 204, statusText: 'No Content' });
    await pendingEmpty;
    expect(service.seerrInstance()).toBeNull();
  });

  it('saves and deletes the Seerr instance only after successful responses', async () => {
    const saved: SeerrInstanceSettings = { ...seerrInstance(), baseUrl: 'http://updated-seerr' };
    const saving = service.saveSeerrInstance(saved);
    const saveRequest = http.expectOne('/api/settings/seerr');
    expect(saveRequest.request.method).toBe('PUT');
    expect(saveRequest.request.body).toEqual(saved);
    saveRequest.flush(null);
    await saving;
    expect(service.seerrInstance()).toEqual(saved);

    const deleting = service.deleteSeerrInstance();
    const deleteRequest = http.expectOne('/api/settings/seerr');
    expect(deleteRequest.request.method).toBe('DELETE');
    expect(service.seerrInstance()).toEqual(saved);
    deleteRequest.flush(null);
    await deleting;
    expect(service.seerrInstance()).toBeNull();
  });

  it('keeps the saved Seerr instance when saving fails', async () => {
    const loading = service.loadSeerrInstance();
    http.expectOne('/api/settings/seerr').flush(seerrInstance());
    await loading;

    const pending = service.saveSeerrInstance({ ...seerrInstance(), baseUrl: 'http://changed-seerr' }).then(
      (): never => {
        throw new Error('Expected request failure');
      },
      (error) => error as Error,
    );
    http.expectOne('/api/settings/seerr').flush({ detail: 'Cannot save' }, { status: 500, statusText: 'Error' });

    expect((await pending)!.message).toBe('Cannot save');
    expect(service.seerrInstance()).toEqual(seerrInstance());
  });

  it('clears the syncing state after a successful response with missing optional fields', async () => {
    const pending = service.syncInstance('Sonarr', 's1');
    expect(service.isSyncing('Sonarr', 's1')).toBeTrue();
    const request = http.expectOne('/api/settings/sync');
    expect(request.request.body).toEqual({ kind: 'Sonarr', instanceId: 's1' });
    request.flush({});

    expect(await pending).toEqual({ message: '', syncedAt: null, itemCount: 0 });
    expect(service.isSyncing('Sonarr', 's1')).toBeFalse();
  });

  it('clears the syncing state and propagates server errors after a failed sync', async () => {
    const pending = service.syncInstance('Radarr', 'r1').then(
      (): never => {
        throw new Error('Expected request failure');
      },
      (error) => error as Error,
    );
    expect(service.isSyncing('Radarr', 'r1')).toBeTrue();
    http.expectOne('/api/settings/sync').flush({ title: 'Sync failed' }, { status: 502, statusText: 'Bad Gateway' });

    expect((await pending)!.message).toBe('Sync failed');
    expect(service.isSyncing('Radarr', 'r1')).toBeFalse();
  });

  it('keeps a running synchronization of another instance while one finishes', async () => {
    const firstSync = service.syncInstance('Sonarr', 's1');
    const secondSync = service.syncInstance('Radarr', 'r2');
    const requests = http.match('/api/settings/sync');

    expect(requests).toHaveSize(2);
    expect(service.isSyncing('Sonarr', 's1')).toBeTrue();
    expect(service.isSyncing('Radarr', 'r2')).toBeTrue();

    requests[0].flush({ itemCount: 3 });
    expect(await firstSync).toEqual({ message: '', syncedAt: null, itemCount: 3 });
    expect(service.isSyncing('Sonarr', 's1')).toBeFalse();
    expect(service.isSyncing('Radarr', 'r2')).toBeTrue();

    requests[1].flush({ itemCount: 1 });
    await secondSync;
    expect(service.isSyncing('Radarr', 'r2')).toBeFalse();
  });

  it('keeps the fixed log path and updates settings only after success', async () => {
    const logging = { path: 'new-logs', retentionDays: 7, minimumLevel: 'Warning' as const };
    const pending = service.saveLoggingSettings(logging);
    const request = http.expectOne('/api/settings/logging');
    expect(request.request.body).toEqual({ logging: { ...logging, path: './logs' } });
    expect(service.logging().path).toBe('./logs');
    request.flush(null);
    await pending;
    expect(service.logging()).toEqual({ ...logging, path: './logs' });
  });

  it('loads title rules with defaults for old installations', async () => {
    const pending = service.load();
    http.expectOne('/api/settings/general').flush({ language: 'En', theme: 'System' });
    await pending;
    expect(service.titleSorting()).toEqual({ enabled: false, ignoredLeadingWords: [] });
  });

  it('loads sign-in protection limits with defaults for old installations', async () => {
    const pending = service.load();
    http.expectOne('/api/settings/general').flush({ language: 'En', theme: 'System' });
    await pending;
    expect(service.authProtection()).toEqual({ requestsPerMinute: 10, maxFailedAttempts: 5 });
  });

  it('fills only the missing half of a partial sign-in protection section', async () => {
    const pending = service.load();
    http.expectOne('/api/settings/general').flush({ language: 'En', theme: 'System', authProtection: { requestsPerMinute: 3 } });
    await pending;
    expect(service.authProtection()).toEqual({ requestsPerMinute: 3, maxFailedAttempts: 5 });
  });

  it('uses confirmed normalized title rules and preserves them during general saves', async () => {
    const rules = { enabled: true, ignoredLeadingWords: ['  The ', 'the', 'A'] };
    const pending = service.saveTitleSorting(rules);
    const request = http.expectOne('/api/settings/title-sorting');
    expect(request.request.body).toEqual(rules);
    expect(service.titleSorting()).toEqual({ enabled: false, ignoredLeadingWords: [] });
    request.flush({ enabled: true, ignoredLeadingWords: ['The', 'A'] });
    await pending;
    rules.ignoredLeadingWords.push('An');
    expect(service.titleSorting()).toEqual({ enabled: true, ignoredLeadingWords: ['The', 'A'] });
    const generalSave = service.saveGeneral({ language: 'de', theme: 'dark' });
    const generalRequest = http.expectOne('/api/settings/general');
    expect(generalRequest.request.body).toEqual({ language: 'De', theme: 'Dark' });
    generalRequest.flush(null);
    await generalSave;
    expect(service.titleSorting()).toEqual({ enabled: true, ignoredLeadingWords: ['The', 'A'] });
  });

  it('preserves saved title rules on validation and network failures', async () => {
    const loaded = service.load();
    http
      .expectOne('/api/settings/general')
      .flush({ language: 'En', theme: 'System', titleSorting: { enabled: true, ignoredLeadingWords: ['The'] } });
    await loaded;
    const pending = service.saveTitleSorting({ enabled: false, ignoredLeadingWords: ['two words'] });
    const rejected = expectAsync(pending).toBeRejectedWith(jasmine.any(ValidationProblemError));
    http
      .expectOne('/api/settings/title-sorting')
      .flush({ errors: { 'titleSorting.ignoredLeadingWords[0]': ['Single word required'] } }, { status: 400, statusText: 'Bad Request' });
    await rejected;
    expect(service.titleSorting()).toEqual({ enabled: true, ignoredLeadingWords: ['The'] });
    const network = service.saveTitleSorting({ enabled: false, ignoredLeadingWords: [] });
    const networkRejected = expectAsync(network).toBeRejectedWith(jasmine.any(Error));
    http.expectOne('/api/settings/title-sorting').error(new ProgressEvent('error'));
    await networkRejected;
    expect(service.titleSorting()).toEqual({ enabled: true, ignoredLeadingWords: ['The'] });
  });
});
