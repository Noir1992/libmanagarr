import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { ExistingLinkConfirmation, SymlinkFinding, SymlinkLink, SymlinkLinkPlan } from '../../../interfaces/symlinks/symlink-link';
import { SymlinkSuggestion } from '../../../interfaces/symlinks/symlink-suggestion';
import { AppI18nService } from '../../../services/app-i18n.service';
import { SymlinkService } from '../../../services/symlink.service';
import { ConfirmSymlinkDialogComponent, ConfirmSymlinkDialogData } from './confirm-symlink-dialog.component';

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
    reasons: [],
    alternatives: [],
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

function createDraft(overrides: Partial<SymlinkLink> = {}): SymlinkLink {
  return {
    id: 'draft',
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
    operationStatus: 'Pending',
    setupStep: null,
    findings: [],
    monitoringOriginal: true,
    monitoringDisabled: false,
    autoFollowUp: true,
    desiredFilename: 'the-series-s00e04.mkv',
    appliedFilename: null,
    sourceVersion: null,
    expectedMetadata: null,
    confirmedMetadata: null,
    suggestionId: 'suggestion-1',
    revision: 0,
    createdAt: '2026-11-01T09:00:00.000Z',
    updatedAt: '2026-11-01T09:00:00.000Z',
    lastCheckedAt: null,
    lastSuccessfulCheckAt: null,
    ...overrides,
  };
}

function createFinding(code: string, detail: string): SymlinkFinding {
  return { code, detail, detectedAt: '2026-11-06T09:00:00.000Z' };
}

describe('ConfirmSymlinkDialogComponent', () => {
  let fixture: ComponentFixture<ConfirmSymlinkDialogComponent>;
  let element: HTMLElement;
  let previewLink: jasmine.Spy<(request: unknown) => Promise<SymlinkLinkPlan>>;
  let confirmSuggestion: jasmine.Spy<
    (id: string, configRevision: number, sourceVersion?: string | null, existingLink?: ExistingLinkConfirmation) => Promise<SymlinkLink>
  >;
  let dialogRef: jasmine.SpyObj<MatDialogRef<ConfirmSymlinkDialogComponent>>;

  const data: ConfirmSymlinkDialogData = { suggestion: createSuggestion(), configRevision: 7 };

  /** The constructor reads the preview, so the fixture is only built once the spies answer the way this test needs. */
  async function open(): Promise<void> {
    fixture = TestBed.createComponent(ConfirmSymlinkDialogComponent);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle();
  }

  /** A handler is its own async chain, so one whenStable round after a click is not enough. */
  async function settle(): Promise<void> {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  function actionButtons(): HTMLButtonElement[] {
    return Array.from(element.querySelectorAll<HTMLButtonElement>('mat-dialog-actions button'));
  }

  beforeEach(async () => {
    previewLink = jasmine.createSpy('previewLink').and.resolveTo({ runnable: true, draft: createDraft(), blockers: [], warnings: [] });
    confirmSuggestion = jasmine.createSpy('confirmSuggestion').and.resolveTo(createDraft({ id: 'link-1' }));
    dialogRef = jasmine.createSpyObj<MatDialogRef<ConfirmSymlinkDialogComponent>>('MatDialogRef', ['close']);

    await TestBed.configureTestingModule({
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: SymlinkService, useValue: { previewLink, confirmSuggestion } },
      ],
    }).compileComponents();

    TestBed.inject(AppI18nService).setLanguage('en');
  });

  it('lays out the link the decision would write before it is written', async () => {
    await open();

    expect(previewLink).toHaveBeenCalledWith({
      radarrInstanceId: 'radarr-1',
      radarrMovieId: 41,
      sonarrInstanceId: 'sonarr-1',
      sonarrSeriesId: 12,
      sonarrEpisodeId: 555,
      radarrFileId: 900,
    });
    const text: string = (element.querySelector('.dialog-content')?.textContent ?? '').replace(/\s+/g, ' ');
    expect(text).toContain('The Special Movie');
    expect(text).toContain('Season 0, episode 4');
    expect(text).not.toContain('/data/series/the-series/season-00/s00e04.mkv');
    expect(text).not.toContain('the-series-s00e04.mkv');
  });

  it('omits the provisional link path and its explanation from the confirmation', async () => {
    await open();
    const content = element.querySelector('.plan-grid')!;
    expect(content.textContent).toContain('/data/movies/the-special-movie.mkv');
    expect(Array.from(content.querySelectorAll('dt'), (label) => label.textContent?.trim())).not.toContain('Link path');
    expect(element.textContent).not.toContain('Provisional name.');
  });

  it('shows the source and destination instance names from the current preview', async () => {
    previewLink.and.resolveTo({
      runnable: true,
      draft: createDraft({ radarrInstanceName: 'Movies on NAS', sonarrInstanceName: 'Test Sonarr' }),
      blockers: [],
      warnings: [],
    });
    await open();
    expect(element.querySelector('.plan-radarr-instance')?.textContent?.trim()).toBe('Movies on NAS');
    expect(element.querySelector('.plan-sonarr-instance')?.textContent?.trim()).toBe('Test Sonarr');
  });

  it('still identifies both instances when their display names are unavailable', async () => {
    await open();
    expect(element.querySelector('.plan-radarr-instance')?.textContent?.trim()).toBe('radarr-1');
    expect(element.querySelector('.plan-sonarr-instance')?.textContent?.trim()).toBe('sonarr-1');
  });

  it('leaves the last word to the blocker that stands in the way', async () => {
    previewLink.and.resolveTo({
      runnable: false,
      draft: createDraft(),
      blockers: [createFinding('TargetOccupied', 'Another file already sits at that path.')],
      warnings: [createFinding('SourceWithoutKnownSize', 'The size of the file is not known.')],
    });
    await open();

    const text: string = (element.querySelector('.plan-findings')?.textContent ?? '').replace(/\s+/g, ' ');
    expect(text).toContain('TargetOccupied');
    expect(text).toContain('Another file already sits at that path.');
    expect(text).toContain('SourceWithoutKnownSize');
    expect(text).toContain('The size of the file is not known.');
    expect(element.querySelector('.plan-refused')?.textContent).toContain('cannot be created while something stands in the way');
    expect(actionButtons()[1].disabled).toBeTrue();
  });

  it('confirms with the configuration the preview was drawn from', async () => {
    await open();

    actionButtons()[1].click();
    await settle();

    expect(confirmSuggestion).toHaveBeenCalledWith('suggestion-1', 7, null);
    expect(dialogRef.close).toHaveBeenCalledWith(jasmine.objectContaining({ id: 'link-1' }));
  });

  it('keeps the label and shows the spinner on the button while the write runs', async () => {
    let finishWrite: ((link: SymlinkLink) => void) | undefined;
    confirmSuggestion.and.returnValue(new Promise<SymlinkLink>((resolve) => (finishWrite = resolve)));
    await open();

    actionButtons()[1].click();
    await settle();

    // The label names the action that is running; only the icon beside it reports the progress.
    const button = actionButtons()[1];
    expect(button.disabled).toBeTrue();
    expect(button.textContent).toContain('Create it now');
    expect(button.textContent).not.toContain('Loading data...');
    expect(button.querySelector('mat-icon.spinning')).not.toBeNull();

    finishWrite?.(createDraft({ id: 'link-1' }));
    await settle();
    expect(dialogRef.close).toHaveBeenCalledWith(jasmine.objectContaining({ id: 'link-1' }));
  });

  it('shows the existing link and its metadata and explicitly confirms adoption', async () => {
    previewLink.and.resolveTo({
      runnable: true,
      adoptsExisting: true,
      draft: createDraft({
        sonarrFileId: 88,
        sourceVersion: 'source-v1',
        desiredFilename: 'existing-special.mkv',
        linkPath: '/data/series/the-series/Season 00/existing-special.mkv',
        linkText: '../../../movies/the-special-movie.mkv',
        expectedMetadata: { origin: 'radarr', releaseGroup: 'Group', languages: ['German'], quality: 'WEBDL-1080p', size: 123 },
      }),
      blockers: [],
      warnings: [],
    });
    await open();
    expect(element.querySelector('.dialog-title')?.textContent).toContain('Adopt existing symlink');
    expect(element.querySelector('.plan-link-path app-info-icon')).toBeNull();
    expect(element.querySelector('.dialog-content')?.textContent).toContain('without recreating, moving or renaming');
    const paths = Array.from(element.querySelectorAll('.plan-path'), (path) => path.textContent?.trim());
    expect(paths).toEqual(['/data/movies/the-special-movie.mkv', '../../../movies/the-special-movie.mkv']);
    const content = element.querySelector('.plan-grid')?.textContent;
    expect(content).toContain('Group');
    expect(content).toContain('German');
    expect(content).toContain('WEBDL-1080p');
    expect(Array.from(element.querySelectorAll('.plan-grid dt'), (label) => label.textContent?.trim())).not.toContain('Desired filename');
    expect(actionButtons()[1].textContent).toContain('Adopt this link');
    expect(actionButtons()[1].disabled).toBeFalse();
    actionButtons()[1].click();
    await settle();
    expect(confirmSuggestion).toHaveBeenCalledWith('suggestion-1', 7, 'source-v1', {
      path: '/data/series/the-series/Season 00/existing-special.mkv',
      linkText: '../../../movies/the-special-movie.mkv',
      sonarrFileId: 88,
    });
  });

  it('keeps the window open and says so when the write was refused', async () => {
    await open();
    confirmSuggestion.and.rejectWith(new Error('The path configuration moved.'));

    actionButtons()[1].click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.querySelector('.feedback.error')?.textContent).toContain('The path configuration moved.');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('reports a preview that never arrived instead of an empty window', async () => {
    previewLink.and.rejectWith(new Error('The movie is not in the snapshot.'));
    await open();

    expect(element.querySelector('.feedback.error')?.textContent).toContain('The movie is not in the snapshot.');
    expect(element.querySelector('.plan-grid')).toBeNull();
  });
});
