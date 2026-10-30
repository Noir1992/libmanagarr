import { HttpErrorResponse } from '@angular/common/http';
import { signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { SeerrRequest } from '../../interfaces/requests/seerr-request';
import { SeerrRequestEditOptions } from '../../interfaces/requests/seerr-request-edit-options';
import { SeerrRequestUpdate } from '../../interfaces/requests/seerr-request-update';
import { RequestsService } from '../../services/requests.service';
import { RequestDeclineDialogWidget } from '../../widgets/request-decline-dialog/request-decline-dialog.widget';
import { RequestDeleteDialogWidget } from '../../widgets/request-delete-dialog/request-delete-dialog.widget';
import { RequestEditDialogWidget } from '../../widgets/request-edit-dialog/request-edit-dialog.widget';
import { RequestsPage } from './requests.page';

type RequestsServiceStub = Pick<
  RequestsService,
  | 'requests'
  | 'total'
  | 'page'
  | 'limit'
  | 'statusFilter'
  | 'loading'
  | 'loadError'
  | 'notConfigured'
  | 'loadAsync'
  | 'setStatusFilterAsync'
  | 'setPageAsync'
  | 'approveAsync'
  | 'declineAsync'
  | 'deleteAsync'
  | 'updateAsync'
  | 'loadEditOptionsAsync'
  | 'describeActionError'
>;

const movieOptions: SeerrRequestEditOptions = {
  qualityProfiles: [{ id: 15, name: 'Standard' }],
  rootFolders: ['/media/videos/movies'],
  seasons: [],
  profileId: 15,
  rootFolder: '/media/videos/movies',
};

function createRequest(overrides: Partial<SeerrRequest> = {}): SeerrRequest {
  return {
    id: '1',
    status: 'pending',
    is4kRequest: false,
    type: 'movie',
    requestedBy: 'alice',
    requestedAt: '2026-10-01T12:30:00.000Z',
    title: 'Sample Movie',
    posterPath: 'https://seerr.example/posters/sample.jpg',
    year: 2011,
    seasons: null,
    ...overrides,
  };
}

describe('RequestsPage', () => {
  let fixture: ComponentFixture<RequestsPage>;
  let stub: RequestsServiceStub & {
    requests: WritableSignal<readonly SeerrRequest[]>;
    total: WritableSignal<number>;
    page: WritableSignal<number>;
    limit: WritableSignal<number>;
    statusFilter: WritableSignal<string | null>;
    loading: WritableSignal<boolean>;
    loadError: WritableSignal<string | null>;
    notConfigured: WritableSignal<boolean>;
    approveAsync: jasmine.Spy<(requestId: number) => Promise<void>>;
    loadEditOptionsAsync: jasmine.Spy<(requestId: number) => Promise<SeerrRequestEditOptions>>;
    describeActionError: jasmine.Spy<(error: unknown) => string>;
  };
  let dialog: jasmine.SpyObj<MatDialog>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;

  const toastData = (): { type: string; message: string } => {
    const config: { data: { type: string; message: string } } = snackBar.openFromComponent.calls.mostRecent().args[1] as {
      data: { type: string; message: string };
    };
    return config.data;
  };

  // The overflow trigger is css-hidden on wide viewports, so the tests open it through the dom.
  async function openOverflowMenu(rowIndex: number): Promise<void> {
    const triggers: NodeListOf<HTMLButtonElement> = (fixture.nativeElement as HTMLElement).querySelectorAll(
      '.requests-more-trigger',
    );
    triggers[rowIndex].click();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function overflowItemTexts(): string[] {
    return Array.from(document.querySelectorAll<HTMLElement>('.mat-mdc-menu-panel .mat-mdc-menu-item')).map(
      (item: HTMLElement): string => item.textContent?.replace(/\s+/g, ' ').trim() ?? '',
    );
  }

  beforeEach(async () => {
    stub = {
      requests: signal<readonly SeerrRequest[]>([]),
      total: signal<number>(0),
      page: signal<number>(1),
      limit: signal<number>(20),
      statusFilter: signal<string | null>(null),
      loading: signal<boolean>(false),
      loadError: signal<string | null>(null),
      notConfigured: signal<boolean>(false),
      loadAsync: jasmine.createSpy('loadAsync').and.resolveTo(),
      setStatusFilterAsync: jasmine.createSpy('setStatusFilterAsync').and.resolveTo(),
      setPageAsync: jasmine.createSpy('setPageAsync').and.resolveTo(),
      approveAsync: jasmine.createSpy('approveAsync').and.resolveTo(),
      declineAsync: jasmine.createSpy('declineAsync').and.resolveTo(),
      deleteAsync: jasmine.createSpy('deleteAsync').and.resolveTo(),
      updateAsync: jasmine.createSpy('updateAsync').and.resolveTo(),
      loadEditOptionsAsync: jasmine
        .createSpy('loadEditOptionsAsync')
        .and.resolveTo({
          qualityProfiles: [],
          rootFolders: [],
          seasons: [],
          profileId: null,
          rootFolder: null,
        } satisfies SeerrRequestEditOptions),
      describeActionError: jasmine.createSpy('describeActionError').and.returnValue('The request could not be updated.'),
    };

    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    dialog.open.and.returnValue({ afterClosed: () => of(undefined) } as never);
    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['openFromComponent']);

    await TestBed.configureTestingModule({
      imports: [RequestsPage],
      providers: [
        provideRouter([]),
        { provide: RequestsService, useValue: stub },
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RequestsPage);
  });

  it('loads the first page on init', () => {
    fixture.detectChanges();

    expect(stub.loadAsync).toHaveBeenCalled();
  });

  it('renders request rows with title, type, status, requester and absolute poster urls', () => {
    stub.requests.set([
      createRequest(),
      createRequest({
        id: '2',
        status: 'approved',
        is4kRequest: true,
        type: 'tv',
        title: 'Sample Series',
        posterPath: '/covers/relative.jpg',
        year: null,
      }),
    ]);
    fixture.detectChanges();

    const rows: NodeListOf<Element> = (fixture.nativeElement as HTMLElement).querySelectorAll('.requests-list-item');
    expect(rows).toHaveSize(2);

    const firstRow: Element = rows[0];
    expect(firstRow.querySelector('.requests-item-title')?.textContent?.trim()).toBe('Sample Movie');
    expect(firstRow.querySelector('.requests-item-year')?.textContent?.trim()).toBe('(2011)');
    expect(firstRow.querySelector('.requests-item-title-row .requests-item-type')?.textContent?.trim()).toBe('Movie');
    expect(firstRow.querySelector('.requests-item-title-row .requests-item-type')?.getAttribute('data-type')).toBe('movie');
    expect(firstRow.querySelector('.requests-status-badge')?.textContent?.trim()).toBe('Pending');
    expect(firstRow.querySelector('.requests-item-requester')?.textContent).toContain('alice');
    expect(firstRow.querySelector('.requests-item-date')?.textContent?.trim()).toContain('Requested:');
    expect(firstRow.querySelector('img.requests-poster-image')?.getAttribute('src')).toBe(
      'https://seerr.example/posters/sample.jpg',
    );

    const secondRow: Element = rows[1];
    expect(secondRow.querySelector('.requests-item-title')?.textContent?.trim()).toBe('Sample Series');
    expect(secondRow.querySelector('.requests-item-title-row .requests-item-type')?.textContent?.trim()).toBe('Series');
    expect(secondRow.querySelector('.requests-item-title-row .requests-item-type')?.getAttribute('data-type')).toBe('tv');
    expect(secondRow.querySelector('.requests-status-badge')?.getAttribute('data-status')).toBe('approved');
    expect(secondRow.querySelector('.requests-item-4k-badge')).not.toBeNull();
    // Seerr-relative poster paths cannot be rendered and fall back to the type icon.
    expect(secondRow.querySelector('img.requests-poster-image')).toBeNull();
    expect(secondRow.querySelector('.requests-poster mat-icon')).not.toBeNull();
  });

  it('renders a bubble per requested season and labels season 0 as specials', () => {
    stub.requests.set([
      createRequest({ id: '2', type: 'tv', title: 'Sample Series', seasons: [0, 1, 2] }),
      createRequest({ id: '3', type: 'tv', title: 'Other Series', seasons: [3] }),
      createRequest(),
    ]);
    fixture.detectChanges();

    const rows: NodeListOf<Element> = (fixture.nativeElement as HTMLElement).querySelectorAll('.requests-list-item');

    expect(rows[0].querySelector('.requests-item-seasons-label')?.textContent?.trim()).toBe('Seasons:');
    const seasons: NodeListOf<Element> = rows[0].querySelectorAll('.requests-item-season');
    expect(Array.from(seasons).map((season: Element): string => season.textContent?.trim() ?? '')).toEqual([
      'Specials',
      '1',
      '2',
    ]);
    expect(rows[1].querySelector('.requests-item-season')?.textContent?.trim()).toBe('3');
    // Movie requests carry no seasons and must not render the row at all.
    expect(rows[2].querySelector('.requests-item-seasons')).toBeNull();
  });

  it('renders the status between the seasons and the requester with its own label', () => {
    stub.requests.set([createRequest({ type: 'tv', seasons: [1, 2] })]);
    fixture.detectChanges();

    const summary: Element | null = (fixture.nativeElement as HTMLElement).querySelector('.requests-item-summary');
    const children: string[] = Array.from(summary?.children ?? []).map((child: Element): string => child.className);
    expect(children).toEqual(['requests-item-seasons', 'requests-item-status', 'requests-item-meta', 'requests-item-date']);
    expect(summary?.querySelector('.requests-item-status-label')?.textContent?.trim()).toBe('Status:');
    expect(summary?.querySelector('.requests-item-status .requests-status-badge')).not.toBeNull();
  });

  it('renders the poster from this app cache endpoint paths', () => {
    stub.requests.set([createRequest({ posterPath: '/api/requests/movie/poster/383498' })]);
    fixture.detectChanges();

    const img: Element | null = (fixture.nativeElement as HTMLElement).querySelector('img.requests-poster-image');
    expect(img?.getAttribute('src')).toBe('/api/requests/movie/poster/383498');
  });

  it('shows the loading section while requests are loading', () => {
    stub.loading.set(true);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('app-loading-section')).not.toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('.requests-list')).toBeNull();
  });

  it('shows the load error section with the service error message', () => {
    stub.loadError.set('Seerr is not reachable.');
    fixture.detectChanges();

    const errorSection: Element | null = (fixture.nativeElement as HTMLElement).querySelector('app-loading-error-section');
    expect(errorSection).not.toBeNull();
    expect(fixture.componentInstance['requestsService'].loadError()).toBe('Seerr is not reachable.');
  });

  it('shows the not-configured hint instead of the list', () => {
    stub.notConfigured.set(true);
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.requests-not-configured-state')).not.toBeNull();
    expect(element.querySelector('.requests-filter-bar')).toBeNull();
    expect(element.querySelector('.requests-list')).toBeNull();
  });

  it('shows the empty state filtered when a status filter is active', () => {
    stub.statusFilter.set('pending');
    fixture.detectChanges();

    const noDataSection: Element | null = (fixture.nativeElement as HTMLElement).querySelector('app-no-data-section');
    expect(noDataSection).not.toBeNull();
    expect(fixture.componentInstance['isFiltered']()).toBeTrue();
  });

  it('applies a status filter when a chip is clicked', () => {
    fixture.detectChanges();

    const chips: NodeListOf<HTMLButtonElement> = (fixture.nativeElement as HTMLElement).querySelectorAll('.requests-filter-chip');
    expect(chips).toHaveSize(6);

    chips[1].click();
    fixture.detectChanges();

    expect(stub.setStatusFilterAsync).toHaveBeenCalledWith('pending');
    expect(chips[0].classList.contains('is-active')).toBeTrue();
  });

  it('does not reload when the active chip is clicked again', () => {
    stub.statusFilter.set('approved');
    fixture.detectChanges();

    const chips: NodeListOf<HTMLButtonElement> = (fixture.nativeElement as HTMLElement).querySelectorAll('.requests-filter-chip');
    chips[2].click();

    expect(stub.setStatusFilterAsync).not.toHaveBeenCalled();
    expect(chips[2].classList.contains('is-active')).toBeTrue();
  });

  it('pages forward and backward through the results', () => {
    stub.requests.set([createRequest()]);
    stub.total.set(60);
    stub.page.set(2);
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement as HTMLElement;
    const previousButton: HTMLButtonElement = element.querySelector('.requests-pagination-button:not(:last-child)')!;
    const nextButton: HTMLButtonElement = element.querySelectorAll('.requests-pagination-button')[1] as HTMLButtonElement;

    expect(previousButton.disabled).toBeFalse();
    expect(nextButton.disabled).toBeFalse();
    expect(element.querySelector('.requests-pagination-info')?.textContent?.trim()).toBe('Page 2 of 3');

    nextButton.click();
    expect(stub.setPageAsync).toHaveBeenCalledWith(3);

    previousButton.click();
    expect(stub.setPageAsync).toHaveBeenCalledWith(1);
  });

  it('disables pagination buttons at the first and last page', () => {
    stub.requests.set([createRequest()]);
    stub.total.set(20);
    stub.page.set(1);
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement as HTMLElement;
    const buttons: NodeListOf<HTMLButtonElement> = element.querySelectorAll('.requests-pagination-button');
    expect(buttons[0].disabled).toBeTrue();
    expect(buttons[1].disabled).toBeTrue();
    expect(element.querySelector('.requests-pagination-info')?.textContent?.trim()).toBe('Page 1 of 1');
  });

  it('shows management actions by status and type while delete stays available', () => {
    stub.requests.set([
      createRequest(),
      createRequest({ id: '2', status: 'approved', type: 'tv' }),
      createRequest({ id: '3', status: 'declined' }),
      createRequest({ id: '4', status: 'unknown', type: null }),
    ]);
    fixture.detectChanges();

    const rows: NodeListOf<Element> = (fixture.nativeElement as HTMLElement).querySelectorAll('.requests-list-item');

    expect(rows[0].querySelector('.requests-action-approve')).not.toBeNull();
    expect(rows[0].querySelector('.requests-action-decline')).not.toBeNull();
    expect(rows[0].querySelector('.requests-action-edit')).not.toBeNull();
    expect(rows[0].querySelector('.requests-action-delete')).not.toBeNull();

    expect(rows[1].querySelector('.requests-action-approve')).toBeNull();
    expect(rows[1].querySelector('.requests-action-decline')).toBeNull();
    // Approved rows are configured already, so editing is pending-only.
    expect(rows[1].querySelector('.requests-action-edit')).toBeNull();
    expect(rows[1].querySelector('.requests-action-delete')).not.toBeNull();

    expect(rows[2].querySelector('.requests-action-approve')).toBeNull();
    expect(rows[2].querySelector('.requests-action-edit')).toBeNull();
    expect(rows[2].querySelector('.requests-action-delete')).not.toBeNull();

    expect(rows[3].querySelectorAll('.requests-action-button')).toHaveSize(1);
  });

  it('mirrors the inline actions in the overflow menu', async () => {
    stub.requests.set([createRequest()]);
    fixture.detectChanges();

    await openOverflowMenu(0);

    const texts: string[] = overflowItemTexts();
    expect(texts).toHaveSize(4);
    expect(texts[0]).toContain('Approve');
    expect(texts[1]).toContain('Decline');
    expect(texts[2]).toContain('Edit');
    expect(texts[3]).toContain('Delete');
  });

  it('lists only the delete action in the overflow menu of a declined request', async () => {
    stub.requests.set([createRequest({ id: '3', status: 'declined' })]);
    fixture.detectChanges();

    await openOverflowMenu(0);

    const texts: string[] = overflowItemTexts();
    expect(texts).toHaveSize(1);
    expect(texts[0]).toContain('Delete');
  });

  it('runs the approve action picked from the overflow menu', async () => {
    stub.requests.set([createRequest()]);
    fixture.detectChanges();

    await openOverflowMenu(0);
    const items: NodeListOf<HTMLElement> = document.querySelectorAll('.mat-mdc-menu-panel .mat-mdc-menu-item');
    items[0].click();

    expect(stub.approveAsync).toHaveBeenCalledOnceWith(1);
  });

  it('approves the request, reloads and shows a success toast', async () => {
    const request: SeerrRequest = createRequest();
    stub.requests.set([request]);
    fixture.detectChanges();

    await fixture.componentInstance['approveAsync'](request);

    expect(stub.approveAsync).toHaveBeenCalledOnceWith(1);
    expect(stub.loadAsync).toHaveBeenCalledTimes(2);
    expect(toastData()).toEqual({ type: 'success', message: 'Request approved.' });
  });

  it('reports the described problem and skips the reload when the approval fails', async () => {
    const request: SeerrRequest = createRequest();
    stub.approveAsync.and.rejectWith(new HttpErrorResponse({ status: 404, error: { detail: 'Request not found.' } }));
    stub.describeActionError.and.returnValue('Request not found.');
    stub.requests.set([request]);
    fixture.detectChanges();

    await fixture.componentInstance['approveAsync'](request);

    expect(stub.loadAsync).toHaveBeenCalledTimes(1);
    expect(toastData()).toEqual({ type: 'error', message: 'Request not found.' });
  });

  it('declines with the reason from the dialog', async () => {
    const request: SeerrRequest = createRequest();
    dialog.open.and.returnValue({ afterClosed: () => of('not now') } as never);

    await fixture.componentInstance['declineWithDialogAsync'](request);

    expect(dialog.open).toHaveBeenCalledWith(RequestDeclineDialogWidget, jasmine.objectContaining({ data: { title: 'Sample Movie' } }));
    expect(stub.declineAsync).toHaveBeenCalledOnceWith(1, 'not now');
    expect(toastData()).toEqual({ type: 'success', message: 'Request declined.' });
  });

  it('does nothing when the decline dialog is cancelled', async () => {
    await fixture.componentInstance['declineWithDialogAsync'](createRequest());

    expect(stub.declineAsync).not.toHaveBeenCalled();
    expect(snackBar.openFromComponent).not.toHaveBeenCalled();
  });

  it('deletes after the confirmation dialog resolves with true', async () => {
    const request: SeerrRequest = createRequest();
    dialog.open.and.returnValue({ afterClosed: () => of(true) } as never);

    await fixture.componentInstance['deleteWithDialogAsync'](request);

    expect(dialog.open).toHaveBeenCalledWith(RequestDeleteDialogWidget, jasmine.objectContaining({ data: { title: 'Sample Movie' } }));
    expect(stub.deleteAsync).toHaveBeenCalledOnceWith(1);
    expect(toastData()).toEqual({ type: 'success', message: 'Request deleted.' });
  });

  it('keeps the request when the delete dialog is cancelled', async () => {
    dialog.open.and.returnValue({ afterClosed: () => of(false) } as never);

    await fixture.componentInstance['deleteWithDialogAsync'](createRequest());

    expect(stub.deleteAsync).not.toHaveBeenCalled();
  });

  it('saves the freshly loaded edits and approves the request right away', async () => {
    const request: SeerrRequest = createRequest();
    const update: SeerrRequestUpdate = { profileId: 16, rootFolder: '/media/videos/movies' };
    stub.loadEditOptionsAsync.and.resolveTo(movieOptions);
    dialog.open.and.returnValue({ afterClosed: () => of(update) } as never);

    await fixture.componentInstance['editWithDialogAsync'](request);

    expect(stub.loadEditOptionsAsync).toHaveBeenCalledOnceWith(1);
    expect(dialog.open).toHaveBeenCalledWith(
      RequestEditDialogWidget,
      jasmine.objectContaining({ data: { title: 'Sample Movie', options: movieOptions } }),
    );
    expect(stub.updateAsync).toHaveBeenCalledOnceWith(1, update);
    expect(stub.approveAsync).toHaveBeenCalledOnceWith(1);
    expect(toastData()).toEqual({ type: 'success', message: 'Request approved.' });
  });

  it('shows a toast and skips the dialog when the edit options cannot be loaded', async () => {
    stub.loadEditOptionsAsync.and.rejectWith(new HttpErrorResponse({ status: 502, error: { title: 'Seerr is not reachable.' } }));
    stub.describeActionError.and.returnValue('Seerr is not reachable.');

    await fixture.componentInstance['editWithDialogAsync'](createRequest());

    expect(dialog.open).not.toHaveBeenCalled();
    expect(stub.updateAsync).not.toHaveBeenCalled();
    expect(toastData()).toEqual({ type: 'error', message: 'Seerr is not reachable.' });
  });

  it('ignores further actions while one action runs', async () => {
    const request: SeerrRequest = createRequest();
    let release: () => void = (): void => undefined;
    stub.approveAsync.and.callFake(
      () =>
        new Promise<void>((resolve: () => void): void => {
          release = resolve;
        }),
    );

    const first: Promise<void> = fixture.componentInstance['approveAsync'](request);
    const second: Promise<void> = fixture.componentInstance['approveAsync'](createRequest({ id: '2' }));
    await second;

    expect(stub.approveAsync).toHaveBeenCalledTimes(1);

    release();
    await first;
    expect(stub.approveAsync).toHaveBeenCalledTimes(1);
  });
});
