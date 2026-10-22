import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LogEntry } from '../interfaces/log-entry';
import { AppLogsService } from './app-logs.service';

describe('AppLogsService', () => {
  let service: AppLogsService;
  let http: HttpTestingController;
  const start = '2026-05-01T00:00:00+02:00';
  const end = '2026-05-02T23:59:00+02:00';

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AppLogsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends exact date boundaries and repeated level parameters and returns entries', async () => {
    const pending = service.loadLogs(start, end, ['Warning', 'Error']);
    const request = http.expectOne((req) => req.url === '/api/logs/');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('startDateTime')).toBe(start);
    expect(request.request.params.get('endDateTime')).toBe(end);
    expect(request.request.params.getAll('levels')).toEqual(['Warning', 'Error']);
    const entries: LogEntry[] = [{ timestamp: start, level: 'Error', source: 'Sync', message: 'Failed' }];
    request.flush(entries);
    expect(await pending).toEqual(entries);
  });

  it('sends an explicit empty level selection instead of omitting the parameter', async () => {
    const pending = service.loadLogs(start, end, []);
    const request = http.expectOne((req) => req.url === '/api/logs/');
    expect(request.request.params.has('levels')).toBeTrue();
    expect(request.request.params.getAll('levels')).toEqual(['']);
    request.flush([]);
    expect(await pending).toEqual([]);
  });

  it('propagates HTTP failures to the caller', async () => {
    const pending = service.loadLogs(start, end, ['Error']);
    const assertion = expectAsync(pending).toBeRejectedWith(jasmine.any(HttpErrorResponse));
    http.expectOne((req) => req.url === '/api/logs/').flush({ detail: 'Cannot read logs' }, { status: 500, statusText: 'Error' });
    await assertion;
  });
});
