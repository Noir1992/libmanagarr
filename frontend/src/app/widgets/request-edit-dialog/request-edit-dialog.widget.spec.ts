import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl } from '@angular/forms';
import { MatSelect } from '@angular/material/select';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { By } from '@angular/platform-browser';
import { SeerrRequestEditOptions } from '../../interfaces/requests/seerr-request-edit-options';
import { RequestEditDialogWidget } from './request-edit-dialog.widget';

const movieOptions: SeerrRequestEditOptions = {
  qualityProfiles: [
    { id: 15, name: 'Standard' },
    { id: 16, name: 'High Resolution' },
  ],
  rootFolders: ['/media/videos/movies', '/media/videos/movies (anime)'],
  seasons: [],
  profileId: 15,
  rootFolder: '/media/videos/movies',
};

// The specials and season two are still free, season one belongs to the request being edited and
// season three is already downloading, so only that last switch has to stay locked.
const tvOptions: SeerrRequestEditOptions = {
  qualityProfiles: [{ id: 18, name: 'Standard' }],
  rootFolders: ['/media/videos/series', '/media/videos/series (anime)'],
  seasons: [
    { number: 0, episodeCount: 4, state: 'none' },
    { number: 1, episodeCount: 12, state: 'pending' },
    { number: 2, episodeCount: 10, state: 'none' },
    { number: 3, episodeCount: null, state: 'processing' },
  ],
  profileId: 18,
  rootFolder: '/media/videos/series',
};

function createDialog(
  title: string | null,
  options: SeerrRequestEditOptions,
): { fixture: ComponentFixture<RequestEditDialogWidget>; dialogRefSpy: jasmine.SpyObj<MatDialogRef<RequestEditDialogWidget>> } {
  const dialogRefSpy = jasmine.createSpyObj<MatDialogRef<RequestEditDialogWidget>>('MatDialogRef', ['close']);

  TestBed.configureTestingModule({
    imports: [RequestEditDialogWidget],
    providers: [
      { provide: MatDialogRef, useValue: dialogRefSpy },
      { provide: MAT_DIALOG_DATA, useValue: { title, options } },
    ],
  });

  return { fixture: TestBed.createComponent(RequestEditDialogWidget), dialogRefSpy };
}

function seasonControlsOf(fixture: ComponentFixture<RequestEditDialogWidget>): FormControl<boolean>[] {
  return fixture.componentInstance['form'].controls['seasons']['controls'] as FormControl<boolean>[];
}

function toggleButtonsOf(element: HTMLElement): HTMLButtonElement[] {
  return Array.from(element.querySelectorAll<HTMLButtonElement>('mat-slide-toggle button').values());
}

describe('RequestEditDialogWidget', () => {
  it('renders profile and root folder selects without a server field or unchanged option', () => {
    const { fixture } = createDialog('Sample Movie', movieOptions);
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.request-dialog-title')?.textContent?.trim()).toBe('Edit request');
    expect(element.querySelector('.request-dialog-media-title')?.textContent?.trim()).toBe('Sample Movie');
    // Mat-select only mounts its options while open, so the closed dialog shows the two controls.
    const selects: NodeListOf<Element> = element.querySelectorAll('mat-select');
    expect(selects).toHaveSize(2);
    expect(selects[0].getAttribute('id')).toBe('request-edit-profile');
    expect(selects[1].getAttribute('id')).toBe('request-edit-root-folder');
    expect(Array.from(element.querySelectorAll('.field label').values()).map((label) => label.textContent?.trim())).toEqual([
      'Quality profile',
      'Root folder',
    ]);
  });

  it('lists both root folders without an unchanged option when the select is open', async () => {
    const { fixture } = createDialog('Sample Movie', movieOptions);
    fixture.detectChanges();

    // The options only exist while the panel is open, so the overlay needs the stable state.
    const folderSelect: MatSelect = fixture.debugElement.queryAll(By.directive(MatSelect))[1].componentInstance as MatSelect;
    folderSelect.open();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const optionTexts: string[] = Array.from(document.querySelectorAll<HTMLElement>('.mat-mdc-option').values()).map(
      (option) => option.textContent?.trim() ?? '',
    );
    expect(optionTexts).toEqual(['/media/videos/movies', '/media/videos/movies (anime)']);
  });

  it('preselects the requests own values so saving starts enabled', () => {
    const { fixture } = createDialog('Sample Movie', movieOptions);
    fixture.detectChanges();

    const form: { profileId: number | null; rootFolder: string | null } = fixture.componentInstance['form'].getRawValue();
    expect(form.profileId).toBe(15);
    expect(form.rootFolder).toBe('/media/videos/movies');

    const saveButton: HTMLButtonElement = (fixture.nativeElement as HTMLElement).querySelector('.request-dialog-save')!;
    expect(saveButton.disabled).toBeFalse();
    expect(saveButton.textContent?.trim()).toBe('Save & approve');
  });

  it('falls back to the first option when the request carries no own values', () => {
    const { fixture } = createDialog('Sample Movie', { ...movieOptions, profileId: null, rootFolder: null });
    fixture.detectChanges();

    const form: { profileId: number | null; rootFolder: string | null } = fixture.componentInstance['form'].getRawValue();
    expect(form.profileId).toBe(15);
    expect(form.rootFolder).toBe('/media/videos/movies');
  });

  it('shows one table row per season with its episode count and state', () => {
    const { fixture } = createDialog('Sample Series', tvOptions);
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement as HTMLElement;
    expect(
      Array.from(element.querySelectorAll('.request-seasons thead th').values()).map((heading) => heading.textContent?.trim()),
    ).toEqual(['Seasons', 'Season', 'Episodes', 'Status']);

    const rows: NodeListOf<HTMLTableRowElement> = element.querySelectorAll('.request-seasons tbody tr');
    expect(rows).toHaveSize(4);
    expect(Array.from(rows.values()).map((row) => row.cells[1].textContent?.trim())).toEqual([
      'Specials',
      'Season 1',
      'Season 2',
      'Season 3',
    ]);
    expect(Array.from(rows.values()).map((row) => row.cells[2].textContent?.trim())).toEqual(['4', '12', '10', '']);
    expect(Array.from(rows.values()).map((row) => row.cells[3].textContent?.trim())).toEqual([
      'Not requested',
      'Pending',
      'Not requested',
      'Processing',
    ]);
  });

  it('keeps the edited seasons switch enabled and checked and locks only unavailable seasons', () => {
    const { fixture } = createDialog('Sample Series', tvOptions);
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement as HTMLElement;
    const toggles: HTMLButtonElement[] = toggleButtonsOf(element);
    expect(toggles).toHaveSize(4);
    expect(toggles.map((toggle) => toggle.hasAttribute('disabled'))).toEqual([false, false, false, true]);
    expect(Array.from(element.querySelectorAll('.request-season-locked').values())).toHaveSize(1);
    expect(
      Array.from(element.querySelectorAll<HTMLTableRowElement>('.request-seasons tbody tr').values())
        .filter((row) => row.classList.contains('request-season-locked'))
        .map((row) => row.cells[1].textContent?.trim()),
    ).toEqual(['Season 3']);

    expect(seasonControlsOf(fixture).map((control) => control.value)).toEqual([false, true, false, false]);
  });

  it('stacks the season table above the advanced server settings', () => {
    const { fixture } = createDialog('Sample Series', tvOptions);
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement as HTMLElement;
    const heading: HTMLElement = element.querySelector('.request-advanced-heading')!;
    expect(heading.textContent?.trim()).toBe('Advanced');

    const table: HTMLElement = element.querySelector('.request-seasons')!;
    // The advanced block carries both selects and follows the table, so the choice comes first.
    expect(table.compareDocumentPosition(heading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(heading.compareDocumentPosition(element.querySelector('#request-edit-profile')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(element.querySelector('.request-advanced')?.contains(element.querySelector('#request-edit-root-folder')!)).toBeTrue();
  });

  it('skips the advanced heading when there is nothing above the server settings', () => {
    const { fixture } = createDialog('Sample Movie', movieOptions);
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.request-advanced-heading')).toBeNull();
    expect(element.querySelector('.request-advanced')?.contains(element.querySelector('#request-edit-profile')!)).toBeTrue();
  });

  it('closes with profile and root folder only when a movie request is saved', () => {
    const { fixture, dialogRefSpy } = createDialog('Sample Movie', movieOptions);
    fixture.detectChanges();

    fixture.componentInstance['form'].controls['profileId'].setValue(16);
    fixture.componentInstance['form'].controls['rootFolder'].setValue('/media/videos/movies (anime)');
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.request-dialog-save')!.click();

    expect(dialogRefSpy.close).toHaveBeenCalledOnceWith({
      profileId: 16,
      rootFolder: '/media/videos/movies (anime)',
    });
  });

  it('closes with the picked seasons next to the preselected pending season', () => {
    const { fixture, dialogRefSpy } = createDialog('Sample Series', tvOptions);
    fixture.detectChanges();

    const toggles: HTMLButtonElement[] = toggleButtonsOf(fixture.nativeElement as HTMLElement);
    toggles[2].click();
    fixture.detectChanges();

    expect(dialogRefSpy.close).not.toHaveBeenCalled();
    fixture.componentInstance['save']();

    expect(dialogRefSpy.close).toHaveBeenCalledOnceWith({
      profileId: 18,
      rootFolder: '/media/videos/series',
      seasons: [1, 2],
    });
  });

  it('blocks saving while no season is picked and frees it again on the first toggle', () => {
    const { fixture, dialogRefSpy } = createDialog('Sample Series', {
      ...tvOptions,
      seasons: tvOptions.seasons.map((season) => ({ ...season, state: 'none' as const })),
    });
    fixture.detectChanges();

    const saveButton: HTMLButtonElement = (fixture.nativeElement as HTMLElement).querySelector('.request-dialog-save')!;
    expect(saveButton.disabled).toBeTrue();

    fixture.componentInstance['save']();
    expect(dialogRefSpy.close).not.toHaveBeenCalled();

    toggleButtonsOf(fixture.nativeElement as HTMLElement)[0].click();
    fixture.detectChanges();
    expect(saveButton.disabled).toBeFalse();
  });

  it('keeps save disabled when nothing can be selected at all', () => {
    const { fixture, dialogRefSpy } = createDialog('Sample Movie', {
      qualityProfiles: [],
      rootFolders: [],
      seasons: [],
      profileId: null,
      rootFolder: null,
    });
    fixture.detectChanges();

    const saveButton: HTMLButtonElement = (fixture.nativeElement as HTMLElement).querySelector('.request-dialog-save')!;
    expect(saveButton.disabled).toBeTrue();

    fixture.componentInstance['save']();
    expect(dialogRefSpy.close).not.toHaveBeenCalled();
  });

  it('closes plainly on cancel', () => {
    const { fixture, dialogRefSpy } = createDialog('Sample Movie', movieOptions);
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.request-dialog-cancel')!.click();
    expect(dialogRefSpy.close).toHaveBeenCalledOnceWith();
  });
});
