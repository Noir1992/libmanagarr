import { signal, WritableSignal } from '@angular/core';
import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { SelectFilterOption } from '../../interfaces/filter-option';
import { PageFilterWidget } from '../../widgets/page-filter/page-filter.widget';
import { InfoIconWidget } from '../../widgets/info-icon/info-icon.widget';
import { SymlinkSettings } from '../../interfaces/symlinks/symlink-settings';
import { SymlinkRejection } from '../../interfaces/symlinks/symlink-decisions';
import { SymlinkLink } from '../../interfaces/symlinks/symlink-link';
import { ExistingEpisodeFileCheck, SymlinkMatchingRun, SymlinkSuggestion } from '../../interfaces/symlinks/symlink-suggestion';
import { AppI18nService } from '../../services/app-i18n.service';
import { SymlinkActionState, SymlinkService } from '../../services/symlink.service';
import { ConfirmSymlinkDialogComponent } from './confirm-symlink-dialog/confirm-symlink-dialog.component';
import { RejectSymlinkDialogComponent } from './reject-symlink-dialog/reject-symlink-dialog.component';
import { SymlinksPage } from './symlinks.page';

type SymlinkServiceStub = Pick<
  SymlinkService,
  | 'settings'
  | 'suggestions'
  | 'links'
  | 'rejections'
  | 'suggestionStates'
  | 'linkStates'
  | 'overviewLoading'
  | 'overviewError'
  | 'refreshing'
  | 'lastRun'
  | 'settingsError'
  | 'loadOverview'
  | 'loadSettings'
  | 'refreshSuggestions'
  | 'refreshProgress'
  | 'rejectSuggestion'
  | 'checkLink'
  | 'setFollowUp'
  | 'deleteLink'
  | 'reactivateRejection'
>;

function createSettings(overrides: Partial<SymlinkSettings> = {}): SymlinkSettings {
  return {
    mediaRoot: '/media',
    storageAreas: [{ id: 'area-1', name: 'Media', moviesRoot: '/data/movies', seriesRoot: '/data/series' }],
    pathMappings: [],
    readerViews: [],
    revision: 7,
    capabilityChecks: [],
    ...overrides,
  };
}

function createSuggestion(overrides: Partial<SymlinkSuggestion> = {}): SymlinkSuggestion {
  return {
    id: 'suggestion-1',
    matcherVersion: '2',
    status: 'Pending',
    radarrInstanceId: 'radarr-1',
    radarrMovieId: 41,
    radarrMovieTitle: 'The Special Movie',
    radarrFileId: 900,
    radarrFilePath: '/data/movies/the-special-movie.mkv',
    sonarrInstanceId: 'sonarr-1',
    sonarrSeriesId: 12,
    sonarrSeriesTitle: 'The Series',
    sonarrSeriesPath: '/data/series/the-series',
    sonarrEpisodeId: 555,
    sonarrEpisodeTitle: 'Behind the Scenes',
    seasonNumber: 0,
    episodeNumber: 4,
    reasons: [{ code: 'identical-title', detail: 'The special and the movie have the same title.' }],
    alternatives: [{ radarrInstanceId: 'radarr-1', radarrMovieId: 42, radarrFileId: 901, detail: 'Another cut of the same film.' }],
    detail: null,
    configRevision: 7,
    stale: false,
    linkId: null,
    revision: 1,
    createdAt: '2026-11-01T09:00:00.000Z',
    updatedAt: '2026-11-01T09:00:00.000Z',
    decidedAt: null,
    ...overrides,
  };
}

function createLink(overrides: Partial<SymlinkLink> = {}): SymlinkLink {
  return {
    id: 'link-1',
    radarrInstanceId: 'radarr-1',
    radarrMovieId: 41,
    radarrMovieTitle: 'The Special Movie',
    radarrFileId: 900,
    sonarrInstanceId: 'sonarr-1',
    sonarrSeriesId: 12,
    sonarrSeriesTitle: 'The Series',
    sonarrEpisodeId: 555,
    sonarrEpisodeTitle: 'Behind the Scenes',
    seasonNumber: 0,
    episodeNumber: 4,
    storageAreaId: 'area-1',
    linkPath: '/data/series/the-series/season-00/s00e04.mkv',
    linkText: '/data/movies/the-special-movie.mkv',
    sourcePath: '/data/movies/the-special-movie.mkv',
    operationStatus: 'Active',
    setupStep: null,
    findings: [{ code: 'MetadataMismatch', detail: 'The size differs from the expected one.', detectedAt: '2026-11-02T09:00:00.000Z' }],
    monitoringOriginal: true,
    monitoringDisabled: true,
    autoFollowUp: true,
    desiredFilename: 's00e04.mkv',
    appliedFilename: null,
    sourceVersion: '900',
    expectedMetadata: null,
    confirmedMetadata: null,
    suggestionId: 'suggestion-1',
    revision: 3,
    createdAt: '2026-11-01T10:00:00.000Z',
    updatedAt: '2026-11-02T10:00:00.000Z',
    lastCheckedAt: '2026-11-02T10:00:00.000Z',
    lastSuccessfulCheckAt: null,
    ...overrides,
  };
}

function createRejection(overrides: Partial<SymlinkRejection> = {}): SymlinkRejection {
  return {
    radarrInstanceId: 'radarr-1',
    radarrMovieId: 41,
    radarrMovieTitle: 'The Special Movie',
    sonarrInstanceId: 'sonarr-1',
    sonarrSeriesId: 12,
    sonarrSeriesTitle: 'The Series',
    sonarrEpisodeId: 555,
    sonarrEpisodeTitle: 'Behind the Scenes',
    detail: 'The film is a different cut.',
    createdAt: '2026-11-01T11:00:00.000Z',
    ...overrides,
  };
}

function createRun(overrides: Partial<SymlinkMatchingRun> = {}): SymlinkMatchingRun {
  return { instancesConsidered: 2, created: 1, updated: 0, markedStale: 0, skippedRejected: 0, didWork: true, ...overrides };
}

describe('SymlinksPage', () => {
  let fixture: ComponentFixture<SymlinksPage>;
  let element: HTMLElement;
  let stub: SymlinkServiceStub & {
    settings: WritableSignal<SymlinkSettings | null>;
    suggestions: WritableSignal<SymlinkSuggestion[]>;
    links: WritableSignal<SymlinkLink[]>;
    rejections: WritableSignal<SymlinkRejection[]>;
    suggestionStates: WritableSignal<Record<string, SymlinkActionState>>;
    linkStates: WritableSignal<Record<string, SymlinkActionState>>;
    overviewError: WritableSignal<string | null>;
    lastRun: WritableSignal<SymlinkMatchingRun | null>;
  };
  let dialog: jasmine.SpyObj<MatDialog>;

  /** The three cards in the order the page shows them: offers, managed links, rejections. */
  function cards(): HTMLElement[] {
    return Array.from(element.querySelectorAll<HTMLElement>('.page-content > .card'));
  }

  function actionButtons(cardIndex: number): HTMLButtonElement[] {
    return Array.from(cardIndex < cards().length ? cards()[cardIndex].querySelectorAll<HTMLButtonElement>('.item-actions button') : []);
  }

  function rowText(cardIndex: number): string {
    return (cards()[cardIndex].querySelector('.item')?.textContent ?? '').replace(/\s+/g, ' ');
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  beforeEach(async () => {
    globalThis.localStorage.removeItem('filters:symlinks');
    stub = {
      settings: signal<SymlinkSettings | null>(createSettings()),
      suggestions: signal<SymlinkSuggestion[]>([]),
      links: signal<SymlinkLink[]>([]),
      rejections: signal<SymlinkRejection[]>([]),
      suggestionStates: signal<Record<string, SymlinkActionState>>({}),
      linkStates: signal<Record<string, SymlinkActionState>>({}),
      overviewLoading: signal(false),
      overviewError: signal<string | null>(null),
      refreshing: signal(false),
      lastRun: signal<SymlinkMatchingRun | null>(null),
      settingsError: signal<string | null>(null),
      loadOverview: jasmine.createSpy('loadOverview').and.resolveTo(),
      loadSettings: jasmine.createSpy('loadSettings').and.resolveTo(),
      refreshSuggestions: jasmine.createSpy('refreshSuggestions').and.resolveTo(createRun()),
      refreshProgress: jasmine.createSpy('refreshProgress').and.resolveTo(),
      rejectSuggestion: jasmine.createSpy('rejectSuggestion').and.resolveTo(createSuggestion({ status: 'Rejected' })),
      checkLink: jasmine.createSpy('checkLink').and.resolveTo(createLink()),
      setFollowUp: jasmine.createSpy('setFollowUp').and.callFake(async (id: string, enabled: boolean, revision: number) => {
        const updated = createLink({ id, autoFollowUp: enabled, revision: revision + 1 });
        stub.links.update((links) => links.map((link) => (link.id === id ? updated : link)));
        return updated;
      }),
      deleteLink: jasmine.createSpy('deleteLink').and.resolveTo(),
      reactivateRejection: jasmine.createSpy('reactivateRejection').and.resolveTo(),
    };
    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);

    await TestBed.configureTestingModule({
      imports: [SymlinksPage],
      providers: [provideRouter([]), { provide: SymlinkService, useValue: stub }, { provide: MatDialog, useValue: dialog }],
    }).compileComponents();

    TestBed.inject(AppI18nService).setLanguage('en');
    fixture = TestBed.createComponent(SymlinksPage);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle();
  });

  afterEach(() => {
    globalThis.localStorage.removeItem('filters:symlinks');
  });

  it('filters offers and links together through the shared filter widget and title search', async () => {
    stub.suggestions.set([
      createSuggestion(),
      createSuggestion({ id: 'other-offer', radarrInstanceId: 'radarr-2', radarrMovieTitle: 'Another Movie' }),
    ]);
    stub.links.set([createLink(), createLink({ id: 'other-link', radarrInstanceId: 'radarr-2', radarrMovieTitle: 'Another Movie' })]);
    fixture.detectChanges();
    const widget = fixture.debugElement.query(By.directive(PageFilterWidget)).componentInstance as PageFilterWidget;
    const filter = (id: string): SelectFilterOption => widget.filters().find((option) => option.id === id) as SelectFilterOption;

    filter('instance').onChanged('radarr-2');
    fixture.detectChanges();
    expect(rowText(0)).toContain('Another Movie');
    expect(cards()[0].textContent).not.toContain('The Special Movie');
    expect(rowText(1)).toContain('Another Movie');
    expect(cards()[1].textContent).not.toContain('The Special Movie');

    filter('status').onChanged('Active');
    fixture.detectChanges();
    expect(rowText(0)).toBe('');
    expect(rowText(1)).toContain('Another Movie');

    filter('status').onChanged('');
    filter('instance').onChanged('');
    const search = element.querySelector('.symlink-search input') as HTMLInputElement;
    search.value = 'SPECIAL MOVIE';
    search.dispatchEvent(new Event('input'));
    await settle();
    expect(rowText(0)).toContain('The Special Movie');
    expect(rowText(1)).toContain('The Special Movie');
    expect(cards()[0].textContent).not.toContain('Another Movie');
    expect(cards()[1].textContent).not.toContain('Another Movie');
  });

  it('restores the shared filters when the page is reopened before rows arrive', async () => {
    fixture.destroy();
    globalThis.localStorage.setItem('filters:symlinks', JSON.stringify({ status: 'Active', instance: 'radarr-2' }));
    fixture = TestBed.createComponent(SymlinksPage);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle();

    stub.suggestions.set([createSuggestion({ radarrInstanceId: 'radarr-2' })]);
    stub.links.set([createLink(), createLink({ id: 'other-link', radarrInstanceId: 'radarr-2', radarrMovieTitle: 'Another Movie' })]);
    fixture.detectChanges();

    expect(rowText(0)).toBe('');
    expect(rowText(1)).toContain('Another Movie');
    expect(cards()[1].textContent).not.toContain('The Special Movie');

    expect(element.querySelectorAll('.filter-bubble-remove').length).toBe(2);
    for (let remaining = 2; remaining > 0; remaining--) {
      (element.querySelector('.filter-bubble-remove') as HTMLButtonElement).click();
      await settle();
    }
    expect(element.querySelector('.filter-bubble-remove')).toBeNull();
    expect(rowText(0)).toContain('The Special Movie');
    expect(cards()[1].textContent).toContain('The Special Movie');
    expect(cards()[1].textContent).toContain('Another Movie');
    expect(JSON.parse(globalThis.localStorage.getItem('filters:symlinks') ?? '{}')).toEqual(
      jasmine.objectContaining({ status: '', instance: '' }),
    );
  });

  it('reads the register and the path configuration together', () => {
    // The paths are what the preview is drawn from, so the offers are unusable without them.
    expect(stub.loadOverview).toHaveBeenCalledTimes(1);
    expect(stub.loadSettings).toHaveBeenCalledTimes(1);
  });

  it('names the pair and both titles without a redundant open badge', () => {
    stub.suggestions.set([createSuggestion()]);
    fixture.detectChanges();

    const text: string = rowText(0);
    expect(text).toContain('The Special Movie');
    expect(cards()[0].querySelector('.card-title')?.textContent).toContain('Suggestions');
    expect(text).toContain('Special 4');
    expect(text).not.toContain('Behind the Scenes');
    expect(text).toContain('The special and the movie have the same title.');
    expect(text).toContain('Another cut of the same film.');
    expect(cards()[0].querySelector('.status-pill')).toBeNull();
  });

  it('keeps non-default offer states visible', () => {
    stub.suggestions.set([
      createSuggestion({ status: 'WaitingForFile' }),
      createSuggestion({ id: 'stale', status: 'Stale' }),
      createSuggestion({ id: 'conflict', status: 'Conflict' }),
      createSuggestion({ id: 'failed', status: 'Failed' }),
    ]);
    fixture.detectChanges();
    const states = cards()[0].querySelectorAll('.status-pill');
    expect(states.length).toBe(4);
    expect(Array.from(states, (state) => state.textContent?.trim())).toEqual([
      TestBed.inject(AppI18nService).t('symlinks.suggestion.WaitingForFile'),
      TestBed.inject(AppI18nService).t('symlinks.suggestion.Stale'),
      TestBed.inject(AppI18nService).t('symlinks.suggestion.Conflict'),
      TestBed.inject(AppI18nService).t('symlinks.suggestion.Failed'),
    ]);
  });

  it('shows the movie above the series and places both paths in info icons', () => {
    stub.suggestions.set([createSuggestion()]);
    fixture.detectChanges();
    const row = cards()[0].querySelector('.item')!;
    const movie = row.querySelector('.offer-movie')!;
    const series = row.querySelector('.offer-series')!;
    expect(movie.querySelector('span')?.textContent?.trim()).toBe('Movie: The Special Movie');
    expect(series.querySelector('span')?.textContent?.trim()).toBe('Series: The Series');
    expect(movie.compareDocumentPosition(series) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(row.querySelector('.item-path')).toBeNull();

    const movieInfo = fixture.debugElement.query(By.css('.offer-movie app-info-icon')).componentInstance as InfoIconWidget;
    const seriesInfo = fixture.debugElement.query(By.css('.offer-series app-info-icon')).componentInstance as InfoIconWidget;
    expect(movieInfo.messageText()).toBe('Path: /data/movies/the-special-movie.mkv');
    expect(seriesInfo.messageText()).toBe('Path: /data/series/the-series');
    expect(movie.querySelector('app-info-icon')).not.toBeNull();
    expect(series.querySelector('app-info-icon')).not.toBeNull();
    expect(series.textContent).toContain('Special 4');
    expect(series.textContent).not.toContain('Season 0');
    expect(series.textContent).not.toContain('Behind the Scenes');
  });

  it('keeps offers readable when a file or series path is not available yet', () => {
    stub.suggestions.set([createSuggestion({ radarrFilePath: null, sonarrSeriesPath: null })]);
    fixture.detectChanges();
    expect(rowText(0)).toContain('Movie: The Special Movie');
    expect(rowText(0)).toContain('Series: The Series');
    expect(cards()[0].querySelector('.offer-movie app-info-icon')).toBeNull();
    const seriesInfo = fixture.debugElement.query(By.css('.offer-series app-info-icon')).componentInstance as InfoIconWidget;
    expect(seriesInfo.messageText()).toContain('Path:');
    expect(seriesInfo.messageText()).toContain('Run a Sonarr sync');
    expect(cards()[0].querySelector('.offer-series app-info-icon button')).not.toBeNull();
  });

  it('keeps instance names out of the suggestions and managed-link rows', () => {
    stub.suggestions.set([createSuggestion({ radarrInstanceName: 'Source Radarr', sonarrInstanceName: 'Target Sonarr' })]);
    stub.links.set([createLink({ radarrInstanceName: 'Source Radarr', sonarrInstanceName: 'Target Sonarr' })]);
    fixture.detectChanges();
    for (const index of [0, 1]) {
      expect(rowText(index)).not.toContain('Source Radarr');
      expect(rowText(index)).not.toContain('Target Sonarr');
      expect(rowText(index)).not.toContain('→');
    }
  });

  it('summarizes matching reasons separately from the existing-file notice and keeps their details accessible', () => {
    stub.suggestions.set([
      createSuggestion({
        reasons: [
          { code: 'identical-title', detail: 'The titles match after normalization.' },
          { code: 'release-year-match', detail: 'The special aired in 2016 and the movie was released in 2016.' },
          { code: 'runtime-differs', detail: 'The special runs 40 minutes, the film runs 62 minutes.' },
          {
            code: 'episode-has-file',
            detail: 'The special already has an episode file in Sonarr, so confirming this pair needs a decision about that file.',
          },
        ],
      }),
    ]);
    fixture.detectChanges();

    const reasons = cards()[0].querySelectorAll('.offer-reasons .reason-label');
    expect(Array.from(reasons, (reason) => reason.textContent?.trim())).toEqual([
      'Matching titles',
      'Same release year',
      'Different runtimes',
    ]);
    const detail = fixture.debugElement.query(By.css('.offer-reasons app-info-icon')).componentInstance as InfoIconWidget;
    expect(detail.messageText()).toContain('40 minutes');
    expect(detail.messageText()).not.toContain('decision about that file');
    expect(cards()[0].querySelector('.offer-file-note')?.textContent).toContain('inspection result is unavailable');
    const notice = fixture.debugElement.query(By.css('.offer-file-note app-info-icon')).componentInstance as InfoIconWidget;
    expect(notice.messageText()).toContain('Reload the suggestions');
    expect(notice.messageText()).toContain('No file or link is automatically adopted or overwritten');
    expect(rowText(0)).not.toContain('needs a decision');
  });

  it('keeps unfamiliar reason details instead of silently dropping them', () => {
    stub.suggestions.set([createSuggestion({ reasons: [{ code: 'future-reason', detail: 'Additional matching evidence.' }] })]);
    fixture.detectChanges();
    expect(cards()[0].querySelector('.reason-label')?.textContent).toContain('Additional matching evidence.');
    expect(cards()[0].querySelector('.offer-file-note')).toBeNull();
  });

  it('opens the suggestion reasons as a table with one row per criterion', async () => {
    stub.suggestions.set([
      createSuggestion({
        reasons: [
          { code: 'identical-title', detail: 'Both titles match after normalization.' },
          { code: 'runtime-differs', detail: 'The special runs 40 minutes, the movie runs 62 minutes.' },
        ],
      }),
    ]);
    fixture.detectChanges();
    const info = fixture.debugElement.query(By.css('.offer-reasons app-info-icon')).componentInstance as InfoIconWidget;
    expect(info.wide()).toBeTrue();
    expect(info['measurePanelSize']().height).toBeGreaterThan(60);
    (cards()[0].querySelector('.offer-reasons app-info-icon button') as HTMLButtonElement).click();
    await settle();

    const overlay = TestBed.inject(OverlayContainer).getContainerElement();
    const table = overlay.querySelector('.reason-details-table')!;
    expect(table.querySelector('caption')?.textContent?.trim()).toBe('Why suggested');
    expect(Array.from(table.querySelectorAll('thead th'), (cell) => cell.textContent?.trim())).toEqual(['Criterion', 'Details']);
    expect(
      Array.from(table.querySelectorAll('tbody tr'), (row) =>
        Array.from(row.querySelectorAll('th, td'), (cell) => cell.textContent?.trim()),
      ),
    ).toEqual([
      ['Matching titles', 'Both titles match after normalization.'],
      ['Different runtimes', 'The special runs 40 minutes, the movie runs 62 minutes.'],
    ]);
    const panel = overlay.querySelector('.info-popover')!;
    expect(panel.classList.contains('info-popover--wide')).toBeTrue();
    expect(panel.getBoundingClientRect().width).toBeLessThanOrEqual(Math.min(560, window.innerWidth - 10));
  });

  it('shows a successful live inspection separately from the match reasons without claiming adoption', () => {
    const check: ExistingEpisodeFileCheck = {
      status: 'Matches',
      detail: 'The symlink points to /data/movies/film.mkv. It was not adopted or modified.',
      episodePath: '/data/series/show/special.mkv',
      expectedTarget: '/data/movies/film.mkv',
      actualTarget: '/data/movies/film.mkv',
      checkedAt: '2026-11-09T12:00:00Z',
    };
    stub.suggestions.set([createSuggestion({ existingFileCheck: check })]);
    fixture.detectChanges();
    const note = cards()[0].querySelector('.offer-file-note')!;
    expect(note.textContent).toContain('Existing symlink points to the proposed movie.');
    expect(note.classList.contains('is-success')).toBeTrue();
    expect(note.classList.contains('is-warning')).toBeFalse();
    expect(note.querySelector('app-info-icon')).toBeNull();
    expect(note.textContent).not.toContain('inspection result is unavailable');
  });

  it('shows specific discrepancies and read errors with their inspection details', () => {
    const cases: { status: ExistingEpisodeFileCheck['status']; text: string }[] = [
      { status: 'TargetMismatch', text: 'points to a different target' },
      { status: 'BrokenSymlink', text: 'is broken or cyclic' },
      { status: 'RegularFile', text: 'is not a symlink' },
      { status: 'MissingFile', text: 'reported by Sonarr is missing' },
      { status: 'Unavailable', text: 'could not be verified' },
    ];
    for (const { status, text } of cases) {
      stub.suggestions.set([
        createSuggestion({
          existingFileCheck: {
            status,
            detail: 'Actual: other.mkv; expected: movie.mkv.',
            episodePath: '/data/series/special.mkv',
            expectedTarget: '/data/movies/movie.mkv',
            actualTarget: '/data/movies/other.mkv',
            checkedAt: '2026-11-09T12:00:00Z',
          },
        }),
      ]);
      fixture.detectChanges();
      const note = cards()[0].querySelector('.offer-file-note')!;
      expect(note.textContent).toContain(text);
      expect(note.classList.contains('is-warning')).toBeTrue();
      const info = fixture.debugElement.query(By.css('.offer-file-note app-info-icon')).componentInstance as InfoIconWidget;
      expect(info.messageText()).toContain('Actual: other.mkv; expected: movie.mkv.');
    }
  });

  it('keeps a declined pair out of the offers, which is the archive to show it', () => {
    stub.suggestions.set([createSuggestion({ status: 'Rejected' })]);
    fixture.detectChanges();

    expect(rowText(0)).toBe('');
    expect(cards()[0].textContent).toContain('No open suggestions');
  });

  it('runs matching without displaying an additional result summary', async () => {
    (stub.refreshSuggestions as jasmine.Spy).and.callFake(async (): Promise<SymlinkMatchingRun> => {
      const run: SymlinkMatchingRun = createRun({ didWork: false, created: 0 });
      stub.lastRun.set(run);
      return run;
    });
    const refresh: HTMLButtonElement = cards()[0].querySelector('.card-header-actions button') as HTMLButtonElement;
    refresh.click();
    await settle();

    expect(stub.refreshSuggestions).toHaveBeenCalledTimes(1);
    expect(cards()[0].querySelector('.card-subtitle')).toBeNull();
  });

  it('puts the current configuration revision into the confirmation', () => {
    stub.suggestions.set([createSuggestion()]);
    fixture.detectChanges();

    actionButtons(0)[0].click();

    expect(dialog.open).toHaveBeenCalledWith(
      ConfirmSymlinkDialogComponent,
      jasmine.objectContaining({ data: { suggestion: stub.suggestions()[0], configRevision: 7 } }),
    );
  });

  it('refuses to promise a link while the configuration is still unknown', () => {
    stub.settings.set(null);
    stub.suggestions.set([createSuggestion()]);
    fixture.detectChanges();

    actionButtons(0)[0].click();

    expect(dialog.open).not.toHaveBeenCalled();
  });

  it('declines with the sentence the author typed', async () => {
    dialog.open.and.returnValue({
      afterClosed: () => of('The film is a different cut.'),
    } as unknown as MatDialogRef<RejectSymlinkDialogComponent>);
    stub.suggestions.set([createSuggestion()]);
    fixture.detectChanges();

    actionButtons(0)[1].click();
    await settle();

    expect(dialog.open).toHaveBeenCalledWith(
      RejectSymlinkDialogComponent,
      jasmine.objectContaining({ data: { suggestion: stub.suggestions()[0] } }),
    );
    expect(stub.rejectSuggestion).toHaveBeenCalledWith('suggestion-1', 'The film is a different cut.');
  });

  it('leaves a decline without a reason bare instead of storing spaces', async () => {
    dialog.open.and.returnValue({ afterClosed: () => of('   ') } as unknown as MatDialogRef<RejectSymlinkDialogComponent>);
    stub.suggestions.set([createSuggestion()]);
    fixture.detectChanges();

    actionButtons(0)[1].click();
    await settle();

    expect(stub.rejectSuggestion).toHaveBeenCalledWith('suggestion-1', null);
  });

  it('shows the managed link state without the removed series and setup row', () => {
    stub.links.set([
      createLink({
        operationStatus: 'Updating',
        setupStep: 'RescanRequested',
        desiredFilename: 'desired-only.mkv',
        appliedFilename: 'applied-only.mkv',
        episodeNumber: 6,
        findings: [
          ...createLink().findings,
          { code: 'renaming-disabled', detail: 'Sonarr has episode renaming switched off.', detectedAt: '2026-11-02T09:00:00Z' },
        ],
      }),
    ]);
    fixture.detectChanges();

    const text: string = rowText(1);
    expect(text).toContain('/data/series/the-series/season-00/s00e04.mkv');
    expect(text).toContain('The size differs from the expected one.');
    expect(text).toContain('Updating');
    expect(text).not.toContain('Step:');
    expect(text).not.toContain('The linked episode is no longer monitored in Sonarr');
    expect(text).not.toContain('renaming-disabled');
    expect(text).not.toContain('Sonarr has episode renaming switched off.');
    expect(cards()[1].querySelector('.item-series')).toBeNull();
    expect(text).toContain('Special 6');
    expect(text).not.toContain('Season 0');
    expect(text).not.toContain('Desired filename');
    expect(text).not.toContain('Applied filename');
    expect(text).not.toContain('desired-only.mkv');
    expect(text).not.toContain('applied-only.mkv');
    expect(actionButtons(1).some((button) => button.textContent?.includes('Retry setup'))).toBeFalse();
    expect(
      actionButtons(1)
        .find((button) => button.textContent?.trim() === 'Edit')
        ?.classList.contains('secondary-button'),
    ).toBeTrue();
  });

  it('looks at the row a person asked about', async () => {
    stub.links.set([createLink()]);
    fixture.detectChanges();

    actionButtons(1)[0].click();
    await settle();

    expect(stub.checkLink).toHaveBeenCalledWith('link-1');
  });

  it('retains a locked deletion and swaps it for its suggestion after polling completes', async () => {
    fixture.destroy();
    spyOnProperty(globalThis.document, 'hidden', 'get').and.returnValue(false);
    const intervals = spyOn(globalThis, 'setInterval').and.callThrough();
    (stub.refreshProgress as jasmine.Spy).and.callFake(async () => {
      stub.links.set([]);
      stub.suggestions.set([createSuggestion()]);
    });
    fixture = TestBed.createComponent(SymlinksPage);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle();
    stub.links.set([createLink({ operationStatus: 'CleanupPending' })]);
    fixture.detectChanges();
    expect(cards()[1].querySelectorAll('.item').length).toBe(1);
    expect(actionButtons(1).every((button) => button.disabled)).toBeTrue();
    expect(cards()[1].querySelector('.danger-button .feedback-icon.spinning')).not.toBeNull();

    const poll = intervals.calls.allArgs().find((args) => args[1] === 5000)![0] as () => void;
    poll();
    await settle();

    expect(stub.refreshProgress).toHaveBeenCalledTimes(1);
    expect(cards()[0].querySelectorAll('.item').length).toBe(1);
    expect(cards()[1].querySelectorAll('.item').length).toBe(0);
    expect(cards()[2].querySelectorAll('.item').length).toBe(0);
  });

  for (const phase of ['request', 'DeletePending', 'CleanupPending'] as const) {
    it(`locks every row control and shows delete feedback during ${phase}`, async () => {
      stub.links.set([createLink({ operationStatus: phase === 'request' ? 'Active' : phase })]);
      stub.suggestions.set([createSuggestion()]);
      if (phase === 'request') stub.linkStates.set({ 'link-1': { deleting: true, busy: true } });
      fixture.detectChanges();
      const row = cards()[1].querySelector('.item')!;
      expect(Array.from(row.querySelectorAll<HTMLButtonElement>('button')).every((button) => button.disabled)).toBeTrue();
      expect(row.getAttribute('aria-busy')).toBe('true');
      expect(row.querySelector('.danger-button .feedback-icon.spinning')).not.toBeNull();
      expect(cards()[0].querySelector('.item')).toBeNull();
      const confirm = spyOn(globalThis, 'confirm');
      const component = fixture.componentInstance;
      await component['checkAsync'](stub.links()[0]);
      await component['setFollowUpAsync'](stub.links()[0], false);
      await component['deleteAsync'](stub.links()[0]);
      component['edit'](stub.links()[0]);
      expect(stub.checkLink).not.toHaveBeenCalled();
      expect(stub.setFollowUp).not.toHaveBeenCalled();
      expect(stub.deleteLink).not.toHaveBeenCalled();
      expect(dialog.open).not.toHaveBeenCalled();
      expect(confirm).not.toHaveBeenCalled();
      // Completing the HTTP request alone does not unlock an unfinished deletion.
      if (phase === 'request') {
        stub.linkStates.set({ 'link-1': { deleting: true, busy: false } });
        fixture.detectChanges();
        expect(Array.from(row.querySelectorAll<HTMLButtonElement>('button')).every((button) => button.disabled)).toBeTrue();
      }
    });
  }

  it('keeps a deleting row visible when the active-status filter was already selected', () => {
    stub.links.set([createLink()]);
    fixture.detectChanges();
    const widget = fixture.debugElement.query(By.directive(PageFilterWidget)).componentInstance as PageFilterWidget;
    (widget.filters().find((filter) => filter.id === 'status') as SelectFilterOption).onChanged('Active');
    fixture.detectChanges();
    stub.links.set([createLink({ operationStatus: 'CleanupPending' })]);
    fixture.detectChanges();
    expect(cards()[1].querySelectorAll('.item').length).toBe(1);
    expect(actionButtons(1).every((button) => button.disabled)).toBeTrue();
  });

  it('says when the last look at the row was', () => {
    stub.links.set([createLink({ lastCheckedAt: '2026-11-05T18:30:00.000Z', lastSuccessfulCheckAt: '2026-11-05T18:30:00.000Z' })]);
    fixture.detectChanges();

    // Two equal stamps are the same look: it ran, and it had nothing to say, so there is nothing to add.
    expect(rowText(1)).toContain('Last checked');
    expect(rowText(1)).not.toContain('Nothing to report since');
  });

  it('names the last quiet look while the row carries a complaint again', () => {
    stub.links.set([createLink({ lastCheckedAt: '2026-11-05T18:30:00.000Z', lastSuccessfulCheckAt: '2026-11-01T09:00:00.000Z' })]);
    fixture.detectChanges();

    expect(rowText(1)).toContain('Last checked');
    expect(rowText(1)).toContain('Nothing to report since');
  });

  it('says nothing about a check that never ran', () => {
    stub.links.set([createLink({ lastCheckedAt: null, lastSuccessfulCheckAt: null })]);
    fixture.detectChanges();

    expect(rowText(1)).not.toContain('Last checked');
  });

  it('shows the enabled-by-default follow-up switch directly below the first row', () => {
    stub.links.set([createLink()]);
    fixture.detectChanges();
    const row = cards()[1].querySelector('.item')!;
    const followUp = row.querySelector('.follow-up-control')!;
    expect(row.querySelector('.item-head')?.nextElementSibling).toBe(followUp);
    expect(followUp.querySelector('button[role="switch"]')?.getAttribute('aria-checked')).toBe('true');
    expect(actionButtons(1).some((button) => button.textContent?.includes('source upgrades'))).toBeFalse();
  });

  it('enables following with the stored row revision through the switch', async () => {
    stub.links.set([createLink({ autoFollowUp: false })]);
    fixture.detectChanges();
    const toggle = cards()[1].querySelector('.follow-up-control button[role="switch"]') as HTMLButtonElement;
    expect(toggle.getAttribute('aria-checked')).toBe('false');
    toggle.click();
    await settle();
    expect(stub.setFollowUp).toHaveBeenCalledWith('link-1', true, 3);
    expect(toggle.getAttribute('aria-checked')).toBe('true');
  });

  it('disables following with the switch', async () => {
    stub.links.set([createLink()]);
    fixture.detectChanges();
    const toggle = cards()[1].querySelector('.follow-up-control button[role="switch"]') as HTMLButtonElement;
    toggle.click();
    await settle();
    expect(stub.setFollowUp).toHaveBeenCalledWith('link-1', false, 3);
    expect(toggle.getAttribute('aria-checked')).toBe('false');
  });

  it('retains the saved switch state when an update fails', async () => {
    stub.links.set([createLink()]);
    (stub.setFollowUp as jasmine.Spy).and.rejectWith(new Error('Update failed.'));
    fixture.detectChanges();
    const toggle = cards()[1].querySelector('.follow-up-control button[role="switch"]') as HTMLButtonElement;
    toggle.click();
    await settle();
    expect(stub.setFollowUp).toHaveBeenCalledWith('link-1', false, 3);
    expect(toggle.getAttribute('aria-checked')).toBe('true');
  });

  it('disables the follow-up switch while an action on the row is running', () => {
    stub.links.set([createLink()]);
    stub.linkStates.set({ 'link-1': { busy: true } });
    fixture.detectChanges();
    expect((cards()[1].querySelector('.follow-up-control button[role="switch"]') as HTMLButtonElement).disabled).toBeTrue();
  });

  it('explains upgrades, broken old targets and queued work in the info popup', () => {
    stub.links.set([createLink()]);
    fixture.detectChanges();
    (cards()[1].querySelector('.follow-up-control app-info-icon button') as HTMLButtonElement).click();
    fixture.detectChanges();
    const text = TestBed.inject(OverlayContainer).getContainerElement().querySelector('.info-popover')?.textContent ?? '';
    expect(text).toContain('1080p');
    expect(text).toContain('2160p');
    expect(text).toContain('can break if Radarr deletes');
    expect(text).toContain('does not cancel updates already queued');
  });

  it('only deletes a link after the question was answered', async () => {
    stub.links.set([createLink()]);
    fixture.detectChanges();
    const question: jasmine.Spy = spyOn(globalThis, 'confirm').and.returnValue(false);

    actionButtons(1)
      .find((button) => button.textContent?.includes('Delete'))!
      .click();
    await settle();
    expect(stub.deleteLink).not.toHaveBeenCalled();
    expect(question.calls.mostRecent().args[0]).toContain('Only the symlink file will be deleted.');
    expect(question.calls.mostRecent().args[0]).toContain('original movie file');

    question.and.returnValue(true);
    actionButtons(1)
      .find((button) => button.textContent?.includes('Delete'))!
      .click();
    await settle();

    expect(question).toHaveBeenCalledTimes(2);
    expect(stub.deleteLink).toHaveBeenCalledWith('link-1', 3);
  });

  it('releases a declined pair by the whole pair, because that is what the archive stores', async () => {
    stub.rejections.set([createRejection()]);
    fixture.detectChanges();

    actionButtons(2)[0].click();
    await settle();

    expect(stub.reactivateRejection).toHaveBeenCalledWith({
      radarrInstanceId: 'radarr-1',
      radarrMovieId: 41,
      sonarrInstanceId: 'sonarr-1',
      sonarrSeriesId: 12,
      sonarrEpisodeId: 555,
    });
  });

  it('reports a decline that could not be lifted where the list sits', async () => {
    stub.rejections.set([createRejection()]);
    fixture.detectChanges();
    (stub.reactivateRejection as jasmine.Spy).and.rejectWith(new Error('The archive is busy.'));

    actionButtons(2)[0].click();
    await settle();

    expect(element.querySelector('.loading-error-state-message')?.textContent).toContain('The archive is busy.');
  });

  it('points at the paths instead of offering a link that cannot be placed', () => {
    stub.settings.set(createSettings({ mediaRoot: '' }));
    fixture.detectChanges();

    expect(element.querySelector('.configuration-hint')?.textContent).toContain('Configure the shared media directory');
  });
  it('shows only the disabled notice and a settings link below the description when symlinks are off', () => {
    stub.settings.set(createSettings({ enabled: false }));
    stub.suggestions.set([createSuggestion()]);
    stub.links.set([createLink()]);
    stub.rejections.set([createRejection()]);
    fixture.detectChanges();
    expect(element.querySelector('.page-description')).not.toBeNull();
    const content = element.querySelector('.page-content')!;
    expect(content.children.length).toBe(1);
    expect(content.querySelector('[role="status"]')?.textContent).toContain(
      'Symlinks are disabled. Enable them in Settings to resume processing.',
    );
    expect(content.querySelector('.list-controls')).toBeNull();
    expect(content.querySelector('app-page-filter')).toBeNull();
    expect(content.querySelector('.card')).toBeNull();
    const settingsLink = content.querySelector('a')!;
    expect(settingsLink.textContent?.trim()).toBe('Settings');
    expect(settingsLink.getAttribute('href')).toBe('/settings?tab=symlinks');
    expect(settingsLink.classList.contains('mat-mdc-outlined-button')).toBeTrue();
    stub.settings.set(createSettings({ enabled: true }));
    fixture.detectChanges();
    expect(element.querySelector('.list-controls')).not.toBeNull();
    expect(cards().length).toBe(3);
  });
});
