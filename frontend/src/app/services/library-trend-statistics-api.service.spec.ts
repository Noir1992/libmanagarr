import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LibraryTrendStatisticsApiService } from './library-trend-statistics-api.service';

describe('LibraryTrendStatisticsApiService', () => {
  let service: LibraryTrendStatisticsApiService;
  let httpTestingController: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [LibraryTrendStatisticsApiService, provideHttpClient(withXhr()), provideHttpClientTesting()],
    });

    service = TestBed.inject(LibraryTrendStatisticsApiService);
    httpTestingController = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTestingController.verify();
  });

  it('should request library trend statistics with filters and date range', () => {
    service
      .fetchLibraryTrendStatistics(['s1', 's2'], ['r1'], '2026-05-01T00:00:00.000Z', '2026-05-31T23:59:59.000Z')
      .subscribe((response) => {
        expect(response.points).toHaveSize(1);
      });

    const request: TestRequest = httpTestingController.expectOne(
      '/api/statistics/library-trend?sonarrInstanceIds=s1,s2&radarrInstanceIds=r1&start=2026-05-01T00:00:00.000Z&end=2026-05-31T23:59:59.000Z',
    );
    expect(request.request.params.get('sonarrInstanceIds')).toBe('s1,s2');
    expect(request.request.params.get('radarrInstanceIds')).toBe('r1');
    expect(request.request.params.get('start')).toBe('2026-05-01T00:00:00.000Z');
    expect(request.request.params.get('end')).toBe('2026-05-31T23:59:59.000Z');

    request.flush({
      availableFrom: '2026-05-01T10:00:00.000Z',
      availableTo: '2026-05-31T23:59:59.000Z',
      totalMediaFileCount: 1,
      totalSizeBytes: 100,
      points: [
        {
          occurredAt: '2026-05-01T10:00:00.000Z',
          mediaFileCount: 1,
          totalSizeBytes: 100,
          deltaMediaFileCount: 1,
          deltaSizeBytes: 100,
        },
      ],
      history: [
        {
          sourceKey: 'radarr:1',
          kind: 'Radarr',
          instanceId: 'r1',
          instanceName: 'Radarr Main',
          occurredAt: '2026-05-01T10:00:00.000Z',
          changeType: 'Added',
          entityTitle: 'Dune',
          seriesTitle: null,
          filePath: '/media/dune.mkv',
          sizeBytes: 100,
          deltaSizeBytes: 100,
        },
      ],
    });
  });
});
