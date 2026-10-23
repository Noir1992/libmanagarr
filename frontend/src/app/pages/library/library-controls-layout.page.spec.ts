import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { AppI18nService } from '../../services/app-i18n.service';
import { AppSettingsService } from '../../services/app-settings.service';
import { RadarrApiService } from '../../services/radarr-api.service';
import { SonarrApiService } from '../../services/sonarr-api.service';
import { LibraryPage } from './library.page';

describe('LibraryPage control layout', () => {
  let fixture: ComponentFixture<LibraryPage>;

  beforeEach(async () => {
    const radarr = jasmine.createSpyObj<RadarrApiService>('Radarr', ['fetchFilterValues', 'fetchLibrary']);
    const sonarr = jasmine.createSpyObj<SonarrApiService>('Sonarr', ['fetchFilterValues', 'fetchLibrary']);
    const filterValues = of({ genres: [], tags: [], statuses: [], qualityProfiles: [], audioLanguages: [], subtitleLanguages: [] });
    radarr.fetchFilterValues.and.returnValue(filterValues);
    sonarr.fetchFilterValues.and.returnValue(filterValues);
    radarr.fetchLibrary.and.returnValue(of({ syncedAt: null, instances: [] }));
    sonarr.fetchLibrary.and.returnValue(of({ syncedAt: null, instances: [] }));

    TestBed.configureTestingModule({
      providers: [
        {
          provide: AppSettingsService,
          useValue: {
            sonarrInstances: signal([{ id: 's', name: 'Sonarr', baseUrl: '', apiKey: '' }]),
            radarrInstances: signal([{ id: 'r', name: 'Radarr', baseUrl: '', apiKey: '' }]),
            loadError: signal(null),
            titleSorting: signal({ enabled: false, ignoredLeadingWords: [] }),
            loadInstances: () => Promise.resolve(),
          },
        },
        { provide: RadarrApiService, useValue: radarr },
        { provide: SonarrApiService, useValue: sonarr },
      ],
    });
    TestBed.inject(AppI18nService).setLanguage('en');

    fixture = TestBed.createComponent(LibraryPage);
    fixture.nativeElement.style.cssText = 'position: fixed; top: 0; left: 0; width: 980px;';
    fixture.detectChanges();
  });

  function panel(selector: string): HTMLElement {
    const element: HTMLElement | null = fixture.nativeElement.querySelector(selector);
    if (!(element instanceof HTMLElement)) {
      throw new Error(`Missing element: ${selector}`);
    }
    return element;
  }

  it('shows the filter and sort icon buttons side by side at the same height', () => {
    const filterButton = panel('.list-controls app-page-filter .filter-toggle-button');
    const sortButton = panel('.list-controls app-sort-control .sort-toggle-button');

    expect(Math.round(sortButton.getBoundingClientRect().height)).toBe(Math.round(filterButton.getBoundingClientRect().height));
  });

  it('keeps the sort icon visible while the filter editor popover is open', () => {
    panel('.list-controls app-page-filter .filter-toggle-button').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.filter-editor')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-sort-control .sort-toggle-button')).toBeTruthy();
  });

  it('places the title search left of the filter icon in the same toolbar row', () => {
    const search = panel('.list-controls .title-search input[type="search"]');
    const filterButton = panel('.list-controls app-page-filter .filter-toggle-button');
    const searchRect = search.getBoundingClientRect();
    const filterRect = filterButton.getBoundingClientRect();

    expect(searchRect.right).toBeLessThanOrEqual(filterRect.left);
    expect(searchRect.top).toBeLessThan(filterRect.bottom);
    expect(filterRect.top).toBeLessThan(searchRect.bottom);
  });

  it('keeps the search field and all icon buttons in one row at phone width', () => {
    fixture.nativeElement.style.width = '360px';
    fixture.detectChanges();

    const centerY = (selector: string): number => {
      const rect = panel(selector).getBoundingClientRect();
      return rect.top + rect.height / 2;
    };
    const searchCenter = centerY('.list-controls .title-search input');

    for (const selector of [
      '.list-controls app-page-filter .filter-toggle-button',
      '.list-controls app-sort-control .sort-toggle-button',
      '.list-controls .view-toggle-button',
    ]) {
      expect(Math.abs(centerY(selector) - searchCenter)).toBeLessThanOrEqual(2);
    }
  });
});
