import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';
import { GeneralSettingsBackend } from '../../interfaces/settings/general-settings-backend';
import { InstancesSettingsBackend } from '../../interfaces/settings/instances-settings-backend';
import { LoggingSettings } from '../../interfaces/settings/logging-settings';
import { ScheduledTasks } from '../../interfaces/settings/scheduled-tasks';
import { AppSettingsService } from '../../services/app-settings.service';
import { SettingsPage } from './settings.page';

function createScheduledTasksResponse(): ScheduledTasks {
  return [
    {
      taskKey: 'arrDataSync',
      revision: 0,
      canRunManually: true,
      enabled: false,
      readOnly: false,
      scheduleMode: 'Interval',
      intervalUnit: 'Hour',
      intervalValue: 1,
      timeHour: 0,
      timeMinute: 0,
      everyNDays: 1,
      lastRunAt: null,
      nextRunAt: null,
    },
    {
      taskKey: 'cleanup',
      revision: 0,
      canRunManually: false,
      enabled: true,
      readOnly: true,
      scheduleMode: 'DailyAtTime',
      intervalUnit: 'Hour',
      intervalValue: 1,
      timeHour: 0,
      timeMinute: 0,
      everyNDays: 1,
      lastRunAt: null,
      nextRunAt: null,
    },
  ];
}

function createLoggingResponse(): LoggingSettings {
  return {
    path: './logs',
    retentionDays: 30,
    minimumLevel: 'Information',
  };
}

function createGeneralSettingsResponse(): GeneralSettingsBackend {
  return {
    language: 'En',
    theme: 'System',
  };
}

function createInstancesResponse(): InstancesSettingsBackend {
  return {
    sonarrInstances: [
      {
        id: 'sonarr-main',
        name: 'Sonarr Main',
        baseUrl: 'http://localhost:8989',
        apiKey: 'secret',
      },
    ],
    radarrInstances: [
      {
        id: 'radarr-main',
        name: 'Radarr Main',
        baseUrl: 'http://localhost:7878',
        apiKey: 'secret',
      },
    ],
  };
}

describe('SettingsPage', () => {
  let fixture: ComponentFixture<SettingsPage>;
  let httpTestingController: HttpTestingController;
  let activatedRouteStub: ActivatedRouteStub;

  beforeEach(async () => {
    activatedRouteStub = new ActivatedRouteStub({ tab: 'general' });
    await TestBed.configureTestingModule({
      imports: [SettingsPage],
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: activatedRouteStub },
        {
          provide: MatDialog,
          useValue: {
            open: jasmine.createSpy('open').and.returnValue({
              afterClosed: () => of(true),
            }),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SettingsPage);
    httpTestingController = TestBed.inject(HttpTestingController);
  });

  class ActivatedRouteStub {
    private readonly queryParamMapSubject: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
    private currentQueryParams: Record<string, string>;

    constructor(initialQueryParams: Record<string, string>) {
      this.currentQueryParams = initialQueryParams;
      this.queryParamMapSubject = new BehaviorSubject(convertToParamMap(this.currentQueryParams));
    }

    get snapshot(): ActivatedRoute['snapshot'] {
      return {
        queryParamMap: convertToParamMap(this.currentQueryParams),
      } as ActivatedRoute['snapshot'];
    }

    get queryParamMap(): BehaviorSubject<ReturnType<typeof convertToParamMap>> {
      return this.queryParamMapSubject;
    }

    setQueryParams(nextQueryParams: Record<string, string>): void {
      this.currentQueryParams = nextQueryParams;
      this.queryParamMapSubject.next(convertToParamMap(this.currentQueryParams));
    }
  }

  afterEach(() => {
    httpTestingController.verify();
  });

  it('should load settings into the page state', async () => {
    fixture.detectChanges();

    httpTestingController.expectOne('/api/settings/general').flush(createGeneralSettingsResponse());
    httpTestingController.expectOne('/api/settings/logging').flush(createLoggingResponse());

    await fixture.componentInstance['initializePageAsync']();
    fixture.detectChanges();

    const appSettingsService: AppSettingsService = TestBed.inject(AppSettingsService);
    expect(appSettingsService.language()).toBe('en');
    expect(appSettingsService.theme()).toBe('system');
  });

  it('should render instance headings with logos', async () => {
    fixture.detectChanges();

    httpTestingController.expectOne('/api/settings/general').flush(createGeneralSettingsResponse());
    httpTestingController.expectOne('/api/settings/logging').flush(createLoggingResponse());

    await fixture.componentInstance['initializePageAsync']();
    fixture.detectChanges();

    fixture.componentInstance['selectedTabIndex'].set(1);
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 50));
    fixture.detectChanges();
    httpTestingController.expectOne('/api/settings/instances').flush(createInstancesResponse());
    await fixture.whenStable();
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 50));
    fixture.detectChanges();
    await fixture.whenStable();

    const logos: NodeListOf<HTMLImageElement> = fixture.nativeElement.querySelectorAll('.mat-mdc-tab-body-active img[aria-hidden="true"]');
    expect(logos).toHaveSize(2);
    expect(logos[0].getAttribute('src')).toBe('/icons/logo_radarr.svg');
    expect(logos[1].getAttribute('src')).toBe('/icons/logo_sonarr.svg');
  });

  it('should read the active tab from the url', async () => {
    activatedRouteStub.setQueryParams({ tab: 'tasks' });
    fixture.detectChanges();

    httpTestingController.expectOne('/api/settings/general').flush(createGeneralSettingsResponse());

    await fixture.componentInstance['initializePageAsync']();
    fixture.detectChanges();
    httpTestingController.expectOne('/api/settings/tasks').flush(createScheduledTasksResponse());

    expect(fixture.componentInstance['selectedTabIndex']()).toBe(2);
  });

  it('should update the tab query param when the active tab changes', async () => {
    fixture.detectChanges();

    httpTestingController.expectOne('/api/settings/general').flush(createGeneralSettingsResponse());
    httpTestingController.expectOne('/api/settings/logging').flush(createLoggingResponse());

    await fixture.componentInstance['initializePageAsync']();
    fixture.detectChanges();

    const router: Router = TestBed.inject(Router);
    const navigateSpy: jasmine.Spy = spyOn(router, 'navigate').and.resolveTo(true);

    fixture.componentInstance['handleTabSelectionChange'](1);
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 50));
    fixture.detectChanges();
    httpTestingController.expectOne('/api/settings/instances').flush(createInstancesResponse());

    expect(fixture.componentInstance['selectedTabIndex']()).toBe(1);
    expect(navigateSpy).toHaveBeenCalledWith(
      [],
      jasmine.objectContaining({
        queryParams: { tab: 'arr-instances' },
        queryParamsHandling: 'merge',
        replaceUrl: false,
      }),
    );
  });

  it('should follow browser back and forward changes to the tab query param', async () => {
    fixture.detectChanges();

    httpTestingController.expectOne('/api/settings/general').flush(createGeneralSettingsResponse());
    httpTestingController.expectOne('/api/settings/logging').flush(createLoggingResponse());

    await fixture.componentInstance['initializePageAsync']();
    fixture.detectChanges();

    activatedRouteStub.setQueryParams({ tab: 'tasks' });
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 50));
    fixture.detectChanges();
    httpTestingController.expectOne('/api/settings/tasks').flush(createScheduledTasksResponse());

    expect(fixture.componentInstance['selectedTabIndex']()).toBe(2);
  });

  it('should block browser tab changes when the active tab has unsaved changes', async () => {
    fixture.detectChanges();

    httpTestingController.expectOne('/api/settings/general').flush(createGeneralSettingsResponse());
    httpTestingController.expectOne('/api/settings/logging').flush(createLoggingResponse());

    await fixture.componentInstance['initializePageAsync']();
    fixture.detectChanges();

    const router: Router = TestBed.inject(Router);
    const navigateSpy: jasmine.Spy = spyOn(router, 'navigate').and.resolveTo(true);
    const confirmTarget: typeof globalThis & {
      confirm: (message?: string) => boolean;
    } = globalThis as typeof globalThis & {
      confirm: (message?: string) => boolean;
    };
    spyOn(confirmTarget, 'confirm').and.returnValue(false);

    const settingsGeneralTabWidget: SettingsPage['settingsGeneralTabWidget'] = fixture.componentInstance['settingsGeneralTabWidget']!;
    settingsGeneralTabWidget['settingsForm'].controls.language.markAsDirty();

    activatedRouteStub.setQueryParams({ tab: 'tasks' });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    fixture.detectChanges();

    expect(fixture.componentInstance['selectedTabIndex']()).toBe(0);
    expect(navigateSpy).toHaveBeenCalledWith(
      [],
      jasmine.objectContaining({
        queryParams: { tab: 'general' },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      }),
    );
  });
});
