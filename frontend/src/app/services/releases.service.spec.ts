import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AppI18nService } from './app-i18n.service';
import { ReleasesService } from './releases.service';
import { ReleaseOverview } from '../interfaces/releases/release-overview';

describe('ReleasesService', () => {
  let service: ReleasesService;
  let httpTestingController: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ReleasesService,
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        { provide: AppI18nService, useValue: { t: (key: string): string => key } },
      ],
    });

    service = TestBed.inject(ReleasesService);
    httpTestingController = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTestingController.verify();
  });

  it('reads the release list from the release endpoint', async () => {
    const load: Promise<void> = service.loadAsync(false);

    const request: TestRequest = httpTestingController.expectOne('/api/releases/');
    expect(request.request.method).toBe('GET');
    request.flush(overview('Ok', ['1.4.0', '1.3.0'], false));

    await load;

    expect(service.versions().map((version) => version.version)).toEqual(['1.4.0', '1.3.0']);
    expect(service.currentVersion()).toBe('1.3.0');
    expect(service.updateAvailable()).toBeFalse();
    expect(service.loadError()).toBeNull();
  });

  it('asks the backend to re-read the release list on a refresh', async () => {
    const load: Promise<void> = service.loadAsync(true);

    const request: TestRequest = httpTestingController.expectOne('/api/releases/refresh');
    expect(request.request.method).toBe('POST');
    request.flush(overview('Ok', ['1.5.0'], true));

    await load;

    expect(service.updateAvailable()).toBeTrue();
  });

  it('names the failure without dropping the state of a failed read', async () => {
    const first: Promise<void> = service.loadAsync(false);
    httpTestingController.expectOne('/api/releases/').flush(overview('Ok', ['1.3.0'], false));
    await first;

    const second: Promise<void> = service.loadAsync(true);
    httpTestingController.expectOne('/api/releases/refresh').flush({}, { status: 503, statusText: 'Unavailable' });
    await second;

    expect(service.loadError()).toBe('releases.unreachable');
    expect(service.loading()).toBeFalse();
  });

  it('probes once per session while a refresh stays explicit', async () => {
    const firstProbe: Promise<void> = service.loadOnceAsync();
    httpTestingController.expectOne('/api/releases/').flush(overview('Ok', ['1.3.0'], false));
    await firstProbe;

    await service.loadOnceAsync();

    expect(service.currentVersion()).toBe('1.3.0');
  });

  it('keeps the navigation usable while no answer arrives', async () => {
    const probe: Promise<void> = service.loadOnceAsync();

    httpTestingController.expectOne('/api/releases/');

    expect(service.loading()).toBeTrue();
    expect(service.overview()).toBeNull();
    expect(service.updateAvailable()).toBeFalse();

    httpTestingController.verify();
    void probe;
  });

  function overview(status: ReleaseOverview['status'], versions: readonly string[], updateAvailable: boolean): ReleaseOverview {
    return {
      currentVersion: '1.3.0',
      updateAvailable,
      status,
      checkedAt: '2026-11-02T10:00:00.000Z',
      versions: versions.map((version): ReleaseOverview['versions'][number] => ({
        version,
        name: `Release ${version}`,
        tag: `v${version}`,
        publishedAt: '2026-11-01T10:00:00.000Z',
        notes: 'Notes',
        url: `https://github.com/Noir1992/libmanagarr/releases/tag/v${version}`,
        isPrerelease: false,
        isCurrent: version === '1.3.0',
        isNewerThanCurrent: updateAvailable && version !== '1.3.0',
        pullReference: `ghcr.io/noir1992/libmanagarr:${version}`,
      })),
    };
  }
});
