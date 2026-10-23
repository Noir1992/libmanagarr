import { CdkVirtualScrollViewport } from '@angular/cdk/scrolling';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal, WritableSignal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { MultiSelectFilterOption } from '../../interfaces/filter-option';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { LogsPage } from './logs.page';

describe('LogsPage', () => {
  let fixture: ComponentFixture<LogsPage>;
  let httpTestingController: HttpTestingController;
  let appSettingsService: { loadError: WritableSignal<string | null>; localeId: WritableSignal<string> };

  beforeEach(async () => {
    appSettingsService = {
      loadError: signal<string | null>(null),
      localeId: signal<string>('en-US'),
    };

    await TestBed.configureTestingModule({
      imports: [LogsPage],
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {
          provide: AppI18nService,
          useValue: {
            t: (key: string): string =>
              ({
                'settings.logs.title': 'Logs',
                'settings.logs.description': 'Logs description',
                'filter.title': 'Filter',
                'settings.logs.filter.levels': 'Log levels',
                'settings.logs.filter.range': 'Date range',
                'settings.logs.loading': 'Loading logs...',
                'settings.logs.empty': 'No logs found.',
                'settings.logs.column.timestamp': 'Timestamp',
                'settings.logs.column.level': 'Level',
                'settings.logs.column.source': 'Source',
                'settings.logs.column.message': 'Message',
                'settings.logs.loadError': 'Could not load logs.',
                'common.loading.error': 'Loading error',
                'common.noData': 'No logs found.',
                'common.noDataForFilter': 'No logs found for the selected filter.',
              })[key] ?? key,
          },
        },
        {
          provide: AppSettingsService,
          useValue: appSettingsService,
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LogsPage);
    httpTestingController = TestBed.inject(HttpTestingController);
  });

  function getViewport(): CdkVirtualScrollViewport {
    const viewport = fixture.debugElement.query(By.directive(CdkVirtualScrollViewport));

    expect(viewport).withContext('virtual scroll viewport should exist').toBeTruthy();

    return viewport!.componentInstance as CdkVirtualScrollViewport;
  }

  afterEach(() => {
    httpTestingController.verify();
  });

  it('renders the logs page and loads entries', async () => {
    fixture.detectChanges();

    const logsRequest: TestRequest = httpTestingController.expectOne((request) => {
      return request.url === '/api/logs/' && request.params.has('startDateTime') && request.params.has('endDateTime');
    });

    expect(logsRequest.request.params.getAll('levels')).toEqual(['Debug', 'Information', 'Warning', 'Error']);

    logsRequest.flush([
      {
        timestamp: '2026-05-26T08:15:00Z',
        level: 'Information',
        source: 'Cleanup Task',
        message: 'Cleanup completed.',
      },
    ]);

    await fixture.whenStable();
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    const viewport: CdkVirtualScrollViewport = getViewport();
    viewport.checkViewportSize();
    viewport.scrollToIndex(0);
    await fixture.whenStable();
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;

    expect(compiled.textContent).toContain('Logs');
    expect(compiled.textContent).toContain('Logs description');
    expect(fixture.componentInstance['logsState']()).toHaveSize(1);
    expect(fixture.componentInstance['logsState']()[0].message).toBe('Cleanup completed.');
    expect(compiled.querySelector('table')).not.toBeNull();
    expect(fixture.componentInstance['filters']()[1].persist).toBeFalse();
  });

  it('shows a no data section when no log entries are returned', async () => {
    fixture.detectChanges();

    const initialRequest: TestRequest = httpTestingController.expectOne((request) => request.url === '/api/logs/');
    initialRequest.flush([]);

    await fixture.whenStable();
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.no-data-state')?.textContent).toContain('No logs found.');
    expect(compiled.querySelector('table')).toBeNull();
  });

  it('refreshes logs when the level filter changes', async () => {
    fixture.detectChanges();

    const initialRequest: TestRequest = httpTestingController.expectOne((request) => request.url === '/api/logs/');
    initialRequest.flush([]);
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    const levelFilter: MultiSelectFilterOption = fixture.componentInstance['filters']()[0] as MultiSelectFilterOption;
    levelFilter.onChanged(['Error']);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const nextRequest: TestRequest = httpTestingController.expectOne((request) => {
      return request.url === '/api/logs/' && request.params.getAll('levels')?.length === 1;
    });
    expect(nextRequest.request.params.getAll('levels')).toEqual(['Error']);
    nextRequest.flush([]);
  });

  it('loads all log levels when the level filter is cleared completely', async () => {
    fixture.detectChanges();

    const initialRequest: TestRequest = httpTestingController.expectOne((request) => request.url === '/api/logs/');
    initialRequest.flush([]);
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    const levelFilter: MultiSelectFilterOption = fixture.componentInstance['filters']()[0] as MultiSelectFilterOption;
    levelFilter.onChanged(['Error']);
    fixture.detectChanges();
    await fixture.whenStable();
    httpTestingController.expectOne((request) => request.url === '/api/logs/').flush([]);
    levelFilter.onChanged([]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const nextRequest: TestRequest = httpTestingController.expectOne((request) => {
      return request.url === '/api/logs/' && request.params.getAll('levels')?.length === 4;
    });

    expect(nextRequest.request.params.getAll('levels')).toEqual(['Debug', 'Information', 'Warning', 'Error']);
    nextRequest.flush([]);

    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance['logsState']()).toHaveSize(0);
    expect(fixture.componentInstance['isFiltered']()).toBeFalse();
    expect(fixture.nativeElement.querySelector('.no-data-state')?.textContent).toContain('No logs found.');
  });

  it('shows a settings load error when settings failed to load', async () => {
    appSettingsService.loadError.set('Settings could not be loaded.');

    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    httpTestingController.expectNone((request) => request.url === '/api/logs/');

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.loading-error-state-message')?.textContent).toContain('Settings could not be loaded.');
    expect(compiled.querySelector('app-page-filter')).toBeNull();
  });

  it('shows a loading error when the logs request fails', async () => {
    fixture.detectChanges();

    const logsRequest: TestRequest = httpTestingController.expectOne((request) => request.url === '/api/logs/');
    logsRequest.error(new ErrorEvent('error', { message: 'Could not load logs.' }));

    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.loading-error-state-message')?.textContent).toContain('Could not load logs.');
    expect(compiled.querySelector('app-page-filter')).not.toBeNull();
  });
});
