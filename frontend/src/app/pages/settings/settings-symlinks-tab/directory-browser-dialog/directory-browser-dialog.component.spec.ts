import { OverlayContainer } from '@angular/cdk/overlay';
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { SymlinkDirectoryListing } from '../../../../interfaces/symlinks/symlink-settings';
import { SymlinkService } from '../../../../services/symlink.service';
import { DirectoryBrowserDialogComponent } from './directory-browser-dialog.component';

describe('DirectoryBrowserDialogComponent', () => {
  let fixture: ComponentFixture<DirectoryBrowserDialogComponent>;
  let browse: jasmine.Spy;
  let close: jasmine.Spy;
  const listing: SymlinkDirectoryListing = {
    path: '/media',
    parentPath: '/',
    canSelect: true,
    directories: [{ name: 'movies', path: '/media/movies' }],
    truncated: false,
  };

  beforeEach(async () => {
    browse = jasmine.createSpy('browseDirectories').and.resolveTo(listing);
    close = jasmine.createSpy('close');
    await TestBed.configureTestingModule({
      imports: [DirectoryBrowserDialogComponent],
      providers: [
        { provide: SymlinkService, useValue: { browseDirectories: browse } },
        { provide: MAT_DIALOG_DATA, useValue: { path: '/media' } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(DirectoryBrowserDialogComponent);
  });

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  it('navigates folders and returns the selected directory', async () => {
    await settle();
    expect(browse).toHaveBeenCalledOnceWith('/media');
    browse.and.resolveTo({ ...listing, path: '/media/movies', parentPath: '/media', directories: [] });
    (fixture.nativeElement.querySelector('.directories button') as HTMLButtonElement).click();
    await settle();
    expect(browse).toHaveBeenCalledWith('/media/movies');
    expect(fixture.nativeElement.querySelector('.current-path').textContent).toContain('/media/movies');
    (fixture.nativeElement.querySelector('mat-dialog-actions button:last-child') as HTMLButtonElement).click();
    expect(close).toHaveBeenCalledWith('/media/movies');
  });

  it('returns to the top level with the Home icon button', async () => {
    await settle();
    browse.and.resolveTo({
      path: '/',
      parentPath: null,
      canSelect: false,
      directories: [{ name: 'media', path: '/media' }],
      truncated: false,
    });
    const home = fixture.nativeElement.querySelector('button[aria-label="Home"]') as HTMLButtonElement;
    expect(home.querySelector('mat-icon')?.textContent).toBe('home');
    home.click();
    await settle();
    expect(browse).toHaveBeenCalledWith(null);
    expect(fixture.nativeElement.querySelector('.current-path').textContent).toContain('/');
    expect(fixture.nativeElement.querySelector('.directories').textContent).toContain('media');
  });

  it('navigates to the parent with the Up icon button', async () => {
    await settle();
    browse.and.resolveTo({ path: '/', parentPath: null, canSelect: false, directories: [], truncated: false });
    const up = fixture.nativeElement.querySelector('button[aria-label="Up"]') as HTMLButtonElement;
    expect(up.querySelector('mat-icon')?.textContent).toBe('arrow_upward');
    up.click();
    await settle();
    expect(browse).toHaveBeenCalledWith('/');
    expect((fixture.nativeElement.querySelector('button[aria-label="Up"]') as HTMLButtonElement).disabled).toBeTrue();
  });

  it('returns no directory when Cancel is clicked', async () => {
    await settle();
    (fixture.nativeElement.querySelector('mat-dialog-actions button') as HTMLButtonElement).click();
    expect(close).toHaveBeenCalledOnceWith(undefined);
  });

  it('opens filesystem locations when the configured directory is unavailable', async () => {
    browse.and.callFake((path: string | null) =>
      path
        ? Promise.reject(new HttpErrorResponse({ status: 404 }))
        : Promise.resolve({ path: null, parentPath: null, canSelect: false, directories: [{ name: '/', path: '/' }], truncated: false }),
    );
    await settle();
    expect(browse).toHaveBeenCalledWith(null);
    expect(fixture.nativeElement.querySelector('mat-dialog-actions button:last-child').disabled).toBeTrue();
    expect(fixture.nativeElement.querySelector('.directories').textContent).toContain('/');
  });

  it('displays navigation failures and prevents selecting a stale directory', async () => {
    await settle();
    browse.and.rejectWith(new HttpErrorResponse({ status: 403, error: { detail: 'Permission denied.' } }));
    (fixture.nativeElement.querySelector('.directories button') as HTMLButtonElement).click();
    await settle();
    expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain('Permission denied.');
    expect(fixture.nativeElement.querySelector('mat-dialog-actions button:last-child').disabled).toBeTrue();
  });
  it('grows past the default content limit when the dialog has room', async () => {
    browse.and.resolveTo({
      ...listing,
      directories: Array.from({ length: 20 }, (_, index) => ({ name: `Folder ${index}`, path: `/media/${index}` })),
    });
    const dialog = TestBed.inject(MatDialog).open(DirectoryBrowserDialogComponent, {
      width: '640px',
      maxHeight: '2000px',
      enterAnimationDuration: 0,
      exitAnimationDuration: 0,
      data: { path: '/media' },
    });
    try {
      await settle();
      const overlay = TestBed.inject(OverlayContainer).getContainerElement();
      const content = overlay.querySelector('mat-dialog-content') as HTMLElement;
      const directories = overlay.querySelector('.directories') as HTMLElement;
      expect(content.clientHeight).toBeGreaterThan(window.innerHeight * 0.65);
      expect(content.scrollHeight).toBeLessThanOrEqual(content.clientHeight + 1);
      expect(directories.scrollHeight).toBeLessThanOrEqual(directories.clientHeight + 1);
    } finally {
      dialog.close();
    }
  });

  it('scrolls only the folders while navigation, path and actions stay visible', async () => {
    browse.and.resolveTo({
      ...listing,
      directories: Array.from({ length: 100 }, (_, index) => ({ name: `Folder ${index}`, path: `/media/${index}` })),
    });
    const dialog = TestBed.inject(MatDialog).open(DirectoryBrowserDialogComponent, {
      width: '640px',
      maxHeight: 'calc(100dvh - 48px)',
      enterAnimationDuration: 0,
      exitAnimationDuration: 0,
      data: { path: '/media' },
    });
    try {
      await settle();
      const overlay = TestBed.inject(OverlayContainer).getContainerElement();
      const surface = overlay.querySelector('.mat-mdc-dialog-surface') as HTMLElement;
      const content = overlay.querySelector('mat-dialog-content') as HTMLElement;
      const directories = overlay.querySelector('.directories') as HTMLElement;
      const fixedElements = Array.from(overlay.querySelectorAll<HTMLElement>('.hint, .browser-nav, .current-path, mat-dialog-actions'));
      const fixedPositions = fixedElements.map((element) => element.getBoundingClientRect().top);
      const actions = overlay.querySelector('mat-dialog-actions') as HTMLElement;
      expect(surface.getBoundingClientRect().height).toBeLessThanOrEqual(window.innerHeight - 47);
      expect(content.scrollHeight).toBeLessThanOrEqual(content.clientHeight + 1);
      expect(directories.scrollHeight).toBeGreaterThan(directories.clientHeight);
      directories.scrollTop = directories.scrollHeight;
      expect(directories.scrollTop).toBeGreaterThan(0);
      fixedElements.forEach((element, index) => expect(element.getBoundingClientRect().top).toBeCloseTo(fixedPositions[index], 1));
      expect(content.scrollTop).toBe(0);
      expect(actions.getBoundingClientRect().bottom).toBeLessThanOrEqual(window.innerHeight - 23);
      expect(surface.scrollHeight).toBeLessThanOrEqual(surface.clientHeight + 1);
    } finally {
      dialog.close();
    }
  });
});
