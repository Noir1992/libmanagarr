import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { SymlinkSuggestion } from '../../../interfaces/symlinks/symlink-suggestion';
import { AppI18nService } from '../../../services/app-i18n.service';
import { RejectSymlinkDialogComponent, RejectSymlinkDialogData } from './reject-symlink-dialog.component';

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

describe('RejectSymlinkDialogComponent', () => {
  let fixture: ComponentFixture<RejectSymlinkDialogComponent>;
  let element: HTMLElement;
  let dialogRef: jasmine.SpyObj<MatDialogRef<RejectSymlinkDialogComponent>>;

  function actions(): HTMLButtonElement[] {
    return Array.from(element.querySelectorAll<HTMLButtonElement>('mat-dialog-actions button'));
  }

  function typeReason(text: string): void {
    const detail: HTMLTextAreaElement = element.querySelector('#symlink-reject-detail') as HTMLTextAreaElement;
    detail.value = text;
    detail.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  beforeEach(async () => {
    dialogRef = jasmine.createSpyObj<MatDialogRef<RejectSymlinkDialogComponent>>('MatDialogRef', ['close']);
    const data: RejectSymlinkDialogData = { suggestion: createSuggestion() };

    await TestBed.configureTestingModule({
      imports: [RejectSymlinkDialogComponent],
      providers: [
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: MAT_DIALOG_DATA, useValue: data },
      ],
    }).compileComponents();

    TestBed.inject(AppI18nService).setLanguage('en');
    fixture = TestBed.createComponent(RejectSymlinkDialogComponent);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('names the pair whose decline is being decided', () => {
    const text: string = (element.textContent ?? '').replace(/\s+/g, ' ');

    expect(text).toContain('The Special Movie');
    expect(text).toContain('Season 0, episode 4');
    expect(text).toContain('Declined pairs stay in the rejection archive, where they can be released again.');
  });

  it('keeps the paths out of a decision that is about the pair, not the folder', () => {
    const text: string = element.textContent ?? '';

    expect(text).not.toContain('/data/movies/the-special-movie.mkv');
  });

  it('hands the reason to whoever asked, because the archive is read later', () => {
    typeReason('The film is a different cut.');
    actions()[1].click();

    expect(dialogRef.close).toHaveBeenCalledWith('The film is a different cut.');
  });

  it('sends the reason without the spaces around it', () => {
    typeReason('   The film is a different cut.\t');
    actions()[1].click();

    expect(dialogRef.close).toHaveBeenCalledWith('The film is a different cut.');
  });

  it('sends nothing when the optional reason stayed empty', () => {
    typeReason('   ');
    actions()[1].click();

    expect(dialogRef.close).toHaveBeenCalledWith('');
  });

  it('closes without an answer when the decline was not meant after all', () => {
    actions()[0].click();

    expect(dialogRef.close).toHaveBeenCalledTimes(1);
    expect(dialogRef.close.calls.mostRecent().args).toEqual([]);
  });
});
