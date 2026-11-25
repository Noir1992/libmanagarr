import { SymlinkLiveService } from '../../services/symlink-live.service';
import type { MockInstance } from 'vitest';
import { MediaTitlesService } from '../../services/media-titles.service';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { LibrarySortDirection } from '../../enums/library-sort-direction';
import { LibrarySortField } from '../../enums/library-sort-field';
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
import { ExistingEpisodeFileCheck, SymlinkSuggestion } from '../../interfaces/symlinks/symlink-suggestion';
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
  | 'settingsError'
  | 'loadOverview'
  | 'loadSettings'
  | 'refreshProgress'
  | 'deletionRefreshPending'
  | 'rejectSuggestion'
  | 'retryLink'
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
    setupStep: 'Completed',
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
  };
  let dialog: Record<'open', MockInstance>;

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
    globalThis.localStorage.removeItem('sort:symlinks');
    globalThis.localStorage.removeItem('expanded:symlinks');
    stub = {
      settings: signal<SymlinkSettings | null>(createSettings()),
      suggestions: signal<SymlinkSuggestion[]>([]),
      links: signal<SymlinkLink[]>([]),
      rejections: signal<SymlinkRejection[]>([]),
      suggestionStates: signal<Record<string, SymlinkActionState>>({}),
      linkStates: signal<Record<string, SymlinkActionState>>({}),
      overviewLoading: signal(false),
      overviewError: signal<string | null>(null),
      settingsError: signal<string | null>(null),
      loadOverview: vi.fn().mockResolvedValue(undefined),
      loadSettings: vi.fn().mockResolvedValue(undefined),
      refreshProgress: vi.fn().mockResolvedValue(undefined),
      deletionRefreshPending: signal(false),
      rejectSuggestion: vi.fn().mockResolvedValue(createSuggestion({ status: 'Rejected' })),
      retryLink: vi.fn().mockResolvedValue(createLink()),
      checkLink: vi.fn().mockResolvedValue(createLink()),
      setFollowUp: vi.fn().mockImplementation(async (id: string, enabled: boolean, revision: number) => {
        const updated = createLink({ id, autoFollowUp: enabled, revision: revision + 1 });
        stub.links.update((links) => links.map((link) => (link.id === id ? updated : link)));
        return updated;
      }),
      deleteLink: vi.fn().mockResolvedValue(undefined),
      reactivateRejection: vi.fn().mockResolvedValue(undefined),
    };
    dialog = {
      open: vi.fn<MatDialog['open']>(),
    };

    await TestBed.configureTestingModule({
      imports: [SymlinksPage],
      providers: [
        { provide: MediaTitlesService, useValue: { revision: signal('en:0') } },
        provideRouter([]),
        { provide: SymlinkService, useValue: stub },
        {
          provide: SymlinkLiveService,
          useValue: { reconcile: vi.fn(), activatePage: vi.fn(), deactivatePage: vi.fn() },
        },
        { provide: MatDialog, useValue: dialog },
      ],
    }).compileComponents();

    TestBed.inject(AppI18nService).setLanguage('en');
    fixture = TestBed.createComponent(SymlinksPage);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle();
    // Existing row-action tests exercise the contents of all three sections.
    fixture.componentInstance['toggleSection']('links');
    fixture.componentInstance['toggleSection']('rejections');
    fixture.detectChanges();
  });

  afterEach(() => {
    globalThis.localStorage.removeItem('filters:symlinks');
    globalThis.localStorage.removeItem('sort:symlinks');
    globalThis.localStorage.removeItem('expanded:symlinks');
  });

  it('shows a warning next to the managed count and lists affected links on click, even while collapsed', async () => {
    stub.links.set([
      createLink({ radarrMovieTitle: 'Broken Movie' }),
      createLink({ id: 'second', radarrMovieTitle: 'Other Movie', episodeNumber: 7 }),
      createLink({ id: 'healthy', radarrMovieTitle: 'Healthy Movie', findings: [] }),
    ]);
    fixture.componentInstance['toggleSection']('links');
    fixture.detectChanges();

    expect(element.querySelectorAll('.card-header .managed-links-warning').length).toBe(1);
    const summary = cards()[1].querySelector('.managed-links-summary');
    expect(summary?.querySelector('.card-subtitle')?.textContent).toBe('(3)');
    expect(summary?.querySelector('.managed-links-warning')).not.toBeNull();
    const overlay = TestBed.inject(OverlayContainer).getContainerElement();
    const trigger = summary?.querySelector('button') as HTMLButtonElement;
    trigger.dispatchEvent(new MouseEvent('mouseenter'));
    fixture.detectChanges();
    expect(overlay.querySelector('.info-popover')).toBeNull();
    trigger.click();
    await settle();
    const text = overlay.querySelector('.info-popover')?.textContent ?? '';
    expect(text).toContain('Broken Movie — Special 4');
    expect(text).toContain('Other Movie — Special 7');
    expect(text).not.toContain('Healthy Movie');
    (overlay.querySelector('.cdk-overlay-backdrop') as HTMLElement).click();
    await settle();
    expect(overlay.querySelector('.info-popover')).toBeNull();

    stub.links.set([createLink({ findings: [] })]);
    fixture.detectChanges();
    expect(element.querySelector('.managed-links-warning')).toBeNull();
  });

  it('does not flag informational findings or hidden renaming notices in the managed header', () => {
    stub.links.set([
      createLink({
        findings: [
          { code: 'language-mapping-unavailable', detail: 'Telugu is not writable.', detectedAt: '2026-11-20T12:00:00Z' },
          { code: 'renaming-disabled', detail: 'Renaming disabled.', detectedAt: '2026-11-20T12:00:00Z' },
        ],
      }),
    ]);
    fixture.detectChanges();
    expect(element.querySelector('.managed-links-warning')).toBeNull();
  });

  it('offers only alphabetical and created sorting next to the filter, with the Library direction toggle', async () => {
    expect(element.querySelector('app-page-filter')?.nextElementSibling?.tagName.toLowerCase()).toBe('app-sort-control');
    const menu = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatMenuHarness);
    await menu.open();
    const items = await menu.getItems();
    expect(items.length).toBe(2);
    expect(await items[0].getText()).toContain('Alphabetical');
    expect(await items[0].getText()).toContain('arrow_upward');
    expect(await items[1].getText()).toContain('Created');
    expect(await items[1].getText()).not.toContain('arrow_');
    await items[1].click();
    await menu.open();
    await (await menu.getItems())[0].click();
    expect(fixture.componentInstance['sortField']()).toBe(LibrarySortField.ALPHABETICAL);
    expect(fixture.componentInstance['sortDirection']()).toBe(LibrarySortDirection.ASCENDING);
    await menu.open();
    await (await menu.getItems())[0].click();
    expect(fixture.componentInstance['sortDirection']()).toBe(LibrarySortDirection.DESCENDING);
    expect(globalThis.localStorage.getItem('sort:symlinks')).toBe('alphabetical:descending');
  });

  for (const [field, direction, expected] of [
    [LibrarySortField.ALPHABETICAL, LibrarySortDirection.ASCENDING, ['alpha', 'zulu']],
    [LibrarySortField.ALPHABETICAL, LibrarySortDirection.DESCENDING, ['zulu', 'alpha']],
    [LibrarySortField.ADDED, LibrarySortDirection.ASCENDING, ['zulu', 'alpha']],
    [LibrarySortField.ADDED, LibrarySortDirection.DESCENDING, ['alpha', 'zulu']],
  ] as const) {
    it(`sorts all lists by ${field} ${direction} independently of the server order`, () => {
      const rows = [
        { id: 'zulu', radarrMovieTitle: 'Zulu', createdAt: '2026-11-01T10:00:00Z' },
        { id: 'alpha', radarrMovieTitle: 'Alpha', createdAt: '2026-11-02T10:00:00Z' },
      ];
      stub.links.set(rows.map((row) => createLink(row)));
      stub.suggestions.set(rows.map((row) => createSuggestion(row)));
      stub.rejections.set(rows.map((row, index) => createRejection({ ...row, radarrMovieId: index })));
      const component = fixture.componentInstance;
      component['onSortFieldChanged'](field);
      component['onSortDirectionChanged'](direction);
      expect(component['links']().map((row) => row.id)).toEqual([...expected]);
      expect(component['offeredSuggestions']().map((row) => row.id)).toEqual([...expected]);
      expect(component['rejections']().map((row) => row.radarrMovieTitle?.toLowerCase())).toEqual([...expected]);
      stub.links.update((links) => [...links].reverse().map((link) => ({ ...link, revision: link.revision + 1 })));
      expect(component['links']().map((row) => row.id)).toEqual([...expected]);
    });
  }

  it('restores the chosen ordering after reopening the page', async () => {
    fixture.componentInstance['onSortFieldChanged'](LibrarySortField.ALPHABETICAL);
    fixture.componentInstance['onSortDirectionChanged'](LibrarySortDirection.DESCENDING);
    fixture.destroy();
    fixture = TestBed.createComponent(SymlinksPage);
    fixture.detectChanges();
    await settle();
    expect(fixture.componentInstance['sortField']()).toBe(LibrarySortField.ALPHABETICAL);
    expect(fixture.componentInstance['sortDirection']()).toBe(LibrarySortDirection.DESCENDING);
  });

  it('starts with hidden paths and uses the Library icon rotation for each link independently', async () => {
    stub.links.set([createLink({ id: 'first' }), createLink({ id: 'second' })]);
    fixture.detectChanges();
    const rows = Array.from(cards()[1].querySelectorAll<HTMLElement>('.item'));
    const toggle = rows[0].querySelector<HTMLButtonElement>('.link-details-toggle')!;
    const icon = toggle.querySelector('.collapse-icon')!;
    expect(toggle.classList.contains('mat-mdc-icon-button')).toBe(true);
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(icon.textContent?.trim()).toBe('expand_more');
    expect(icon.classList.contains('is-collapsed')).toBe(true);
    expect(rows[0].querySelector('.item-facts')).toBeNull();
    toggle.click();
    await settle();
    const details = rows[0].querySelector<HTMLElement>('.item-facts')!;
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(icon.classList.contains('is-collapsed')).toBe(false);
    expect(toggle.getAttribute('aria-controls')).toBe(details.id);
    expect(getComputedStyle(details).display).toBe('grid');
    expect(rows[1].querySelector('.item-facts')).toBeNull();
    stub.links.update((links) => links.map((link) => ({ ...link, revision: link.revision + 1 })));
    fixture.detectChanges();
    expect(rows[0].querySelector('.item-facts')).toBe(details);
    toggle.click();
    await settle();
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(icon.classList.contains('is-collapsed')).toBe(true);
  });

  it('defaults to only Suggestions open and alphabetical ascending, and saves those defaults', async () => {
    fixture.destroy();
    globalThis.localStorage.removeItem('expanded:symlinks');
    globalThis.localStorage.removeItem('sort:symlinks');
    fixture = TestBed.createComponent(SymlinksPage);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle();
    expect(cards()[0].querySelector('.card-content')).not.toBeNull();
    expect(cards()[1].querySelector('.card-content')).toBeNull();
    expect(cards()[2].querySelector('.card-content')).toBeNull();
    expect(globalThis.localStorage.getItem('sort:symlinks')).toBe('alphabetical:ascending');
    expect(JSON.parse(globalThis.localStorage.getItem('expanded:symlinks')!)).toEqual({
      suggestions: true,
      links: false,
      rejections: false,
    });
  });

  it('remembers independently toggled sections across page visits', async () => {
    const toggle = cards()[0].querySelector<HTMLButtonElement>('.section-toggle')!;
    toggle.click();
    await settle();
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(toggle.querySelector('.collapse-icon')?.classList.contains('is-collapsed')).toBe(true);
    fixture.destroy();
    fixture = TestBed.createComponent(SymlinksPage);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle();
    expect(cards()[0].querySelector('.card-content')).toBeNull();
    expect(cards()[1].querySelector('.card-content')).not.toBeNull();
    expect(cards()[2].querySelector('.card-content')).not.toBeNull();
  });

  it('ignores malformed stored section state and restores the defaults', async () => {
    fixture.destroy();
    globalThis.localStorage.setItem('expanded:symlinks', '{broken');
    fixture = TestBed.createComponent(SymlinksPage);
    fixture.detectChanges();
    await settle();
    expect(fixture.componentInstance['expandedSections']()).toEqual({ suggestions: true, links: false, rejections: false });
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
    expect(rowText(1)).not.toContain('The Special Movie');

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
    expect(rowText(1)).not.toContain('Another Movie');
    (element.querySelector('.symlink-search app-search-clear button') as HTMLButtonElement).click();
    await settle();
    expect(search.value).toBe('');
    expect(document.activeElement).toBe(search);
    expect(element.querySelector('.symlink-search app-search-clear')).toBeNull();
    expect(cards()[1].querySelectorAll('.item').length).toBe(2);
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
    expect(rowText(1)).not.toContain('The Special Movie');

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
      expect.objectContaining({ status: '', instance: '' }),
    );
  });

  it('migrates a stored status filter the update removed instead of hiding every offer', async () => {
    fixture.destroy();
    globalThis.localStorage.setItem('filters:symlinks', JSON.stringify({ status: 'WaitingForFile' }));
    fixture = TestBed.createComponent(SymlinksPage);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle();

    stub.suggestions.set([createSuggestion()]);
    fixture.detectChanges();

    // The retired status is no longer a choice; the page falls back to showing all offers and writes
    // the repaired value back so nothing has to be repaired again on the next visit.
    expect(rowText(0)).toContain('The Special Movie');
    expect(element.querySelectorAll('.filter-bubble-remove').length).toBe(0);
    expect(JSON.parse(globalThis.localStorage.getItem('filters:symlinks') ?? '{}')).toEqual(expect.objectContaining({ status: '' }));
  });

  it('registers for initial loading through the shared connection', () => {
    expect(TestBed.inject(SymlinkLiveService).activatePage).toHaveBeenCalledTimes(1);
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
      createSuggestion({ id: 'stale', status: 'Stale' }),
      createSuggestion({ id: 'conflict', status: 'Conflict' }),
      createSuggestion({ id: 'failed', status: 'Failed' }),
    ]);
    fixture.detectChanges();
    const states = cards()[0].querySelectorAll('.status-pill');
    expect(states.length).toBe(3);
    expect(Array.from(states, (state) => state.textContent?.trim())).toEqual([
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
    expect(info.wide()).toBe(true);
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
    expect(panel.classList.contains('info-popover--wide')).toBe(true);
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
    expect(note.classList.contains('is-success')).toBe(true);
    expect(note.classList.contains('is-warning')).toBe(false);
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
      expect(note.classList.contains('is-warning')).toBe(true);
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

  it('puts the current configuration revision into the confirmation', () => {
    stub.suggestions.set([createSuggestion()]);
    fixture.detectChanges();

    actionButtons(0)[0].click();

    expect(dialog.open).toHaveBeenCalledWith(
      ConfirmSymlinkDialogComponent,
      expect.objectContaining({ data: { suggestion: stub.suggestions()[0], configRevision: 7 } }),
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
    dialog.open.mockReturnValue({
      afterClosed: () => of('The film is a different cut.'),
    } as unknown as MatDialogRef<RejectSymlinkDialogComponent>);
    stub.suggestions.set([createSuggestion()]);
    fixture.detectChanges();

    actionButtons(0)[1].click();
    await settle();

    expect(dialog.open).toHaveBeenCalledWith(
      RejectSymlinkDialogComponent,
      expect.objectContaining({ data: { suggestion: stub.suggestions()[0] } }),
    );
    expect(stub.rejectSuggestion).toHaveBeenCalledWith('suggestion-1', 'The film is a different cut.');
  });

  it('leaves a decline without a reason bare instead of storing spaces', async () => {
    dialog.open.mockReturnValue({ afterClosed: () => of('   ') } as unknown as MatDialogRef<RejectSymlinkDialogComponent>);
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
    expect(text).not.toContain('/data/series/the-series/season-00/s00e04.mkv');
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
    expect(actionButtons(1).some((button) => button.textContent?.includes('Retry setup'))).toBe(false);
    expect(
      actionButtons(1)
        .find((button) => button.textContent?.trim() === 'Edit')
        ?.classList.contains('secondary-button'),
    ).toBe(true);
  });

  for (const [step, progress] of [
    ['LinkCreated', 'Creating the symlink'],
    ['MonitoringDisabled', 'Preparing Sonarr'],
    ['RescanRequested', 'Sonarr is rescanning the series'],
    ['EpisodeAssigned', 'Checking the episode file assignment'],
    ['MetadataMatched', 'Matching file information'],
    ['Renamed', 'Confirming the filename and library'],
  ] as const) {
    for (const status of ['Pending', 'Active'] as const) {
      it(`shows progress and locks editing and deletion during ${step} even with status ${status}`, async () => {
        const link = createLink({ operationStatus: status, setupStep: step, findings: [], canEdit: true });
        stub.links.set([link]);
        fixture.detectChanges();
        expect(cards()[1].querySelector('.item .status-pill')?.textContent).toContain('Setting up');
        expect(cards()[1].querySelector('.item .status-pill')?.classList.contains('is-success')).toBe(false);
        expect(cards()[1].querySelector('.setup-progress')?.textContent).toContain(progress);
        expect(cards()[1].querySelector('.setup-progress .feedback-icon.spinning')).not.toBeNull();
        expect(actionButtons(1).find((button) => button.textContent?.trim() === 'Edit')?.disabled).toBe(true);
        expect(actionButtons(1).find((button) => button.textContent?.trim() === 'Delete')?.disabled).toBe(true);
        fixture.componentInstance['edit'](link);
        await fixture.componentInstance['deleteAsync'](link);
        expect(dialog.open).not.toHaveBeenCalled();
        expect(stub.deleteLink).not.toHaveBeenCalled();

        stub.links.set([{ ...link, operationStatus: 'Active', setupStep: 'Completed' }]);
        fixture.detectChanges();
        expect(cards()[1].querySelector('.item .status-pill')?.textContent).toContain('Active');
        expect(cards()[1].querySelector('.setup-progress')).toBeNull();
        expect(actionButtons(1).find((button) => button.textContent?.trim() === 'Edit')?.disabled).toBe(false);
        expect(actionButtons(1).find((button) => button.textContent?.trim() === 'Delete')?.disabled).toBe(false);
      });
    }
  }

  it('allows repair but keeps editing and deletion disabled for an incomplete managed link', async () => {
    stub.links.set([createLink({ operationStatus: 'Conflict', setupStep: 'MetadataMatched', canEdit: true })]);
    fixture.detectChanges();
    const buttons = actionButtons(1);
    const repair = buttons.find((button) => button.textContent?.trim() === 'Repair');
    expect(repair?.disabled).toBe(false);
    expect(buttons.find((button) => button.textContent?.trim() === 'Edit')?.disabled).toBe(true);
    expect(buttons.find((button) => button.textContent?.trim() === 'Delete')?.disabled).toBe(true);

    repair!.click();
    await settle();

    expect(stub.retryLink).toHaveBeenCalledWith('link-1', 3);
  });

  for (const fails of [false, true]) {
    it(`shows Repair feedback and prevents duplicate requests until ${fails ? 'failure' : 'success'}`, async () => {
      const link = createLink({ operationStatus: 'Conflict', setupStep: 'MetadataMatched', canEdit: true });
      stub.links.set([link]);
      let resolve: ((value: SymlinkLink) => void) | undefined;
      let reject: ((error: Error) => void) | undefined;
      vi.mocked(stub.retryLink).mockReturnValue(
        new Promise<SymlinkLink>((done, refused) => {
          resolve = done;
          reject = refused;
        }),
      );
      fixture.detectChanges();
      const button = actionButtons(1).find((candidate) => candidate.textContent?.trim() === 'Repair')!;
      button.click();
      fixture.detectChanges();
      expect(button.disabled).toBe(true);
      expect(button.textContent).toContain('Repair');
      expect(button.querySelector('.feedback-icon.spinning')).not.toBeNull();
      expect(actionButtons(1).every((candidate) => candidate.disabled)).toBe(true);
      await fixture.componentInstance['repairAsync'](link);
      expect(stub.retryLink).toHaveBeenCalledTimes(1);

      if (fails) reject?.(new Error('Repair was refused.'));
      else resolve?.(link);
      await settle();
      expect(button.disabled).toBe(false);
      expect(button.querySelector('.feedback-icon.spinning')).toBeNull();
    });
  }

  it('keeps Repair visible and spinning after acceptance while the background worker is running', async () => {
    stub.links.set([createLink({ findings: [], setupStep: 'RescanRequested' })]);
    stub.linkStates.set({ 'link-1': { busy: true, repairing: true } });
    fixture.detectChanges();
    const button = actionButtons(1).find((candidate) => candidate.textContent?.includes('Repair'))!;
    expect(button).toBeDefined();
    expect(button.disabled).toBe(true);
    expect(button.querySelector('.feedback-icon.spinning')).not.toBeNull();
    await fixture.componentInstance['repairAsync'](createLink());
    expect(stub.retryLink).not.toHaveBeenCalled();

    stub.linkStates.set({ 'link-1': { busy: false } });
    stub.links.set([createLink({ findings: [], setupStep: 'Completed' })]);
    fixture.detectChanges();
    expect(actionButtons(1).some((candidate) => candidate.textContent?.includes('Repair'))).toBe(false);
  });

  it('retains a locked deletion and swaps it for its suggestion after an event refresh completes', async () => {
    fixture.destroy();
    vi.spyOn(globalThis.document, 'hidden', 'get').mockReturnValue(false);
    const intervals = vi.spyOn(globalThis, 'setInterval');
    vi.mocked(stub.refreshProgress).mockImplementation(async () => {
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
    expect(actionButtons(1).every((button) => button.disabled)).toBe(true);
    expect(cards()[1].querySelector('.danger-button .feedback-icon.spinning')).not.toBeNull();

    expect(intervals.mock.calls.some((args) => args[1] === 5000)).toBe(false);
    await stub.refreshProgress();
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
      expect(Array.from(row.querySelectorAll<HTMLButtonElement>('.item-actions button')).every((button) => button.disabled)).toBe(true);
      expect(row.getAttribute('aria-busy')).toBe('true');
      expect(row.querySelector('.danger-button .feedback-icon.spinning')).not.toBeNull();
      expect(cards()[0].querySelector('.item')).toBeNull();
      const confirm = vi.spyOn(globalThis, 'confirm');
      const component = fixture.componentInstance;
      await component['deleteAsync'](stub.links()[0]);
      component['edit'](stub.links()[0]);
      expect(stub.checkLink).not.toHaveBeenCalled();
      expect(stub.setFollowUp).not.toHaveBeenCalled();
      expect(stub.deleteLink).not.toHaveBeenCalled();
      expect(dialog.open).not.toHaveBeenCalled();
      expect(confirm).not.toHaveBeenCalled();
      // Completing the HTTP request alone does not unlock an unfinished deletion.
      stub.linkStates.set({ 'link-1': { deleting: true, busy: false } });
      fixture.detectChanges();
      expect(Array.from(row.querySelectorAll<HTMLButtonElement>('.item-actions button')).every((button) => button.disabled)).toBe(true);
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
    expect(actionButtons(1).every((button) => button.disabled)).toBe(true);
  });

  it('omits the manual check and check timestamps even when the task has checked the row', () => {
    stub.links.set([createLink({ lastCheckedAt: '2026-11-05T18:30:00.000Z', lastSuccessfulCheckAt: '2026-11-01T09:00:00.000Z' })]);
    fixture.detectChanges();

    expect(actionButtons(1).some((button) => button.textContent?.trim() === 'Check')).toBe(false);
    expect(rowText(1)).not.toContain('Last checked');
    expect(rowText(1)).not.toContain('Nothing to report since');
  });

  it('has no source-upgrade switch, including for legacy entries', () => {
    stub.links.set([createLink({ autoFollowUp: false })]);
    fixture.detectChanges();
    expect(cards()[1].querySelector('mat-slide-toggle')).toBeNull();
    expect(stub.setFollowUp).not.toHaveBeenCalled();
  });

  for (const state of [
    { operationStatus: 'Active' as const, setupStep: 'Completed' as const },
    { operationStatus: 'Active' as const, setupStep: 'RescanRequested' as const },
    { operationStatus: 'Updating' as const, setupStep: 'LinkCreated' as const },
  ]) {
    it(`hides Repair during normal ${state.operationStatus}/${state.setupStep} progress`, () => {
      stub.links.set([createLink({ ...state, findings: [] })]);
      fixture.detectChanges();
      expect(actionButtons(1).some((button) => button.textContent?.trim() === 'Repair')).toBe(false);
    });
  }

  for (const hasError of [false, true]) {
    it(`shows unsupported languages as information ${hasError ? 'alongside an actual repairable issue' : 'without offering Repair'}`, () => {
      const information = {
        code: 'language-mapping-unavailable',
        detail: 'Sonarr cannot represent Telugu. Other metadata was transferred.',
        detectedAt: '2026-11-05T18:30:00Z',
      };
      stub.links.set([
        createLink({
          operationStatus: 'Active',
          setupStep: 'Completed',
          findings: hasError
            ? [information, { code: 'link-lost', detail: 'Missing link', detectedAt: information.detectedAt }]
            : [information],
        }),
      ]);
      fixture.detectChanges();

      const info = fixture.debugElement.query(By.css('.managed-heading app-info-icon')).componentInstance as InfoIconWidget;
      expect(info.messageText()).toBe(information.detail);
      const special = cards()[1].querySelector('.item-title .managed-special');
      expect(special?.textContent).toContain('Special 4');
      expect(special?.querySelector('app-info-icon')).not.toBeNull();
      expect(cards()[1].querySelectorAll('.item-warning').length).toBe(hasError ? 1 : 0);
      expect(cards()[1].querySelector('.item-warning')?.textContent ?? '').not.toContain(information.code);
      expect(actionButtons(1).some((button) => button.textContent?.trim() === 'Repair')).toBe(hasError);
    });
  }

  it('keeps Repair available for a conflict even when its only finding is informational', () => {
    stub.links.set([
      createLink({
        operationStatus: 'Conflict',
        findings: [{ code: 'language-mapping-unavailable', detail: 'Sonarr cannot represent Telugu.', detectedAt: '' }],
      }),
    ]);
    fixture.detectChanges();
    expect(actionButtons(1).some((button) => button.textContent?.trim() === 'Repair')).toBe(true);
  });

  it('offers Repair after a missing-link finding', () => {
    stub.links.set([createLink({ findings: [{ code: 'link-lost', detail: 'Missing link', detectedAt: '2026-11-05T18:30:00Z' }] })]);
    fixture.detectChanges();
    expect(actionButtons(1).some((button) => button.textContent?.trim() === 'Repair')).toBe(true);
  });

  it('only deletes a link after the question was answered', async () => {
    stub.links.set([createLink()]);
    fixture.detectChanges();
    const question: MockInstance = vi.spyOn(globalThis, 'confirm').mockReturnValue(false);

    actionButtons(1)
      .find((button) => button.textContent?.includes('Delete'))!
      .click();
    await settle();
    expect(stub.deleteLink).not.toHaveBeenCalled();
    expect(question.mock.lastCall![0]).toContain('Only the symlink file will be deleted.');
    expect(question.mock.lastCall![0]).toContain('original movie file');

    question.mockReturnValue(true);
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

  for (const fails of [false, true]) {
    it(`shows feedback and prevents duplicate release requests until ${fails ? 'failure' : 'success'}`, async () => {
      const rejection = createRejection();
      stub.rejections.set([rejection]);
      let resolve: (() => void) | undefined;
      let reject: ((error: Error) => void) | undefined;
      vi.mocked(stub.reactivateRejection).mockReturnValue(
        new Promise<void>((done, refused) => {
          resolve = done;
          reject = refused;
        }),
      );
      fixture.detectChanges();
      const button = actionButtons(2)[0];
      button.click();
      fixture.detectChanges();
      expect(button.disabled).toBe(true);
      expect(button.textContent).toContain('Release again');
      expect(button.querySelector('.feedback-icon.spinning')).not.toBeNull();
      await fixture.componentInstance['reactivateAsync'](rejection);
      expect(stub.reactivateRejection).toHaveBeenCalledTimes(1);

      if (fails) reject?.(new Error('The archive is busy.'));
      else resolve?.();
      await settle();
      expect(button.disabled).toBe(false);
      expect(button.querySelector('.feedback-icon.spinning')).toBeNull();
      expect(stub.overviewError()).toBe(fails ? 'The archive is busy.' : null);
    });
  }

  it('reports a decline that could not be lifted where the list sits', async () => {
    stub.rejections.set([createRejection()]);
    fixture.detectChanges();
    vi.mocked(stub.reactivateRejection).mockRejectedValue(new Error('The archive is busy.'));

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
    expect(settingsLink.classList.contains('mat-mdc-outlined-button')).toBe(true);
    stub.settings.set(createSettings({ enabled: true }));
    fixture.detectChanges();
    expect(element.querySelector('.list-controls')).not.toBeNull();
    expect(cards().length).toBe(3);
  });
});
