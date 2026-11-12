import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReleaseOverview } from '../../interfaces/releases/release-overview';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { ReleasesPage } from './releases.page';

describe('ReleasesPage', () => {
  let fixture: ComponentFixture<ReleasesPage>;
  let httpTestingController: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ReleasesPage],
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        {
          provide: AppI18nService,
          useValue: {
            t: (key: string): string =>
              ({
                'releases.title': 'What is new',
                'releases.description': 'Release notes description',
                'releases.current': 'Current version',
                'releases.newer': 'Newer than installed',
                'releases.prerelease': 'Prerelease',
                'releases.empty': 'No published releases are available yet.',
                'releases.unreachable': 'The published releases could not be fetched.',
                'releases.rateLimited': 'GitHub is not answering further requests right now.',
                'releases.rejected': 'GitHub rejected the request.',
              })[key] ?? key,
          },
        },
        { provide: AppSettingsService, useValue: { localeId: (): string => 'en-US' } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ReleasesPage);
    httpTestingController = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTestingController.verify();
  });

  it('reads the release list as soon as the page opens', async () => {
    fixture.detectChanges();

    httpTestingController.expectOne('/api/releases/').flush(overview('Ok'));
    await fixture.whenStable();
    fixture.detectChanges();

    const entries: NodeListOf<Element> = (fixture.nativeElement as HTMLElement).querySelectorAll('.release-entry');

    expect(entries).toHaveSize(2);
    expect(entries[0].querySelector('.release-entry-version')?.textContent).toContain('1.4.0');
    expect(entries[0].querySelector('.release-entry-link')?.getAttribute('href')).toContain('/releases/tag/v1.4.0');
    expect(entries[0].querySelector('.release-entry-date')?.textContent).toMatch(/\d{1,2}:\d{2}/);
    expect(entries[0].querySelector('[data-state="newer"]')?.textContent).toContain('Newer than installed');
    expect(entries[1].querySelector('[data-state="current"]')?.textContent).toContain('Current version');
    expect(entries[1].querySelector('.release-entry-notes li')?.textContent).toContain('Fixed the calendar');
  });

  it('names an unreachable release host next to the last known list', async () => {
    fixture.detectChanges();

    httpTestingController
      .expectOne('/api/releases/')
      .flush(overview('Unreachable', { versions: [], checkedAt: '2026-11-02T10:00:00.000Z' }));
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('.releases-notice')?.textContent).toContain('The published releases could not be fetched.');
    expect(compiled.querySelector('.releases-empty')?.textContent).toContain('No published releases are available yet.');
  });

  it('reports a failed read instead of leaving the page empty', async () => {
    fixture.detectChanges();

    httpTestingController.expectOne('/api/releases/').flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.loading-error-state-message')?.textContent).toContain(
      'The published releases could not be fetched.',
    );
  });

  it('keeps the page usable while no answer arrives', async () => {
    fixture.detectChanges();

    httpTestingController.expectOne('/api/releases/');

    const compiled: HTMLElement = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('.page-title')?.textContent).toContain('What is new');
    expect(compiled.querySelector('app-loading-section')).toBeTruthy();
  });

  function overview(status: ReleaseOverview['status'], overrides?: { versions?: readonly string[]; checkedAt?: string }): ReleaseOverview {
    const versions: readonly string[] = overrides?.versions ?? ['1.4.0', '1.3.0'];

    return {
      currentVersion: '1.3.0',
      updateAvailable: versions.some((version) => version !== '1.3.0'),
      status,
      checkedAt: overrides?.checkedAt ?? '2026-11-02T09:30:00.000Z',
      versions: versions.map((version): ReleaseOverview['versions'][number] => ({
        version,
        tag: `v${version}`,
        publishedAt: '2026-11-01T08:00:00.000Z',
        notes: version === '1.3.0' ? '- Fixed the calendar' : '',
        url: `https://github.com/Noir1992/libmanagarr/releases/tag/v${version}`,
        isPrerelease: false,
        isCurrent: version === '1.3.0',
        isNewerThanCurrent: version !== '1.3.0',
      })),
    };
  }
});
