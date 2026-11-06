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

  it('sends the three lists and keeps the revision with the server', async () => {
    const load = service.loadSettings();
    http.expectOne(base).flush(settings());
    await load;
    const current = service.settings()!;
    const pending = service.saveSettings({ ...current, storageAreas: [...current.storageAreas] });
    expect(service.saving()).toBeTrue();
    const request = http.expectOne(base);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      storageAreas: current.storageAreas,
      pathMappings: current.pathMappings,
      readerViews: current.readerViews,
    });
    request.flush(settings(1));
    await pending;
    expect(service.settings()?.revision).toBe(1);
    expect(service.saving()).toBeFalse();
  });

  it('keeps the refused field of a validation answer where the form can show it', async () => {
    const pending: Promise<string | null> = service
      .saveSettings({ storageAreas: [], pathMappings: [], readerViews: [], revision: 0, capabilityChecks: [] })
      .then(() => null, (error: unknown) => (error as Error).message);
    http
      .expectOne(base)
      .flush(
        { title: 'One or more errors occurred.', errors: { 'symlinks.storageAreas[0].moviesRoot': ['The movie root is required.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    expect(await pending).toBe('The movie root is required.');
    expect(service.fieldErrors()['symlinks.storageAreas[0].moviesRoot']).toBe('The movie root is required.');
    expect(service.settingsError()).toBe('The movie root is required.');
    expect(service.saving()).toBeFalse();
  });

  it('replaces the stored probe answer for the tested area only', async () => {
    const load = service.loadSettings();
    http.expectOne(base).flush({ ...settings(), capabilityChecks: [] } satisfies SymlinkSettings);
    await load;
    const pending = service.testCapability(areaId);
    const request = http.expectOne(`${base}/storage-areas/${areaId}/capability-test`);
    expect(request.request.method).toBe('POST');
    request.flush({ storageAreaId: areaId, status: 'Available', detail: 'Symlink created and read back.', checkedAt: '2026-11-05T09:00:00Z', configRevision: 0, stale: false });
    const check = await pending;
    expect(check.status).toBe('Available');
    expect(service.settings()?.capabilityChecks).toEqual([check]);
    expect(service.capabilityStates()[areaId].busy).toBeFalse();
    expect(service.capabilityStates()[areaId].success).toBeTrue();
  });

  it('reads suggestions, links and declines together', async () => {
    const pending = service.loadOverview();
    http.expectOne(`${base}/suggestions`).flush([suggestion()]);
    http.expectOne(`${base}/links`).flush([link()]);
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
    request.flush({ runnable: false, draft: link('draft'), blockers: [{ code: 'no-file', detail: 'No file.', detectedAt: '2026-11-05T09:00:00Z' }], warnings: [] });
    const plan = await pending;
    expect(plan.runnable).toBeFalse();
    expect(plan.blockers[0].code).toBe('no-file');
  });

  it('adds the accepted link and refreshes the offered pairs after a confirm', async () => {
    const load = service.loadOverview();
    http.expectOne(`${base}/suggestions`).flush([suggestion()]);
    http.expectOne(`${base}/links`).flush([]);
    http.expectOne(`${base}/rejections`).flush([]);
    await load;

    const pending = service.confirmSuggestion('sug-1', 0);
    http.expectOne(`${base}/suggestions/sug-1/confirm`).flush(link());
    await settle();
    http.expectOne(`${base}/suggestions`).flush([{ ...suggestion(), status: 'Creating', linkId: 'link-1' } satisfies SymlinkSuggestion]);
    const created = await pending;
    expect(created.id).toBe('link-1');
    expect(service.links()).toHaveSize(1);
    expect(service.suggestions()[0].status).toBe('Creating');
  });

  it('refreshes the path configuration when the confirmed revision was refused', async () => {
    const load = service.loadOverview();
    http.expectOne(`${base}/suggestions`).flush([suggestion()]);
    http.expectOne(`${base}/links`).flush([]);
    http.expectOne(`${base}/rejections`).flush([]);
    await load;

    const pending: Promise<string | null> = service
      .confirmSuggestion('sug-1', 0)
      .then(() => null, (error: unknown) => (error as Error).message);
    http.expectOne(`${base}/suggestions/sug-1/confirm`).flush({ title: 'The path configuration changed.' }, { status: 409, statusText: 'Conflict' });
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
    http.expectOne(`${base}/suggestions`).flush([suggestion()]);
    http.expectOne(`${base}/links`).flush([]);
    http.expectOne(`${base}/rejections`).flush([]);
    await load;

    const pending = service.rejectSuggestion('sug-1', 'Documentary, not the same film.');
    http.expectOne(`${base}/suggestions/sug-1/reject`).flush({ ...suggestion(), status: 'Rejected', detail: 'Documentary, not the same film.' });
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
    http.expectOne(`${base}/suggestions`).flush([]);
    http.expectOne(`${base}/links`).flush([]);
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
    http.expectOne(`${base}/suggestions`).flush([]);
    http.expectOne(`${base}/links`).flush([link('link-1', 4)]);
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
    const pending: Promise<string | null> = service
      .setFollowUp('link-1', false, 2)
      .then(() => null, (error: unknown) => (error as Error).message);
    http.expectOne(`${base}/links/link-1/follow-up`).flush({ title: 'The entry changed.' }, { status: 409, statusText: 'Conflict' });
    await settle();
    http.expectOne(`${base}/links`).flush([link('link-1', 9)]);
    expect(await pending).toBe('The entry changed.');
    expect(service.links()[0].revision).toBe(9);
    expect(service.linkStates()['link-1'].error).toBe('The entry changed.');
  });

  it('presents the revision on the delete and forgets the row afterwards', async () => {
    const load = service.loadOverview();
    http.expectOne(`${base}/suggestions`).flush([]);
    http.expectOne(`${base}/links`).flush([link('link-1', 3)]);
    http.expectOne(`${base}/rejections`).flush([]);
    await load;

    const pending = service.deleteLink('link-1', 3);
    const request = http.expectOne(`${base}/links/link-1?revision=3`);
    expect(request.request.method).toBe('DELETE');
    request.flush(null);
    await pending;
    expect(service.links()).toHaveSize(0);
  });

  it('answers the row a single check looked at', async () => {
    const load = service.loadOverview();
    http.expectOne(`${base}/suggestions`).flush([]);
    http.expectOne(`${base}/links`).flush([link('link-1', 2)]);
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
