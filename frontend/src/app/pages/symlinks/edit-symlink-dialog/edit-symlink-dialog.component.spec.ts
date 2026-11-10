import { OverlayContainer } from '@angular/cdk/overlay';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatSelectHarness } from '@angular/material/select/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { SymlinkLink, SymlinkLinkPlan } from '../../../interfaces/symlinks/symlink-link';
import { SymlinkSettings } from '../../../interfaces/symlinks/symlink-settings';
import { AppI18nService } from '../../../services/app-i18n.service';
import { SymlinkService } from '../../../services/symlink.service';
import { EditSymlinkDialogComponent } from './edit-symlink-dialog.component';

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((accept, refuse) => {
    resolve = accept;
    reject = refuse;
  });
  return { promise, resolve, reject };
}

describe('EditSymlinkDialogComponent', () => {
  let fixture: ComponentFixture<EditSymlinkDialogComponent>;
  let service: jasmine.SpyObj<SymlinkService>;
  let closed: jasmine.Spy;
  const draft = {
    id: 'link-1',
    revision: 9,
    radarrInstanceId: 'radarr',
    radarrMovieId: 70,
    sonarrInstanceId: 'sonarr',
    sonarrSeriesId: 12,
    sonarrEpisodeId: 5,
    sourceVersion: 'v1',
    linkPath: '/media/series/special.mkv',
    linkText: '../movie.mkv',
    sourcePath: '/media/movie.mkv',
  } as SymlinkLink;
  const plan: SymlinkLinkPlan = { runnable: true, draft, warnings: [], blockers: [] };

  beforeEach(async () => {
    service = jasmine.createSpyObj<SymlinkService>('SymlinkService', ['loadChoices', 'loadSettings', 'previewLink', 'saveLink'], {
      settings: signal({ revision: 4 } as SymlinkSettings),
    });
    service.loadChoices.and.resolveTo({
      movies: [{ instanceId: 'radarr', instanceName: 'Movies', movieId: 70, title: 'Movie', fileId: 10, path: '/media/movie.mkv' }],
      specials: [
        {
          instanceId: 'sonarr',
          instanceName: 'Series',
          seriesId: 12,
          seriesTitle: 'Show',
          episodeId: 5,
          episodeNumber: 5,
          title: 'Movie',
          hasFile: true,
        },
        {
          instanceId: 'sonarr',
          instanceName: 'Series',
          seriesId: 12,
          seriesTitle: 'Show',
          episodeId: 6,
          episodeNumber: 6,
          title: 'Another special',
          hasFile: false,
        },
      ],
    });
    service.loadSettings.and.resolveTo();
    service.previewLink.and.resolveTo(plan);
    service.saveLink.and.resolveTo(draft);
    closed = jasmine.createSpy('close');
    await TestBed.configureTestingModule({
      imports: [EditSymlinkDialogComponent],
      providers: [
        { provide: SymlinkService, useValue: service },
        { provide: MAT_DIALOG_DATA, useValue: draft },
        { provide: MatDialogRef, useValue: { close: closed } },
      ],
    }).compileComponents();
    TestBed.inject(AppI18nService).setLanguage('en');
    fixture = TestBed.createComponent(EditSymlinkDialogComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  function button(text: string): HTMLButtonElement {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(
      (value) => value.textContent?.trim() === text,
    )!;
  }

  async function settlePreview(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('automatically previews the preselected pair and refreshes when the selection changes', async () => {
    expect(button('Check preview')).toBeUndefined();
    await settlePreview();
    expect(service.previewLink).toHaveBeenCalledWith({
      radarrInstanceId: 'radarr',
      radarrMovieId: 70,
      radarrFileId: 10,
      sonarrInstanceId: 'sonarr',
      sonarrSeriesId: 12,
      sonarrEpisodeId: 5,
      replacesId: 'link-1',
    });
    expect(button('Save').disabled).toBeFalse();
    const select = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatSelectHarness.with({ selector: '#symlink-special' }));
    await select.clickOptions({ text: /Special 6/ });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(service.previewLink).toHaveBeenCalledTimes(2);
    expect(service.previewLink.calls.mostRecent().args[0].sonarrEpisodeId).toBe(6);
    expect(button('Save').disabled).toBeFalse();
  });

  it('automatically refreshes when the movie changes too', async () => {
    await settlePreview();
    fixture.componentInstance['choices'].update((choices) => ({
      ...choices,
      movies: [...choices.movies, { ...choices.movies[0], movieId: 71, fileId: 11, title: 'Second film', path: '/media/second.mkv' }],
    }));
    fixture.detectChanges();
    const select = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatSelectHarness.with({ selector: '#symlink-movie' }));
    await select.clickOptions({ text: 'Second film' });
    expect(service.previewLink.calls.mostRecent().args[0]).toEqual(jasmine.objectContaining({ radarrMovieId: 71, radarrFileId: 11 }));
    expect(button('Save').disabled).toBeFalse();
  });

  for (const staleResult of ['success', 'error'] as const) {
    it(`ignores a late ${staleResult} from an older preview`, async () => {
      await settlePreview();
      const old = deferred<SymlinkLinkPlan>();
      const latest = deferred<SymlinkLinkPlan>();
      service.previewLink.and.returnValues(old.promise, latest.promise);
      const component = fixture.componentInstance;
      component['model'].update((value) => ({ ...value, special: 'sonarr:6' }));
      component['onSelectionChanged']();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(button('Save').disabled).toBeTrue();
      component['model'].update((value) => ({ ...value, special: 'sonarr:5' }));
      component['onSelectionChanged']();
      await fixture.whenStable();
      const currentPlan = { ...plan, draft: { ...draft, sourceVersion: 'newest-version' } };
      latest.resolve(currentPlan);
      await fixture.whenStable();
      fixture.detectChanges();
      expect(component['plan']()).toBe(currentPlan);
      if (staleResult === 'success') old.resolve({ ...plan, draft: { ...draft, sonarrEpisodeId: 6, sourceVersion: 'old-version' } });
      else old.reject(new Error('Old request failed.'));
      await fixture.whenStable();
      fixture.detectChanges();
      expect(component['plan']()).toBe(currentPlan);
      expect(component['error']()).toBe('');
      expect(button('Save').disabled).toBeFalse();
      button('Save').click();
      await fixture.whenStable();
      expect(service.saveLink).toHaveBeenCalledWith(
        jasmine.objectContaining({ sonarrEpisodeId: 5, sourceVersion: 'newest-version' }),
        'link-1',
      );
    });
  }

  it('keeps Save blocked when an old request finishes before the current preview', async () => {
    await settlePreview();
    const old = deferred<SymlinkLinkPlan>();
    const latest = deferred<SymlinkLinkPlan>();
    service.previewLink.and.returnValues(old.promise, latest.promise);
    const component = fixture.componentInstance;
    component['model'].update((value) => ({ ...value, special: 'sonarr:6' }));
    component['onSelectionChanged']();
    await fixture.whenStable();
    component['model'].update((value) => ({ ...value, special: 'sonarr:5' }));
    component['onSelectionChanged']();
    await fixture.whenStable();
    old.resolve(plan);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component['previewLoading']()).toBeTrue();
    expect(component['plan']()).toBeNull();
    expect(button('Save').disabled).toBeTrue();
    latest.resolve(plan);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(button('Save').disabled).toBeFalse();
  });

  it('locks both selections while the confirmed pair is being saved', async () => {
    await settlePreview();
    const saving = deferred<SymlinkLink>();
    service.saveLink.and.returnValue(saving.promise);
    button('Save').click();
    fixture.detectChanges();
    const selects = await TestbedHarnessEnvironment.loader(fixture).getAllHarnesses(MatSelectHarness);
    expect(await Promise.all(selects.map((select) => select.isDisabled()))).toEqual([true, true]);
    saving.resolve(draft);
    await fixture.whenStable();
    expect(closed).toHaveBeenCalledWith(draft);
  });

  it('groups movies and specials by instance without mixing equal item IDs', async () => {
    const component = fixture.componentInstance;
    component['choices'].update((choices) => ({
      movies: [
        ...choices.movies,
        { ...choices.movies[0], instanceId: 'radarr-2', instanceName: 'Other movies', title: 'Other Movie', path: '/other/movie.mkv' },
      ],
      specials: [...choices.specials, { ...choices.specials[0], instanceId: 'sonarr-2', instanceName: 'Other series' }],
    }));
    fixture.detectChanges();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    const movie = await loader.getHarness(MatSelectHarness.with({ selector: '#symlink-movie' }));
    await movie.open();
    const movieGroups = await movie.getOptionGroups();
    expect(await Promise.all(movieGroups.map((group) => group.getLabelText()))).toEqual(['Movies', 'Other movies']);
    await movie.clickOptions({ text: 'Other Movie' });
    expect(component['selectedMovie']()?.instanceId).toBe('radarr-2');
    expect((fixture.nativeElement as HTMLElement).querySelector('.selection')?.textContent).not.toContain('/other/movie.mkv');
    const special = await loader.getHarness(MatSelectHarness.with({ selector: '#symlink-special' }));
    await special.open();
    const specialGroups = await special.getOptionGroups();
    expect(await Promise.all(specialGroups.map((group) => group.getLabelText()))).toEqual(['Series', 'Other series']);
    const [group] = await special.getOptionGroups({ labelText: 'Other series' });
    const [option] = await group.getOptions();
    await option.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component['selectedSpecial']()?.instanceId).toBe('sonarr-2');
    await settlePreview();
    expect(service.previewLink).toHaveBeenCalledWith(
      jasmine.objectContaining({ radarrInstanceId: 'radarr-2', sonarrInstanceId: 'sonarr-2' }),
    );
  });

  for (const field of ['movie', 'special'] as const) {
    it(`clears the ${field} search with a Material button while retaining the chosen pair and preview`, async () => {
      await settlePreview();
      const element = fixture.nativeElement as HTMLElement;
      const input = element.querySelector(`#symlink-${field}-search`) as HTMLInputElement;
      input.value = 'no-match';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      await fixture.whenStable();
      const clear = input.parentElement!.querySelector('button.clear-search') as HTMLButtonElement;
      expect(clear.classList.contains('mat-mdc-icon-button')).toBeTrue();
      clear.click();
      fixture.detectChanges();
      await fixture.whenStable();
      expect(input.value).toBe('');
      expect(document.activeElement).toBe(input);
      expect(input.parentElement!.querySelector('button.clear-search')).toBeNull();
      expect(button('Save').disabled).toBeFalse();
    });
  }

  it('omits the provisional link path and its explanation from the preview', async () => {
    await settlePreview();
    const content = (fixture.nativeElement as HTMLElement).querySelector('.plan-grid')!;
    expect(content.textContent).toContain('/media/movie.mkv');
    expect(content.textContent).not.toContain('/media/series/special.mkv');
    expect(Array.from(content.querySelectorAll('dt'), (label) => label.textContent?.trim())).not.toContain('Link path');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Provisional name.');
  });

  it('saves the selected pair with the row revision and preview source version', async () => {
    await settlePreview();
    button('Save').click();
    await fixture.whenStable();
    expect(service.saveLink).toHaveBeenCalledWith(
      jasmine.objectContaining({ revision: 9, configRevision: 4, sourceVersion: 'v1' }),
      'link-1',
    );
    expect(closed).toHaveBeenCalledWith(draft);
  });

  it('keeps a rejected edit open and automatically refreshes its preview for another attempt', async () => {
    service.saveLink.and.rejectWith(new Error('The source changed after the preview.'));
    await settlePreview();
    button('Save').click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(closed).not.toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).querySelector('[role=alert]')?.textContent).toContain('source changed');
    expect(service.previewLink).toHaveBeenCalledTimes(2);
    expect(button('Save').disabled).toBeFalse();
  });
  it('starts with empty selections and shows paths only in the preview', async () => {
    const dialog = TestBed.inject(MatDialog).open(EditSymlinkDialogComponent, {
      width: '760px',
      maxWidth: '95vw',
      enterAnimationDuration: 0,
      exitAnimationDuration: 0,
      data: undefined,
    });
    try {
      await fixture.whenStable();
      fixture.detectChanges();
      await new Promise((resolve) => setTimeout(resolve, 0));
      expect(service.previewLink).toHaveBeenCalledTimes(1);
      const loader = TestbedHarnessEnvironment.documentRootLoader(fixture);
      const movie = await loader.getHarness(MatSelectHarness.with({ selector: '.cdk-overlay-pane #symlink-movie' }));
      const special = await loader.getHarness(MatSelectHarness.with({ selector: '.cdk-overlay-pane #symlink-special' }));
      expect(await movie.getValueText()).toBe('');
      expect(await special.getValueText()).toBe('');
      const overlay = TestBed.inject(OverlayContainer).getContainerElement();
      expect(overlay.querySelector('.selected-path')).toBeNull();
      await movie.open();
      const options = overlay.querySelector('.mat-mdc-select-panel')!;
      expect(options.textContent).not.toContain('/media/movie.mkv');
      expect(options.textContent).not.toContain('Select an entry');
      expect(options.querySelector('mat-pseudo-checkbox')).toBeNull();
      await movie.clickOptions({ text: /Movie/ });
      expect(overlay.querySelector('.selection')?.textContent).not.toContain('/media/movie.mkv');
      expect(service.previewLink).toHaveBeenCalledTimes(1);
      expect((overlay.querySelector('mat-dialog-actions button:last-child') as HTMLButtonElement).disabled).toBeTrue();
      await special.clickOptions({ text: /Special 5/ });
      await fixture.whenStable();
      fixture.detectChanges();
      expect(service.previewLink).toHaveBeenCalledTimes(2);
      expect((overlay.querySelector('mat-dialog-actions button:last-child') as HTMLButtonElement).disabled).toBeFalse();
      expect(
        Array.from(overlay.querySelectorAll('mat-dialog-actions button')).some((value) => value.textContent?.trim() === 'Check preview'),
      ).toBeFalse();
      expect(service.previewLink).toHaveBeenCalledWith({
        radarrInstanceId: 'radarr',
        radarrMovieId: 70,
        radarrFileId: 10,
        sonarrInstanceId: 'sonarr',
        sonarrSeriesId: 12,
        sonarrEpisodeId: 5,
      });
      fixture.detectChanges();
      expect(overlay.querySelector('.plan-grid')?.textContent).toContain('/media/movie.mkv');
    } finally {
      dialog.close();
    }
  });

  it('keeps long titles and paths inside a narrow dialog and its selection panel', async () => {
    const choices = await service.loadChoices();
    choices.movies[0] = {
      ...choices.movies[0],
      title: 'Movie title '.repeat(30),
      path: '/media/' + 'long-directory/'.repeat(50) + 'movie.mkv',
    };
    service.loadChoices.and.resolveTo(choices);
    const dialog = TestBed.inject(MatDialog).open(EditSymlinkDialogComponent, {
      width: '360px',
      maxWidth: '95vw',
      enterAnimationDuration: 0,
      exitAnimationDuration: 0,
      data: draft,
    });
    try {
      await fixture.whenStable();
      fixture.detectChanges();
      await new Promise((resolve) => setTimeout(resolve, 0));
      const overlay = TestBed.inject(OverlayContainer).getContainerElement();
      const content = overlay.querySelector('mat-dialog-content') as HTMLElement;
      expect(content.scrollWidth).toBeLessThanOrEqual(content.clientWidth + 1);
      const controls = Array.from(content.querySelectorAll<HTMLElement>('input, mat-select'));
      expect(controls.length).toBe(4);
      controls.forEach((control) =>
        expect(control.getBoundingClientRect().right).toBeLessThanOrEqual(content.getBoundingClientRect().right + 1),
      );
      const select = await TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(
        MatSelectHarness.with({ selector: '.cdk-overlay-pane #symlink-movie' }),
      );
      await select.open();
      const panel = overlay.querySelector('.mat-mdc-select-panel') as HTMLElement;
      expect(panel.scrollWidth).toBeLessThanOrEqual(panel.clientWidth + 1);
      await select.close();
    } finally {
      dialog.close();
    }
  });
});
