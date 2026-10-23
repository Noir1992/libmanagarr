import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { FilterOptionType } from '../../enums/filter-option-type';
import { DEFAULT_FILTER_ALL, MultiSelectFilterOption } from '../../interfaces/filter-option';
import { AppI18nService } from '../../services/app-i18n.service';
import { normalizeResolutionGroup } from '../../utils/resolution-statistics-utils';
import { PageFilterWidget } from './page-filter.widget';

describe('PageFilterWidget empty multiselect defaults', () => {
  let fixture: ComponentFixture<PageFilterWidget>;
  let filter: MultiSelectFilterOption;
  const storageKey = 'filters:empty-default-test';

  beforeEach(async () => {
    localStorage.removeItem(storageKey);
    await TestBed.configureTestingModule({ imports: [PageFilterWidget] }).compileComponents();
    TestBed.inject(AppI18nService).setLanguage('en');
    filter = {
      id: 'genre',
      label: 'Genres',
      type: FilterOptionType.MULTISELECT,
      options: [
        { value: 'drama', label: 'Drama' },
        { value: 'comedy', label: 'Comedy' },
      ],
      selectedValues: [],
      onChanged: jasmine.createSpy('changed'),
    };
    fixture = TestBed.createComponent(PageFilterWidget);
    fixture.componentRef.setInput('filters', [filter]);
    fixture.componentRef.setInput('storagePrefix', 'empty-default-test');
    fixture.componentInstance['expanded'].set(true);
  });

  afterEach(() => localStorage.removeItem(storageKey));

  it('retains a saved select value after converting the filter to a multiselect', () => {
    localStorage.setItem(storageKey, JSON.stringify({ genre: 'drama' }));
    fixture.detectChanges();
    expect(filter.selectedValues).toEqual(['drama']);
    expect(JSON.parse(localStorage.getItem(storageKey)!)[filter.id]).toEqual(['drama']);
  });

  it('migrates saved resolution aliases while retaining exclusion', async () => {
    filter.id = 'resolution-filter';
    filter.supportsExclusion = true;
    filter.options = [
      { value: '4k', label: '4k' },
      { value: '1080p', label: '1080p' },
    ];
    filter.normalizeValue = normalizeResolutionGroup;
    localStorage.setItem(storageKey, JSON.stringify({ 'resolution-filter': ['2160p', '4320p'], 'resolution-filter:exclude': true }));
    await fixture.whenStable();
    expect(filter.selectedValues).toEqual(['4k']);
    expect(filter.exclude).toBeTrue();
    expect(JSON.parse(localStorage.getItem(storageKey)!)['resolution-filter']).toEqual(['4k']);
  });

  it('starts with every checkbox unchecked and lets select-all and deselect-all work explicitly', async () => {
    fixture.detectChanges();
    const select = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatSelectHarness);
    await select.open();
    const options = await select.getOptions();
    expect(await Promise.all(options.map((option) => option.isSelected()))).toEqual([false, false, false]);
    expect(fixture.componentInstance['activeFilterCount']()).toBe(0);
    await options[0].click();
    expect(filter.selectedValues).toEqual([DEFAULT_FILTER_ALL]);
    expect(await Promise.all(options.map((option) => option.isSelected()))).toEqual([true, true, true]);
    await options[0].click();
    expect(filter.selectedValues).toEqual([]);
    expect(await Promise.all(options.map((option) => option.isSelected()))).toEqual([false, false, false]);
    expect(fixture.componentInstance['activeFilterCount']()).toBe(0);
  });

  for (const legacy of [[DEFAULT_FILTER_ALL], ['drama', 'comedy']]) {
    it(`migrates the old all-selected value ${JSON.stringify(legacy)} to an empty selection`, async () => {
      localStorage.setItem(storageKey, JSON.stringify({ genre: legacy }));
      fixture.detectChanges();
      await fixture.whenStable();
      expect(filter.selectedValues).toEqual([]);
      expect(JSON.parse(localStorage.getItem(storageKey)!).genre).toEqual([]);
    });
  }

  it('restores a saved partial selection', async () => {
    localStorage.setItem(storageKey, JSON.stringify({ genre: ['drama'] }));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(filter.selectedValues).toEqual(['drama']);
    expect(fixture.componentInstance['activeFilterCount']()).toBe(1);
    fixture.componentInstance['removeFilter'](filter);
    expect(filter.selectedValues).toEqual([]);
    expect(filter.onChanged).toHaveBeenCalledOnceWith([]);
    expect(JSON.parse(localStorage.getItem(storageKey)!).genre).toEqual([]);
  });

  it('preserves an explicit new select-all choice when it is restored', async () => {
    localStorage.setItem(storageKey, JSON.stringify({ genre: [DEFAULT_FILTER_ALL], __emptyMultiselectDefaults: true }));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(filter.selectedValues).toEqual([DEFAULT_FILTER_ALL]);
  });

  it('preserves a saved exclusion of every concrete option', async () => {
    filter.supportsExclusion = true;
    localStorage.setItem(storageKey, JSON.stringify({ genre: ['drama', 'comedy'], 'genre:exclude': true }));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(filter.selectedValues).toEqual(['drama', 'comedy']);
    expect(filter.exclude).toBeTrue();
  });
});
