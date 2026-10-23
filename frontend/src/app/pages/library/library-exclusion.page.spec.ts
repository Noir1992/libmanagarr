import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { FilterOptionType } from '../../enums/filter-option-type';
import { MultiSelectFilterOption } from '../../interfaces/filter-option';
import { AppSettingsService } from '../../services/app-settings.service';
import { RadarrApiService } from '../../services/radarr-api.service';
import { SonarrApiService } from '../../services/sonarr-api.service';
import { LibraryPage } from './library.page';

describe('LibraryPage exclusion requests', () => {
  let page: LibraryPage;
  let radarr: jasmine.SpyObj<RadarrApiService>;

  beforeEach(() => {
    radarr = jasmine.createSpyObj<RadarrApiService>('Radarr', ['fetchFilterValues', 'fetchLibrary']);
    radarr.fetchFilterValues.and.returnValue(
      of({
        statuses: [],
        genres: [],
        tags: [],
        qualityProfiles: [],
        audioLanguages: ['Japanese', 'German'],
        subtitleLanguages: ['German'],
      }),
    );
    radarr.fetchLibrary.and.returnValue(of({ syncedAt: null, instances: [] }));
    TestBed.configureTestingModule({
      providers: [
        {
          provide: AppSettingsService,
          useValue: {
            sonarrInstances: signal([]),
            radarrInstances: signal([
              { id: 'first', name: 'First', baseUrl: '', apiKey: '' },
              { id: 'second', name: 'Second', baseUrl: '', apiKey: '' },
            ]),
            loadError: signal(null),
            titleSorting: signal({ enabled: false, ignoredLeadingWords: [] }),
          },
        },
        { provide: RadarrApiService, useValue: radarr },
        { provide: SonarrApiService, useValue: {} },
      ],
    });
    page = TestBed.runInInjectionContext(() => new LibraryPage());
  });

  function multiselect(id: string): MultiSelectFilterOption {
    const filter = page['filterOptions']().find((filter) => filter.id === id);
    if (filter?.type !== FilterOptionType.MULTISELECT) throw new Error(`Missing filter ${id}`);
    return filter;
  }

  it('sends a positive audio selection and a negative subtitle selection and preserves them on refresh', async () => {
    multiselect('audio-language-filter').selectedValues = ['Japanese'];
    const subtitle = multiselect('subtitle-language-filter');
    subtitle.selectedValues = ['German'];
    subtitle.exclude = true;
    await page['loadFilterValues']();
    page['loadData']();
    expect(radarr.fetchLibrary).toHaveBeenCalledOnceWith(
      jasmine.objectContaining({
        audioLanguages: ['Japanese'],
        subtitleLanguages: ['German'],
        excludedFilters: ['subtitle-language-filter'],
      }),
    );
    expect(multiselect('subtitle-language-filter').exclude).toBeTrue();
  });

  it('uses the remaining instances for filter options and sends the raw excluded selection to the library', async () => {
    const instances = multiselect('radarr-instance-filter');
    instances.selectedValues = ['first'];
    instances.exclude = true;
    await page['loadFilterValues']();
    page['loadData']();
    expect(radarr.fetchFilterValues).toHaveBeenCalledOnceWith(['second']);
    expect(radarr.fetchLibrary).toHaveBeenCalledOnceWith(
      jasmine.objectContaining({
        radarrInstanceFilter: ['first'],
        excludedFilters: ['radarr-instance-filter'],
      }),
    );
  });

  it('skips requests when all configured instances are excluded', async () => {
    const instances = multiselect('radarr-instance-filter');
    instances.selectedValues = ['first', 'second'];
    instances.exclude = true;
    await page['loadFilterValues']();
    page['loadData']();
    expect(radarr.fetchFilterValues).not.toHaveBeenCalled();
    expect(radarr.fetchLibrary).not.toHaveBeenCalled();
    expect(page['loading']()).toBeFalse();
  });

  it('treats an empty exclusion as including every configured instance', async () => {
    const instances = multiselect('radarr-instance-filter');
    instances.selectedValues = [];
    instances.exclude = true;
    await page['loadFilterValues']();
    page['loadData']();
    expect(radarr.fetchFilterValues).toHaveBeenCalledOnceWith(['__all__']);
    expect(radarr.fetchLibrary).toHaveBeenCalledOnceWith(
      jasmine.objectContaining({
        radarrInstanceFilter: ['__all__'],
        excludedFilters: [],
      }),
    );
  });
});
