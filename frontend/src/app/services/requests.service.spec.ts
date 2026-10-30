import { HttpErrorResponse, provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RequestsService } from './requests.service';

describe('RequestsService', () => {
  let service: RequestsService;
  let httpMock: HttpTestingController;

  const pagedResponse = {
    requests: [
      {
        id: '3',
        status: 'approved',
        is4kRequest: true,
        type: 'movie',
        requestedBy: 'alice',
        requestedAt: '2026-10-01T12:30:00.000Z',
        title: 'Sample Movie',
        posterPath: '/posters/sample.jpg',
        year: 2011,
      },
    ],
    total: 12,
    page: 2,
    limit: 10,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withXhr()), provideHttpClientTesting()],
    });

    service = TestBed.inject(RequestsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads the first page with sorting and no status filter', async () => {
    const promise: Promise<void> = service.loadAsync();

    const request = httpMock.expectOne((req) => req.url === '/api/requests/');
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('limit')).toBe('20');
    expect(request.request.params.get('sortField')).toBe('createdAt');
    expect(request.request.params.get('sortOrder')).toBe('desc');
    expect(request.request.params.has('status')).toBeFalse();

    request.flush(pagedResponse);
    await promise;

    expect(service.requests()).toHaveSize(1);
    expect(service.total()).toBe(12);
    expect(service.page()).toBe(2);
    expect(service.limit()).toBe(10);
    expect(service.loading()).toBeFalse();
    expect(service.notConfigured()).toBeFalse();
    expect(service.loadError()).toBeNull();
  });

  it('resets to page one and sends the status when the filter changes', async () => {
    const firstPagePromise: Promise<void> = service.setPageAsync(3);
    const firstPageRequest = httpMock.expectOne((req) => req.url === '/api/requests/');
    expect(firstPageRequest.request.params.get('page')).toBe('3');
    firstPageRequest.flush({ requests: [], total: 0, page: 3, limit: 20 });
    await firstPagePromise;

    const filterPromise: Promise<void> = service.setStatusFilterAsync('approved');
    const filterRequest = httpMock.expectOne((req) => req.url === '/api/requests/');
    expect(filterRequest.request.params.get('page')).toBe('1');
    expect(filterRequest.request.params.get('status')).toBe('approved');
    filterRequest.flush({ requests: [], total: 0, page: 1, limit: 20 });
    await filterPromise;

    expect(service.statusFilter()).toBe('approved');
    expect(service.page()).toBe(1);
  });

  it('marks Seerr as not configured for the seerr-not-configured problem code', async () => {
    const promise: Promise<void> = service.loadAsync();

    const request = httpMock.expectOne((req) => req.url === '/api/requests/');
    request.flush(
      { title: 'Seerr is not configured.', status: 503, code: 'seerr-not-configured' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    await promise;

    expect(service.notConfigured()).toBeTrue();
    expect(service.loadError()).toBeNull();
    expect(service.requests()).toEqual([]);
  });

  it('treats any service-unavailable answer as Seerr not being configured', async () => {
    const promise: Promise<void> = service.loadAsync();

    const request = httpMock.expectOne((req) => req.url === '/api/requests/');
    request.flush('', { status: 503, statusText: 'Service Unavailable' });
    await promise;

    expect(service.notConfigured()).toBeTrue();
    expect(service.loadError()).toBeNull();
  });

  it('surfaces the problem title as load error for gateway failures', async () => {
    const promise: Promise<void> = service.loadAsync();

    const request = httpMock.expectOne((req) => req.url === '/api/requests/');
    request.flush({ title: 'Seerr is not reachable.' }, { status: 502, statusText: 'Bad Gateway' });
    await promise;

    expect(service.notConfigured()).toBeFalse();
    expect(service.loadError()).toBe('Seerr is not reachable.');
    expect(service.requests()).toEqual([]);
  });

  it('falls back to the localized load error when the problem body has no message', async () => {
    const promise: Promise<void> = service.loadAsync();

    const request = httpMock.expectOne((req) => req.url === '/api/requests/');
    request.flush('', { status: 500, statusText: 'Server Error' });
    await promise;

    expect(service.notConfigured()).toBeFalse();
    expect(service.loadError()).toBe('Requests could not be loaded.');
  });

  it('ignores page numbers below one', async () => {
    await service.setPageAsync(0);

    httpMock.expectNone((req) => req.url === '/api/requests/');
    expect(service.page()).toBe(1);
  });

  it('approves through the approve route and resolves on the no-content answer', async () => {
    const promise: Promise<void> = service.approveAsync(12);

    const request = httpMock.expectOne('/api/requests/12/approve');
    expect(request.request.method).toBe('POST');
    request.flush(null, { status: 204, statusText: 'No Content' });

    await expectAsync(promise).toBeResolved();
  });

  it('declines with the trimmed reason and omits the body without one', async () => {
    const withReason: Promise<void> = service.declineAsync(12, '  not now  ');
    const reasonRequest = httpMock.expectOne('/api/requests/12/decline');
    expect(reasonRequest.request.method).toBe('POST');
    expect(reasonRequest.request.body).toEqual({ reason: 'not now' });
    reasonRequest.flush(null, { status: 204, statusText: 'No Content' });
    await withReason;

    const withoutReason: Promise<void> = service.declineAsync(12, null);
    const emptyRequest = httpMock.expectOne('/api/requests/12/decline');
    expect(emptyRequest.request.body).toBeNull();
    emptyRequest.flush(null, { status: 204, statusText: 'No Content' });
    await withoutReason;
  });

  it('deletes on the request resource with the delete verb', async () => {
    const promise: Promise<void> = service.deleteAsync(42);

    const request = httpMock.expectOne('/api/requests/42');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });

    await expectAsync(promise).toBeResolved();
  });

  it('updates with only the edited fields', async () => {
    const promise: Promise<void> = service.updateAsync(12, { profileId: 16 });

    const request = httpMock.expectOne('/api/requests/12');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ profileId: 16 });
    request.flush(null, { status: 204, statusText: 'No Content' });

    await expectAsync(promise).toBeResolved();
  });

  it('loads the edit options for one request', async () => {
    const options = {
      qualityProfiles: [{ id: 15, name: 'Standard' }],
      rootFolders: ['/media/videos/movies', '/media/videos/movies (anime)'],
      seasons: [],
      profileId: 15,
      rootFolder: '/media/videos/movies',
    };

    const promise: Promise<unknown> = service.loadEditOptionsAsync(12);
    const request = httpMock.expectOne('/api/requests/12/edit-options');
    expect(request.request.method).toBe('GET');
    request.flush(options);

    await expectAsync(promise).toBeResolvedTo(options);
  });

  it('rejects write operations so callers can describe the problem', async () => {
    const promise: Promise<void> = service.approveAsync(999999999);

    const request = httpMock.expectOne('/api/requests/999999999/approve');
    request.flush({ detail: 'Request not found.' }, { status: 404, statusText: 'Not Found' });

    await expectAsync(promise).toBeRejected();
  });

  it('describes action errors from the problem detail, the title or the localized fallback', () => {
    expect(service.describeActionError(new HttpErrorResponse({ status: 404, error: { detail: 'Request not found.' } }))).toBe(
      'Request not found.',
    );
    expect(service.describeActionError(new HttpErrorResponse({ status: 502, error: { title: 'Seerr is not reachable.' } }))).toBe(
      'Seerr is not reachable.',
    );
    expect(service.describeActionError(new HttpErrorResponse({ status: 500 }))).toBe('The request could not be updated.');
  });
});
