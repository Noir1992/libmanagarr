import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CodecStatisticsApiService } from './codec-statistics-api.service';

describe('CodecStatisticsApiService', () => {
  let service: CodecStatisticsApiService;
  let httpTestingController: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [CodecStatisticsApiService, provideHttpClient(withXhr()), provideHttpClientTesting()],
    });

    service = TestBed.inject(CodecStatisticsApiService);
    httpTestingController = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTestingController.verify();
  });

  it('should request codec statistics with selected instance filters', () => {
    service
      .fetchCodecStatistics(['s1', 's2'], ['r1'], { videoCodecs: ['HEVC'], excludedFilters: ['video-codec-filter'] })
      .subscribe((response) => {
        expect(response.videoCodecs).toHaveSize(1);
      });

    const request: TestRequest = httpTestingController.expectOne('/api/statistics/codecs');
    expect(request.request.method).toBe('POST');

    expect(request.request.body).toEqual({
      sonarrInstanceFilter: ['s1', 's2'],
      radarrInstanceFilter: ['r1'],
      videoCodecs: ['HEVC'],
      excludedFilters: ['video-codec-filter'],
    });
    request.flush({
      containerFormats: [],
      resolutions: [],
      dynamicRanges: [],
      videoCodecs: [{ label: 'x265', count: 1, percentage: 100, mediaFiles: [], totalSizeBytes: 0 }],
      audioCodecs: [],
      videoBitrates: [],
      audioLanguages: [],
      subtitleLanguages: [],
    });
  });

  it('should send empty filter params when nothing is selected', () => {
    service.fetchCodecStatistics([], []).subscribe();

    const request: TestRequest = httpTestingController.expectOne('/api/statistics/codecs');
    expect(request.request.method).toBe('POST');
    request.flush({
      containerFormats: [],
      resolutions: [],
      dynamicRanges: [],
      videoCodecs: [],
      audioCodecs: [],
      videoBitrates: [],
      audioLanguages: [],
      subtitleLanguages: [],
    });
  });
  it('hydrates compact chart memberships using shared file metadata', () => {
    const file = {
      title: 'Movie',
      source: 'Radarr',
      instanceName: 'Radarr',
      rootTitle: 'Movie',
      sizeBytes: 123,
      relativePath: 'Movie/file.mkv',
      titleSlug: 'movie',
    };
    const item = { label: 'Value', count: 1, percentage: 100, totalSizeBytes: 123, mediaFiles: [], fileIds: [0] };
    service.fetchCodecStatistics([], []).subscribe((response) => {
      for (const category of [
        response.containerFormats,
        response.resolutions,
        response.dynamicRanges,
        response.videoCodecs,
        response.audioCodecs,
        response.videoBitrates,
        response.audioLanguages,
        response.subtitleLanguages,
      ]) {
        expect(category[0].mediaFiles).toEqual([file]);
        expect(category[0].mediaFiles[0]).toBe(response.files![0]);
        expect(category[0].count).toBe(1);
        expect(category[0].totalSizeBytes).toBe(123);
      }
    });
    httpTestingController.expectOne('/api/statistics/codecs').flush({
      files: [file],
      containerFormats: [item],
      resolutions: [item],
      dynamicRanges: [item],
      videoCodecs: [item],
      audioCodecs: [item],
      videoBitrates: [item],
      audioLanguages: [item],
      subtitleLanguages: [item],
    });
  });
});
