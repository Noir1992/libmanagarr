import { signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { SymlinkSettings } from '../../interfaces/symlinks/symlink-settings';
import { SymlinkRejection } from '../../interfaces/symlinks/symlink-decisions';
import { SymlinkLink } from '../../interfaces/symlinks/symlink-link';
import { SymlinkMatchingRun, SymlinkSuggestion } from '../../interfaces/symlinks/symlink-suggestion';
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
  | 'rejectSuggestion'
  | 'checkLink'
  | 'setFollowUp'
  | 'deleteLink'
  | 'reactivateRejection'
>;

function createSettings(overrides: Partial<SymlinkSettings> = {}): SymlinkSettings {
  return {
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
    sonarrEpisodeId: 555,
    sonarrEpisodeTitle: 'Behind the Scenes',
    seasonNumber: 0,
    episodeNumber: 4,
    reasons: [{ code: 'IdenticalTitle', detail: 'The special and the movie have the same title.' }],
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
    autoFollowUp: false,
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
      rejectSuggestion: jasmine.createSpy('rejectSuggestion').and.resolveTo(createSuggestion({ status: 'Rejected' })),
      checkLink: jasmine.createSpy('checkLink').and.resolveTo(createLink()),
      setFollowUp: jasmine.createSpy('setFollowUp').and.resolveTo(createLink({ autoFollowUp: true })),
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

  it('reads the register and the path configuration together', () => {
    // The paths are what the preview is drawn from, so the offers are unusable without them.
    expect(stub.loadOverview).toHaveBeenCalledTimes(1);
    expect(stub.loadSettings).toHaveBeenCalledTimes(1);
  });

  it('names the pair, both titles and the offer state', () => {
    stub.suggestions.set([createSuggestion()]);
    fixture.detectChanges();

    const text: string = rowText(0);
    expect(text).toContain('The Special Movie');
    expect(text).toContain('Season 0, episode 4');
    expect(text).toContain('Behind the Scenes');
    expect(text).toContain('The special and the movie have the same title.');
    expect(text).toContain('Another cut of the same film.');
    expect(text).toContain('Open');
  });

  it('keeps a declined pair out of the offers, which is the archive to show it', () => {
    stub.suggestions.set([createSuggestion({ status: 'Rejected' })]);
    fixture.detectChanges();

    expect(rowText(0)).toBe('');
    expect(cards()[0].textContent).toContain('No open offers');
  });

  it('says what a pass did, and says so quietly when it changed nothing', async () => {
    // The service records the summary of the pass itself, which is what the card header then reads.
    (stub.refreshSuggestions as jasmine.Spy).and.callFake(async (): Promise<SymlinkMatchingRun> => {
      const run: SymlinkMatchingRun = createRun({ didWork: false, created: 0 });
      stub.lastRun.set(run);
      return run;
    });
    const refresh: HTMLButtonElement = cards()[0].querySelector('.card-header-actions button') as HTMLButtonElement;
    refresh.click();
    await settle();

    expect(stub.refreshSuggestions).toHaveBeenCalledTimes(1);
    expect(cards()[0].querySelector('.card-subtitle')?.textContent).toContain('Checked 2 instances: nothing changed.');
  });

  it('puts the current configuration revision into the confirmation', () => {
    stub.suggestions.set([createSuggestion()]);
    fixture.detectChanges();

    actionButtons(0)[0].click();

    expect(dialog.open).toHaveBeenCalledWith(ConfirmSymlinkDialogComponent, jasmine.objectContaining({ data: { suggestion: stub.suggestions()[0], configRevision: 7 } }));
  });

  it('refuses to promise a link while the configuration is still unknown', () => {
    stub.settings.set(null);
    stub.suggestions.set([createSuggestion()]);
    fixture.detectChanges();

    actionButtons(0)[0].click();

    expect(dialog.open).not.toHaveBeenCalled();
  });

  it('declines with the sentence the author typed', async () => {
    dialog.open.and.returnValue({ afterClosed: () => of('The film is a different cut.') } as unknown as MatDialogRef<RejectSymlinkDialogComponent>);
    stub.suggestions.set([createSuggestion()]);
    fixture.detectChanges();

    actionButtons(0)[1].click();
    await settle();

    expect(dialog.open).toHaveBeenCalledWith(RejectSymlinkDialogComponent, jasmine.objectContaining({ data: { suggestion: stub.suggestions()[0] } }));
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

  it('shows the preview of a managed link with its state and the step that is still open', () => {
    stub.links.set([createLink({ operationStatus: 'Updating', setupStep: 'RescanRequested' })]);
    fixture.detectChanges();

    const text: string = rowText(1);
    expect(text).toContain('/data/series/the-series/season-00/s00e04.mkv');
    expect(text).toContain('The size differs from the expected one.');
    expect(text).toContain('Updating');
    expect(text).toContain('Step: Rescan requested');
  });

  it('looks at the row a person asked about', async () => {
    stub.links.set([createLink()]);
    fixture.detectChanges();

    actionButtons(1)[0].click();
    await settle();

    expect(stub.checkLink).toHaveBeenCalledWith('link-1');
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

  it('follows source upgrades with the row it was read from', async () => {
    stub.links.set([createLink()]);
    fixture.detectChanges();

    actionButtons(1)[1].click();
    await settle();

    expect(stub.setFollowUp).toHaveBeenCalledWith('link-1', true, 3);
  });

  it('only deletes a link after the question was answered', async () => {
    stub.links.set([createLink()]);
    fixture.detectChanges();
    const question: jasmine.Spy = spyOn(globalThis, 'confirm').and.returnValue(false);

    actionButtons(1)[2].click();
    await settle();
    expect(stub.deleteLink).not.toHaveBeenCalled();

    question.and.returnValue(true);
    actionButtons(1)[2].click();
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
    stub.settings.set(createSettings({ storageAreas: [] }));
    fixture.detectChanges();

    expect(element.querySelector('.configuration-hint')?.textContent).toContain('A storage area is needed');
  });
});
