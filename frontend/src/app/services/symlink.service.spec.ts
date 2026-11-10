import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SymlinkSettings } from '../interfaces/symlinks/symlink-settings';
import { SymlinkLink } from '../interfaces/symlinks/symlink-link';
import { SymlinkSuggestion } from '../interfaces/symlinks/symlink-suggestion';
import { AppI18nService } from './app-i18n.service';
import { SymlinkService } from './symlink.service';

describe('SymlinkService', () => {
  let service: SymlinkService;
  let http: HttpTestingController;

  const base = '/api/settings/symlinks';
  const areaId = 'area-1';

  const settings = (revision = 0): SymlinkSettings => ({
    mediaRoot: '/media',
    storageAreas: [{ id: areaId, name: 'Media', moviesRoot: '/media/movies', seriesRoot: '/media/series' }],
    pathMappings: [{ id: 'map-1', instanceId: 'radarr-1', kind: 'Radarr', instanceRoot: '/data/movies', libManagarrRoot: '/media/movies' }],
    readerViews: [{ id: 'view-1', kind: 'Sonarr', root: '/media/series', verification: 'Unverified' }],
    revision,
    capabilityChecks: [
      { storageAreaId: areaId, status: 'NotTested', detail: 'Not tested yet.', checkedAt: null, configRevision: revision, stale: true },
    ],
  });

  const suggestion = (id = 'sug-1'): SymlinkSuggestion => ({
    id,
    matcherVersion: 'v1',
    status: 'Pending',
    radarrInstanceId: 'radarr-1',
    radarrMovieId: 70,
    radarrMovieTitle: 'Un Atto Di Violenza',
    radarrFileId: 4321,
    radarrFilePath: '/media/movies/un-atto.mkv',
    sonarrInstanceId: 'sonarr-1',
    sonarrSeriesId: 12,
    sonarrSeriesTitle: 'Specials',
    sonarrEpisodeId: 900,
    sonarrEpisodeTitle: 'Un Atto Di Violenza',
    seasonNumber: 0,
    episodeNumber: 1,
    reasons: [{ code: 'title-match', detail: 'Titles match.' }],
    alternatives: [],
    detail: null,
    configRevision: 0,
    stale: false,
    linkId: null,
    revision: 0,
    createdAt: '2026-11-01T10:00:00Z',
    updatedAt: '2026-11-01T10:00:00Z',
    decidedAt: null,
  });

  const link = (id = 'link-1', revision = 0): SymlinkLink => ({
    id,
    radarrInstanceId: 'radarr-1',
    radarrMovieId: 70,
    radarrMovieTitle: 'Un Atto Di Violenza',
    radarrFileId: 4321,
    sonarrInstanceId: 'sonarr-1',
    sonarrSeriesId: 12,
    sonarrSeriesTitle: 'Specials',
    sonarrEpisodeId: 900,
    sonarrEpisodeTitle: 'Un Atto Di Violenza',
    seasonNumber: 0,
    episodeNumber: 1,
    storageAreaId: areaId,
    linkPath: '/media/series/specials/s01e01.mkv',
    linkText: '/media/movies/un-atto.mkv',
    sourcePath: '/media/movies/un-atto.mkv',
    operationStatus: 'Active',
    setupStep: 'Completed',
    findings: [],
    monitoringOriginal: true,
    monitoringDisabled: true,
    autoFollowUp: true,
    desiredFilename: 'un-atto.mkv',
    appliedFilename: 'un-atto.mkv',
    sourceVersion: 'radarr:4321:5368709120',
    expectedMetadata: null,
    confirmedMetadata: null,
    suggestionId: 'sug-1',
    revision,
    createdAt: '2026-11-01T10:00:00Z',
    updatedAt: '2026-11-01T10:00:00Z',
    lastCheckedAt: null,
    lastSuccessfulCheckAt: null,
  });

  /** Drains the microtasks a flushed response hands through before the next request leaves. */
  const settle = async (times = 8): Promise<void> => {
    for (let index = 0; index < times; index += 1) {
      await Promise.resolve();
    }
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AppI18nService, useValue: { t: (key: string) => key } }],
    });
    service = TestBed.inject(SymlinkService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('reads the path configuration with its probe answers', async () => {
    const pending = service.loadSettings();
    expect(service.settingsLoading()).toBeTrue();
    http.expectOne(base).flush(settings());
    await pending;
    expect(service.settings()?.storageAreas).toHaveSize(1);
    expect(service.settingsLoading()).toBeFalse();
    expect(service.settingsError()).toBeNull();
  });

  it('reports a failed settings read instead of showing an empty form as unconfigured', async () => {
    const pending = service.loadSettings().catch((error: unknown) => error as Error);
    http.expectOne(base).flush({ title: 'Nope' }, { status: 500, statusText: 'Error' });
    expect((await pending)?.message).toBeDefined();
    expect(service.settingsError()).toBe('Nope');
    expect(service.settingsLoading()).toBeFalse();
  });

  it('checks support automatically even while disabled and updates the returned effective state', async () => {
    const pending = service.checkCapabilities();
    expect(service.capabilityChecking()).toBeTrue();
    const request = http.expectOne(`${base}/capability-check`);
    expect(request.request.method).toBe('POST');
    request.flush({
      ...settings(),
      enabled: false,
      capabilityChecks: [{ storageAreaId: areaId, status: 'Unsupported', detail: 'Symlinks are unsupported.', stale: false }],
    });
    await pending;
    expect(service.settings()?.enabled).toBeFalse();
    expect(service.settings()?.capabilityChecks[0].status).toBe('Unsupported');
    expect(service.capabilityChecking()).toBeFalse();
  });

  it('reports automatic verification failures separately from settings saves', async () => {
    const pending = service.checkCapabilities().catch(() => undefined);
    http.expectOne(`${base}/capability-check`).flush({ title: 'Verification unavailable' }, { status: 503, statusText: 'Unavailable' });
    await pending;
    expect(service.capabilityError()).toBe('Verification unavailable');
    expect(service.settingsError()).toBeNull();
    expect(service.capabilityChecking()).toBeFalse();
  });

  it('does not let an old automatic check replace settings saved afterwards', async () => {
    const check = service.checkCapabilities();
    const oldRequest = http.expectOne(`${base}/capability-check`);
    const save = service.saveSettings({ mediaRoot: '/new-media', enabled: false });
    http.expectOne(base).flush({ ...settings(), mediaRoot: '/new-media', enabled: false });
    await save;
    oldRequest.flush({ ...settings(), mediaRoot: '/old-media', enabled: true });
    await check;
    expect(service.settings()?.mediaRoot).toBe('/new-media');
    expect(service.settings()?.enabled).toBeFalse();
    expect(service.capabilityChecking()).toBeFalse();
  });

  it('sends the three lists and keeps the revision with the server', async () => {
    const load = service.loadSettings();
    http.expectOne(base).flush(settings());
    await load;
    const current = service.settings()!;
    const pending = service.saveSettings({ mediaRoot: current.mediaRoot! });
    expect(service.saving()).toBeTrue();
    const request = http.expectOne(base);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      mediaRoot: '/media',
      enabled: true,
    });
    request.flush(settings(1));
    await pending;
    expect(service.settings()?.revision).toBe(1);
    expect(service.saving()).toBeFalse();
  });

  it('reloads the menu count immediately after enabling without opening the overview', async () => {
    const load = service.loadSettings();
    http.expectOne(base).flush({ ...settings(), enabled: false });
    await load;
    const disabledPoll = service.refreshCount();
    http.expectOne(`${base}/suggestions`).flush([]);
    await disabledPoll;
    expect(service.pendingCount()).toBe(0);

    const save = service.saveSettings({ mediaRoot: '/media', enabled: true });
    http.expectOne(base).flush({ ...settings(), enabled: true });
    await save;
    expect(service.saving()).toBeFalse();
    const refresh = http.expectOne(`${base}/suggestions`);
    expect(refresh.request.method).toBe('GET');
    refresh.flush([suggestion()]);
    await settle();
    expect(service.pendingCount()).toBe(1);
    http.expectNone(`${base}/links`);
    http.expectNone(`${base}/rejections`);
  });

  it('ignores a late disabled poll after the fresh enabled count has arrived', async () => {
    const load = service.loadSettings();
    http.expectOne(base).flush({ ...settings(), enabled: false });
    await load;
    const oldPoll = service.refreshCount();
    const oldRequest = http.expectOne(`${base}/suggestions`);

    const save = service.saveSettings({ mediaRoot: '/media', enabled: true });
    http.expectOne(base).flush({ ...settings(), enabled: true });
    await save;
    http.expectOne(`${base}/suggestions`).flush([suggestion()]);
    await settle();
    expect(service.pendingCount()).toBe(1);

    oldRequest.flush([]);
    await oldPoll;
    expect(service.pendingCount()).toBe(1);
  });

  it('hides the count when disabled and ignores in-flight responses without starting another read', async () => {
    const oldPoll = service.refreshCount();
    const oldRequest = http.expectOne(`${base}/suggestions`);
    const save = service.saveSettings({ mediaRoot: '/media', enabled: false });
    http.expectOne(base).flush({ ...settings(), enabled: false });
    await save;
    expect(service.pendingCount()).toBe(0);
    http.expectNone(`${base}/suggestions`);
    oldRequest.flush([suggestion()]);
    await oldPoll;
    expect(service.suggestions()).toEqual([]);
    expect(service.pendingCount()).toBe(0);
  });

  it('keeps enabling successful if the subsequent count reload fails', async () => {
    const load = service.loadSettings();
    http.expectOne(base).flush({ ...settings(), enabled: false });
    await load;
    const save = service.saveSettings({ mediaRoot: '/media', enabled: true });
    http.expectOne(base).flush({ ...settings(), enabled: true });
    await save;
    http.expectOne(`${base}/suggestions`).flush({ title: 'Count unavailable' }, { status: 503, statusText: 'Unavailable' });
    await settle();
    expect(service.settings()?.enabled).toBeTrue();
    expect(service.settingsError()).toBeNull();
    expect(service.saving()).toBeFalse();
    expect(service.overviewError()).toBe('Count unavailable');
  });

  it('keeps the refused field of a validation answer where the form can show it', async () => {
    const pending: Promise<string | null> = service.saveSettings({ mediaRoot: '' }).then(
      () => null,
      (error: unknown) => (error as Error).message,
    );
    http
      .expectOne(base)
      .flush(
        { title: 'One or more errors occurred.', errors: { 'symlinks.mediaRoot': ['The media root is required.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    expect(await pending).toBe('The media root is required.');
    expect(service.fieldErrors()['symlinks.mediaRoot']).toBe('The media root is required.');
    expect(service.settingsError()).toBe('The media root is required.');
    expect(service.saving()).toBeFalse();
  });

  it('replaces the stored probe answer for the tested area only', async () => {
    const load = service.loadSettings();
    http.expectOne(base).flush({ ...settings(), capabilityChecks: [] } satisfies SymlinkSettings);
    await load;
    const pending = service.testCapability(areaId);
    const request = http.expectOne(`${base}/storage-areas/${areaId}/capability-test`);
    expect(request.request.method).toBe('POST');
    request.flush({
      storageAreaId: areaId,
      status: 'Available',
      detail: 'Symlink created and read back.',
      checkedAt: '2026-11-05T09:00:00Z',
      configRevision: 0,
      stale: false,
    });
    const check = await pending;
    expect(check.status).toBe('Available');
    expect(service.settings()?.capabilityChecks).toEqual([check]);
    expect(service.capabilityStates()[areaId].busy).toBeFalse();
    expect(service.capabilityStates()[areaId].success).toBeTrue();
  });

  it('reads suggestions, links and declines together', async () => {
    const pending = service.loadOverview();
    http.expectOne(`${base}/links`).flush([link()]);
    await settle();
    http.expectOne(`${base}/suggestions`).flush([suggestion()]);
    http.expectOne(`${base}/rejections`).flush([]);
    await pending;
    expect(service.suggestions()).toHaveSize(1);
    expect(service.links()).toHaveSize(1);
    expect(service.overviewLoading()).toBeFalse();
  });

  it('keeps a refresh summary and only reads again when the pass changed a row', async () => {
    const pending = service.refreshSuggestions();
    http
      .expectOne(`${base}/suggestions/refresh`)
      .flush({ instancesConsidered: 2, created: 0, updated: 0, markedStale: 0, skippedRejected: 1, didWork: false });
    const run = await pending;
    expect(run.didWork).toBeFalse();
    expect(service.lastRun()?.skippedRejected).toBe(1);
    http.expectNone(`${base}/suggestions`);

    const next = service.refreshSuggestions();
    http
      .expectOne(`${base}/suggestions/refresh`)
      .flush({ instancesConsidered: 2, created: 1, updated: 0, markedStale: 0, skippedRejected: 0, didWork: true });
    await settle();
    http.expectOne(`${base}/suggestions`).flush([suggestion()]);
    expect((await next).created).toBe(1);
    expect(service.suggestions()).toHaveSize(1);
  });

  it('asks the preview route what confirming would do', async () => {
    const pending = service.previewLink({
      radarrInstanceId: 'radarr-1',
      radarrMovieId: 70,
      sonarrInstanceId: 'sonarr-1',
      sonarrSeriesId: 12,
      sonarrEpisodeId: 900,
      radarrFileId: null,
    });
    const request = http.expectOne(`${base}/preview`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      radarrInstanceId: 'radarr-1',
      radarrMovieId: 70,
      sonarrInstanceId: 'sonarr-1',
      sonarrSeriesId: 12,
      sonarrEpisodeId: 900,
      radarrFileId: null,
    });
    request.flush({
      runnable: false,
      draft: link('draft'),
      blockers: [{ code: 'no-file', detail: 'No file.', detectedAt: '2026-11-05T09:00:00Z' }],
      warnings: [],
    });
    const plan = await pending;
    expect(plan.runnable).toBeFalse();
    expect(plan.blockers[0].code).toBe('no-file');
  });

  it('adds the accepted link and refreshes the offered pairs after a confirm', async () => {
    const load = service.loadOverview();
    http.expectOne(`${base}/links`).flush([]);
    await settle();
    http.expectOne(`${base}/suggestions`).flush([suggestion()]);
    http.expectOne(`${base}/rejections`).flush([]);
    await load;

    const existingLink = { path: '/media/series/existing.mkv', linkText: '../movies/movie.mkv', sonarrFileId: 88 };
    const pending = service.confirmSuggestion('sug-1', 0, 'source-v1', existingLink);
    const confirm = http.expectOne(`${base}/suggestions/sug-1/confirm`);
    expect(confirm.request.body).toEqual({ configRevision: 0, sourceVersion: 'source-v1', existingLink });
    confirm.flush(link());
    await settle();
    http.expectOne(`${base}/suggestions`).flush([{ ...suggestion(), status: 'Creating', linkId: 'link-1' } satisfies SymlinkSuggestion]);
    const created = await pending;
    expect(created.id).toBe('link-1');
    expect(service.links()).toHaveSize(1);
    expect(service.suggestions()[0].status).toBe('Creating');
  });

  it('refreshes the path configuration when the confirmed revision was refused', async () => {
    const load = service.loadOverview();
    http.expectOne(`${base}/links`).flush([]);
    await settle();
    http.expectOne(`${base}/suggestions`).flush([suggestion()]);
    http.expectOne(`${base}/rejections`).flush([]);
    await load;

    const pending: Promise<string | null> = service.confirmSuggestion('sug-1', 0).then(
      () => null,
      (error: unknown) => (error as Error).message,
    );
    http
      .expectOne(`${base}/suggestions/sug-1/confirm`)
      .flush({ title: 'The path configuration changed.' }, { status: 409, statusText: 'Conflict' });
    await settle();
    http.expectOne(base).flush(settings(2));
    await settle();
    http.expectOne(`${base}/suggestions`).flush([suggestion()]);
    expect(await pending).toBe('The path configuration changed.');
    expect(service.settings()?.revision).toBe(2);
    expect(service.suggestionStates()['sug-1'].error).toBe('The path configuration changed.');
  });

  it('shows the declined pair as decided and reads the archive again', async () => {
    const load = service.loadOverview();
    http.expectOne(`${base}/links`).flush([]);
    await settle();
    http.expectOne(`${base}/suggestions`).flush([suggestion()]);
    http.expectOne(`${base}/rejections`).flush([]);
    await load;

    const pending = service.rejectSuggestion('sug-1', 'Documentary, not the same film.');
    http
      .expectOne(`${base}/suggestions/sug-1/reject`)
      .flush({ ...suggestion(), status: 'Rejected', detail: 'Documentary, not the same film.' });
    await settle();
    http.expectOne(`${base}/rejections`).flush([
      {
        radarrInstanceId: 'radarr-1',
        radarrMovieId: 70,
        radarrMovieTitle: 'Un Atto Di Violenza',
        sonarrInstanceId: 'sonarr-1',
        sonarrSeriesId: 12,
        sonarrSeriesTitle: 'Specials',
        sonarrEpisodeId: 900,
        sonarrEpisodeTitle: 'Un Atto Di Violenza',
        detail: 'Documentary, not the same film.',
        createdAt: '2026-11-05T09:00:00Z',
      },
    ]);
    const rejected = await pending;
    expect(rejected.status).toBe('Rejected');
    expect(service.suggestions()[0].status).toBe('Rejected');
    expect(service.rejections()).toHaveSize(1);
  });

  it('drops a decline from the archive and lets the pair be offered again', async () => {
    const load = service.loadOverview();
    http.expectOne(`${base}/links`).flush([]);
    await settle();
    http.expectOne(`${base}/suggestions`).flush([]);
    http.expectOne(`${base}/rejections`).flush([
      {
        radarrInstanceId: 'radarr-1',
        radarrMovieId: 70,
        radarrMovieTitle: null,
        sonarrInstanceId: 'sonarr-1',
        sonarrSeriesId: 12,
        sonarrSeriesTitle: null,
        sonarrEpisodeId: 900,
        sonarrEpisodeTitle: null,
        detail: null,
        createdAt: '2026-11-05T09:00:00Z',
      },
    ]);
    await load;

    const pending = service.reactivateRejection({
      radarrInstanceId: 'radarr-1',
      radarrMovieId: 70,
      sonarrInstanceId: 'sonarr-1',
      sonarrSeriesId: 12,
      sonarrEpisodeId: 900,
    });
    http.expectOne(`${base}/rejections/reactivate`).flush(null);
    await settle();
    http.expectOne(`${base}/suggestions`).flush([suggestion()]);
    await pending;
    expect(service.rejections()).toHaveSize(0);
    expect(service.suggestions()).toHaveSize(1);
  });

  it('presents the remembered revision when following upgrades is switched', async () => {
    const load = service.loadOverview();
    http.expectOne(`${base}/links`).flush([link('link-1', 4)]);
    await settle();
    http.expectOne(`${base}/suggestions`).flush([]);
    http.expectOne(`${base}/rejections`).flush([]);
    await load;

    const pending = service.setFollowUp('link-1', false, 4);
    const request = http.expectOne(`${base}/links/link-1/follow-up`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ autoFollowUp: false, revision: 4 });
    request.flush({ ...link('link-1', 5), autoFollowUp: false });
    const updated = await pending;
    expect(updated.revision).toBe(5);
    expect(service.links()[0].autoFollowUp).toBeFalse();
    expect(service.linkStates()['link-1'].error).toBeNull();
  });

  it('reads the register again when another writer was faster', async () => {
    const pending: Promise<string | null> = service.setFollowUp('link-1', false, 2).then(
      () => null,
      (error: unknown) => (error as Error).message,
    );
    http.expectOne(`${base}/links/link-1/follow-up`).flush({ title: 'The entry changed.' }, { status: 409, statusText: 'Conflict' });
    await settle();
    http.expectOne(`${base}/links`).flush([link('link-1', 9)]);
    expect(await pending).toBe('The entry changed.');
    expect(service.links()[0].revision).toBe(9);
    expect(service.linkStates()['link-1'].error).toBe('The entry changed.');
  });

  it('keeps deletion locked until the refreshed suggestions and removed row can be shown together', async () => {
    const load = service.loadOverview();
    http.expectOne(`${base}/links`).flush([link('link-1', 3)]);
    await settle();
    http.expectOne(`${base}/suggestions`).flush([]);
    http.expectOne(`${base}/rejections`).flush([]);
    await load;

    const pending = service.deleteLink('link-1', 3);
    expect(service.linkStates()['link-1'].deleting).toBeTrue();
    expect(service.linkStates()['link-1'].busy).toBeTrue();
    await service.deleteLink('link-1', 3);
    const request = http.expectOne(`${base}/links/link-1?revision=3`);
    expect(request.request.method).toBe('DELETE');
    request.flush(null);
    await settle();
    http.expectOne(`${base}/links`).flush([{ ...link('link-1', 4), operationStatus: 'CleanupPending' }]);
    await settle();
    http.expectOne(`${base}/suggestions`).flush([]);
    http.expectOne(`${base}/rejections`).flush([]);
    await pending;
    expect(service.links()[0].operationStatus).toBe('CleanupPending');
    expect(service.linkStates()['link-1'].busy).toBeFalse();
    expect(service.linkStates()['link-1'].deleting).toBeTrue();

    const progress = service.refreshProgress();
    expect(service.refreshProgress()).toBe(progress);
    http.expectOne(`${base}/links`).flush([]);
    await settle();
    expect(service.links()).toHaveSize(1);
    expect(service.suggestions()).toEqual([]);
    http.expectOne(`${base}/suggestions`).flush([suggestion()]);
    expect(service.links()).toHaveSize(1);
    http.expectOne(`${base}/rejections`).flush([]);
    await progress;
    expect(service.links()).toEqual([]);
    expect(service.suggestions()).toEqual([suggestion()]);
    expect(service.rejections()).toEqual([]);
    expect(service.linkStates()['link-1'].deleting).toBeFalse();
  });

  it('retries the coherent overview when a menu count read supersedes its suggestions request', async () => {
    const overview = service.loadOverview();
    http.expectOne(`${base}/links`).flush([link('link-1')]);
    await settle();
    const oldSuggestions = http.expectOne(`${base}/suggestions`);
    const oldRejections = http.expectOne(`${base}/rejections`);
    const count = service.refreshCount();
    http.expectOne(`${base}/suggestions`).flush([suggestion()]);
    await count;
    oldSuggestions.flush([]);
    oldRejections.flush([]);
    await settle();
    http.expectOne(`${base}/links`).flush([link('link-1')]);
    await settle();
    http.expectOne(`${base}/suggestions`).flush([suggestion()]);
    http.expectOne(`${base}/rejections`).flush([]);
    await overview;
    expect(service.links()).toHaveSize(1);
    expect(service.suggestions()).toEqual([suggestion()]);
  });

  async function respondOverview(links: SymlinkLink[], suggestions: SymlinkSuggestion[] = []): Promise<void> {
    http.expectOne(`${base}/links`).flush(links);
    await settle();
    http.expectOne(`${base}/suggestions`).flush(suggestions);
    http.expectOne(`${base}/rejections`).flush([]);
  }

  it('deletes a freshly recreated link despite setup revision changes and an older progress response', async () => {
    const load = service.loadOverview();
    await respondOverview([link('old-link', 3)]);
    await load;
    const firstDelete = service.deleteLink('old-link', 3);
    http.expectOne(`${base}/links/old-link?revision=3`).flush(null);
    await settle();
    await respondOverview([], [suggestion('again')]);
    await firstDelete;

    const oldProgress = service.refreshProgress();
    const staleLinks = http.expectOne(`${base}/links`);
    const recreation = service.confirmSuggestion('again', 0);
    http.expectOne(`${base}/suggestions/again/confirm`).flush(link('new-link', 1));
    await settle();
    http.expectOne(`${base}/suggestions`).flush([{ ...suggestion('again'), status: 'Creating', linkId: 'new-link' }]);
    await recreation;
    staleLinks.flush([]);
    await oldProgress;
    expect(service.links().map((value) => value.id)).toEqual(['new-link']);

    const secondDelete = service.deleteLink('new-link', 1);
    http
      .expectOne(`${base}/links/new-link?revision=1`)
      .flush({ detail: 'Reload the register and apply the change again.' }, { status: 409, statusText: 'Conflict' });
    await settle();
    http.expectOne(`${base}/links`).flush([{ ...link('new-link', 7), linkPath: '/media/series/specials/final-name.mkv' }]);
    await settle();
    expect(service.linkStates()['new-link'].busy).toBeTrue();
    expect(service.linkStates()['new-link'].deleting).toBeTrue();
    expect(service.linkStates()['new-link'].error).toBeNull();
    http.expectOne(`${base}/links/new-link?revision=7`).flush(null);
    await settle();
    await respondOverview([], [suggestion('again-2')]);
    await secondDelete;
    expect(service.links()).toEqual([]);
    expect(service.suggestions()).toEqual([suggestion('again-2')]);
    expect(service.linkStates()['new-link'].error).toBeNull();
    expect(service.linkStates()['new-link'].deleting).toBeFalse();
    http.expectNone(`${base}/suggestions/refresh`);
  });

  for (const [name, changes] of [
    ['pairing', { sonarrEpisodeId: 901 }],
    ['source', { sourcePath: '/media/movies/other.mkv' }],
    ['source version', { sourceVersion: 'different-source-version' }],
    ['real conflict', { operationStatus: 'Conflict' }],
  ] as [string, Partial<SymlinkLink>][]) {
    it(`does not automatically delete a changed ${name} after a revision conflict`, async () => {
      const load = service.loadOverview();
      await respondOverview([link('link-1', 3)]);
      await load;
      const pending = service.deleteLink('link-1', 3).catch((error: Error) => error.message);
      http.expectOne(`${base}/links/link-1?revision=3`).flush({ detail: 'The entry changed.' }, { status: 409, statusText: 'Conflict' });
      await settle();
      const changed = { ...link('link-1', 7), ...changes };
      http.expectOne(`${base}/links`).flush([changed]);
      await settle();
      await respondOverview([changed]);
      expect(await pending).toBe('The entry changed.');
      http.expectNone(`${base}/links/link-1?revision=7`);
      expect(service.links()[0]).toEqual(changed);
      expect(service.linkStates()['link-1'].deleting).toBeFalse();
    });
  }

  it('accepts a deletion already queued while refreshing a conflicting revision', async () => {
    const load = service.loadOverview();
    await respondOverview([link('link-1', 3)]);
    await load;
    const pending = service.deleteLink('link-1', 3);
    http.expectOne(`${base}/links/link-1?revision=3`).flush({}, { status: 409, statusText: 'Conflict' });
    await settle();
    const queued = { ...link('link-1', 4), operationStatus: 'CleanupPending' as const };
    http.expectOne(`${base}/links`).flush([queued]);
    await settle();
    await respondOverview([queued]);
    await pending;
    http.expectNone(`${base}/links/link-1?revision=4`);
    expect(service.linkStates()['link-1'].error).toBeNull();
    expect(service.linkStates()['link-1'].deleting).toBeTrue();
  });

  it('bounds repeated revision conflicts and leaves the refreshed row available for another action', async () => {
    const load = service.loadOverview();
    await respondOverview([link('link-1', 1)]);
    await load;
    const pending = service.deleteLink('link-1', 1).catch((error: Error) => error.message);
    for (let revision = 1; revision <= 3; revision++) {
      http
        .expectOne(`${base}/links/link-1?revision=${revision}`)
        .flush({ detail: 'Still changing' }, { status: 409, statusText: 'Conflict' });
      await settle();
      if (revision < 3) {
        http.expectOne(`${base}/links`).flush([link('link-1', revision + 1)]);
        await settle();
      }
    }
    await respondOverview([link('link-1', 4)]);
    expect(await pending).toBe('Still changing');
    expect(service.links()[0].revision).toBe(4);
    expect(service.linkStates()['link-1'].deleting).toBeFalse();
    http.expectNone(`${base}/links/link-1?revision=4`);
  });

  it('retains a deleting row when loading its replacement suggestion fails and retries together', async () => {
    const load = service.loadOverview();
    http.expectOne(`${base}/links`).flush([{ ...link('link-1'), operationStatus: 'CleanupPending' }]);
    await settle();
    http.expectOne(`${base}/suggestions`).flush([]);
    http.expectOne(`${base}/rejections`).flush([]);
    await load;

    const failed = service.refreshProgress();
    http.expectOne(`${base}/links`).flush([]);
    await settle();
    http.expectOne(`${base}/suggestions`).flush({ title: 'Try again' }, { status: 503, statusText: 'Unavailable' });
    http.expectOne(`${base}/rejections`).flush([]);
    await failed;
    expect(service.links()[0].operationStatus).toBe('CleanupPending');
    expect(service.suggestions()).toEqual([]);

    const retry = service.refreshProgress();
    http.expectOne(`${base}/links`).flush([]);
    await settle();
    http.expectOne(`${base}/suggestions`).flush([suggestion()]);
    http.expectOne(`${base}/rejections`).flush([]);
    await retry;
    expect(service.links()).toEqual([]);
    expect(service.suggestions()).toHaveSize(1);
    expect(service.overviewError()).toBeNull();
  });

  it('unlocks the row when the delete request itself is rejected', async () => {
    const pending = service.deleteLink('link-1', 3).catch((error: Error) => error.message);
    expect(service.linkStates()['link-1'].deleting).toBeTrue();
    http.expectOne(`${base}/links/link-1?revision=3`).flush({ title: 'No permission' }, { status: 503, statusText: 'Unavailable' });
    await settle();
    http.expectOne(`${base}/links`).flush([link('link-1', 3)]);
    await settle();
    http.expectOne(`${base}/suggestions`).flush([]);
    http.expectOne(`${base}/rejections`).flush([]);
    expect(await pending).toBe('No permission');
    expect(service.linkStates()['link-1'].deleting).toBeFalse();
    expect(service.linkStates()['link-1'].busy).toBeFalse();
  });

  it('keeps an interrupted deletion queued on the server visible after a failed delete response', async () => {
    const pending = service.deleteLink('link-1', 3).catch(() => undefined);
    http.expectOne(`${base}/links/link-1?revision=3`).flush({ title: 'Write failed' }, { status: 503, statusText: 'Unavailable' });
    await settle();
    http.expectOne(`${base}/links`).flush([{ ...link('link-1', 4), operationStatus: 'DeletePending' }]);
    await settle();
    http.expectOne(`${base}/suggestions`).flush([]);
    http.expectOne(`${base}/rejections`).flush([]);
    await pending;
    expect(service.links()[0].operationStatus).toBe('DeletePending');
    await service.deleteLink('link-1', 4);
    http.expectNone(`${base}/links/link-1?revision=4`);
  });

  it('removes a completed deletion with its suggestion and ignores an older overview response', async () => {
    const overview = service.loadOverview();
    const oldLinks = http.expectOne(`${base}/links`);
    const deletion = service.deleteLink('link-1', 3);
    http.expectOne(`${base}/links/link-1?revision=3`).flush(null);
    await settle();
    http.expectOne(`${base}/links`).flush([]);
    await settle();
    http.expectOne(`${base}/suggestions`).flush([suggestion()]);
    http.expectOne(`${base}/rejections`).flush([]);
    await deletion;
    oldLinks.flush([link('link-1', 3)]);
    await overview;
    http.expectNone(`${base}/suggestions`);
    http.expectNone(`${base}/rejections`);
    expect(service.links()).toEqual([]);
    expect(service.suggestions()).toEqual([suggestion()]);
  });

  it('answers the row a single check looked at', async () => {
    const load = service.loadOverview();
    http.expectOne(`${base}/links`).flush([link('link-1', 2)]);
    await settle();
    http.expectOne(`${base}/suggestions`).flush([]);
    http.expectOne(`${base}/rejections`).flush([]);
    await load;

    const pending = service.checkLink('link-1');
    const request = http.expectOne(`${base}/links/link-1/check`);
    expect(request.request.method).toBe('POST');
    request.flush({
      ...link('link-1', 3),
      findings: [
        { code: 'link-lost', detail: "'/media/series/specials/s01e01.mkv' is not there anymore.", detectedAt: '2026-11-06T14:00:00Z' },
      ],
      lastCheckedAt: '2026-11-06T14:00:00Z',
    });
    const checked = await pending;

    // The answer is the row the register holds now — findings and timestamp included, not the copy in hand.
    expect(checked.findings).toHaveSize(1);
    expect(service.links()[0].findings[0].code).toBe('link-lost');
    expect(service.links()[0].lastCheckedAt).toBe('2026-11-06T14:00:00Z');
    expect(service.linkStates()['link-1'].error).toBeNull();
    expect(service.linkStates()['link-1'].busy).toBeFalse();
  });

  it('reads the register again when the checked row had moved underneath', async () => {
    const pending: Promise<string | null> = service
      .checkLink('link-1')
      .then(() => null)
      .catch((error: Error) => error.message);
    http.expectOne(`${base}/links/link-1/check`).flush({ title: 'The entry changed.' }, { status: 409, statusText: 'Conflict' });
    await settle();
    http.expectOne(`${base}/links`).flush([link('link-1', 7)]);

    expect(await pending).toBe('The entry changed.');
    expect(service.links()[0].revision).toBe(7);
    expect(service.linkStates()['link-1'].error).toBe('The entry changed.');
  });

  it('reports a read failure without claiming the action failed', async () => {
    const pending = service.rejectSuggestion('sug-1', null);
    http.expectOne(`${base}/suggestions/sug-1/reject`).flush({ ...suggestion(), status: 'Rejected' });
    await settle();
    http.expectOne(`${base}/rejections`).flush({ title: 'Store busy' }, { status: 503, statusText: 'Unavailable' });
    const rejected = await pending;
    expect(rejected.status).toBe('Rejected');
    expect(service.overviewError()).toBe('Store busy');
    expect(service.suggestionStates()['sug-1'].error).toBeNull();
  });
});
