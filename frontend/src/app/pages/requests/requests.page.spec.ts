import { signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SeerrRequest } from '../../interfaces/requests/seerr-request';
import { RequestsService } from '../../services/requests.service';
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
>;

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
  };

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
    };

    await TestBed.configureTestingModule({
      imports: [RequestsPage],
      providers: [provideRouter([]), { provide: RequestsService, useValue: stub }],
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
    expect(firstRow.querySelector('.requests-item-type')?.textContent?.trim()).toBe('Movie');
    expect(firstRow.querySelector('.requests-status-badge')?.textContent?.trim()).toBe('Pending');
    expect(firstRow.querySelector('.requests-item-requester')?.textContent).toContain('alice');
    expect(firstRow.querySelector('img.requests-poster-image')?.getAttribute('src')).toBe(
      'https://seerr.example/posters/sample.jpg',
    );

    const secondRow: Element = rows[1];
    expect(secondRow.querySelector('.requests-item-title')?.textContent?.trim()).toBe('Sample Series');
    expect(secondRow.querySelector('.requests-item-type')?.textContent?.trim()).toBe('Series');
    expect(secondRow.querySelector('.requests-status-badge')?.getAttribute('data-status')).toBe('approved');
    expect(secondRow.querySelector('.requests-item-4k-badge')).not.toBeNull();
    // Seerr-relative poster paths cannot be rendered and fall back to the type icon.
    expect(secondRow.querySelector('img.requests-poster-image')).toBeNull();
    expect(secondRow.querySelector('.requests-poster mat-icon')).not.toBeNull();
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
});
