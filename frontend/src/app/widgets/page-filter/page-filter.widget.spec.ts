import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSlideToggleChange } from '@angular/material/slide-toggle';
import { FilterOptionType } from '../../enums/filter-option-type';
import { DEFAULT_FILTER_ALL, DateTimeRangeFilterOption, MultiSelectFilterOption, SwitchFilterOption } from '../../interfaces/filter-option';
import { PageFilterWidget } from './page-filter.widget';

describe('PageFilterWidget', () => {
  const storageKey = 'filters:page-filter-unit-test';
  let fixture: ComponentFixture<PageFilterWidget>;
  let component: PageFilterWidget;
  let switchFilter: SwitchFilterOption;
  let multiFilter: MultiSelectFilterOption;

  beforeEach(async () => {
    localStorage.removeItem(storageKey);
    switchFilter = {
      id: 'test-switch',
      label: 'Test Switch',
      type: FilterOptionType.SWITCH,
      value: false,
      onChanged: jasmine.createSpy('switchChanged'),
    };
    multiFilter = {
      id: 'test-multi',
      label: 'Test Multi',
      type: FilterOptionType.MULTISELECT,
      options: [
        { value: '1', label: 'One' },
        { value: '2', label: 'Two' },
      ],
      selectedValues: [],
      onChanged: jasmine.createSpy('multiChanged'),
    };
    await TestBed.configureTestingModule({ imports: [PageFilterWidget] }).compileComponents();
    fixture = TestBed.createComponent(PageFilterWidget);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('filters', [switchFilter, multiFilter]);
    fixture.componentRef.setInput('storagePrefix', 'page-filter-unit-test');
    fixture.detectChanges();
  });

  afterEach(() => localStorage.removeItem(storageKey));

  function savedFilters(): Record<string, unknown> {
    return JSON.parse(localStorage.getItem(storageKey)!);
  }

  function dateFilter(persist = true): DateTimeRangeFilterOption {
    return {
      id: 'test-date',
      label: 'Test Date',
      type: FilterOptionType.DATE_TIME_RANGE,
      persist,
      startDate: new Date('2024-01-01T00:00:00Z'),
      endDate: new Date('2024-01-02T23:59:00Z'),
      onChanged: jasmine.createSpy('dateChanged'),
    };
  }

  it('creates the widget with no active default filters', () => {
    expect(component).toBeTruthy();
    expect(component['activeFilterCount']()).toBe(0);
  });

  it('counts explicit selections while treating empty and All selections as unrestricted', () => {
    component['onMultiSelectChanged'](multiFilter, ['1']);
    expect(component['activeFilterCount']()).toBe(1);
    component['onMultiSelectChanged'](multiFilter, ['1', '2']);
    expect(component['activeFilterCount']()).toBe(1);
    component['onMultiSelectChanged'](multiFilter, [DEFAULT_FILTER_ALL]);
    expect(component['activeFilterCount']()).toBe(0);
    component['onMultiSelectChanged'](multiFilter, []);
    expect(component['activeFilterCount']()).toBe(0);
    component['onToggleChanged'](switchFilter, { checked: true } as MatSlideToggleChange);
    expect(component['activeFilterCount']()).toBe(1);
  });

  it('updates the switch, calls its callback, and persists the change', () => {
    component['onToggleChanged'](switchFilter, { checked: true } as MatSlideToggleChange);
    expect(switchFilter.value).toBeTrue();
    expect(switchFilter.onChanged).toHaveBeenCalledOnceWith(true);
    expect(savedFilters()['test-switch']).toBe(true);
  });

  it('updates the multiselect, calls its callback, and persists the selected values', () => {
    component['onMultiSelectChanged'](multiFilter, ['1']);
    expect(multiFilter.selectedValues).toEqual(['1']);
    expect(multiFilter.onChanged).toHaveBeenCalledOnceWith(['1']);
    expect(savedFilters()['test-multi']).toEqual(['1']);
  });

  it('restores saved state without invoking user-change callbacks', async () => {
    localStorage.setItem(storageKey, JSON.stringify({ 'test-switch': true, 'test-multi': ['2'] }));
    const restoredFixture = TestBed.createComponent(PageFilterWidget);
    restoredFixture.componentRef.setInput('filters', [switchFilter, multiFilter]);
    restoredFixture.componentRef.setInput('storagePrefix', 'page-filter-unit-test');
    restoredFixture.detectChanges();
    await restoredFixture.whenStable();
    expect(switchFilter.value).toBeTrue();
    expect(multiFilter.selectedValues).toEqual(['2']);
    expect(switchFilter.onChanged).not.toHaveBeenCalled();
    expect(multiFilter.onChanged).not.toHaveBeenCalled();
  });

  it('restores a saved empty multiselect instead of keeping an explicit default selection', async () => {
    localStorage.setItem(storageKey, JSON.stringify({ 'test-multi': [] }));
    multiFilter.selectedValues = ['1'];
    const restoredFixture = TestBed.createComponent(PageFilterWidget);
    restoredFixture.componentRef.setInput('filters', [multiFilter]);
    restoredFixture.componentRef.setInput('storagePrefix', 'page-filter-unit-test');
    restoredFixture.detectChanges();
    await restoredFixture.whenStable();
    expect(multiFilter.selectedValues).toEqual([]);
    expect(savedFilters()['test-multi']).toEqual([]);
  });

  it('updates a date range and persists its ISO timestamps', () => {
    const filter = dateFilter();
    fixture.componentRef.setInput('filters', [filter]);
    fixture.detectChanges();
    const value = { startDate: new Date('2024-02-01T10:00:00Z'), endDate: new Date('2024-02-02T11:00:00Z') };
    component['onDateTimeRangeChanged'](filter, value);
    expect(filter.startDate).toEqual(value.startDate);
    expect(filter.endDate).toEqual(value.endDate);
    expect(filter.onChanged).toHaveBeenCalledOnceWith(value);
    expect(savedFilters()['test-date']).toEqual({ startDate: value.startDate.toISOString(), endDate: value.endDate.toISOString() });
  });

  it('neither restores nor persists volatile date filters while restoring persistent filters', async () => {
    localStorage.setItem(
      storageKey,
      JSON.stringify({
        'test-switch': true,
        'test-date': { startDate: '2023-01-01T00:00:00Z', endDate: '2023-01-02T00:00:00Z' },
      }),
    );
    const filter = dateFilter(false);
    const restoredFixture = TestBed.createComponent(PageFilterWidget);
    restoredFixture.componentRef.setInput('filters', [switchFilter, filter]);
    restoredFixture.componentRef.setInput('storagePrefix', 'page-filter-unit-test');
    restoredFixture.detectChanges();
    await restoredFixture.whenStable();
    expect(switchFilter.value).toBeTrue();
    expect(filter.startDate).toEqual(new Date('2024-01-01T00:00:00Z'));
    expect(filter.endDate).toEqual(new Date('2024-01-02T23:59:00Z'));
    const value = { startDate: new Date('2024-02-01T14:00:00Z'), endDate: new Date('2024-02-02T15:00:00Z') };
    restoredFixture.componentInstance['onDateTimeRangeChanged'](filter, value);
    expect(filter.onChanged).toHaveBeenCalledOnceWith(value);
    expect(savedFilters()['test-switch']).toBe(true);
    expect(savedFilters()['test-date']).toBeUndefined();
  });

  it('renders active bubbles inside the filter bar by default', () => {
    component['onToggleChanged'](switchFilter, { checked: true } as MatSlideToggleChange);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.filter-bar .filter-summary .filter-bubble')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.filter-bar ~ .filter-summary')).toBeNull();
  });

  it('moves the active bubbles into their own row below the filter bar when bubblesBelow is set', () => {
    component['onToggleChanged'](switchFilter, { checked: true } as MatSlideToggleChange);
    fixture.componentRef.setInput('bubblesBelow', true);
    fixture.detectChanges();

    expect(fixture.nativeElement.classList.contains('page-filter--bubbles-below')).toBeTrue();
    expect(fixture.nativeElement.querySelector('.filter-bar .filter-summary')).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('.filter-bar ~ .filter-summary .filter-bubble')).toHaveSize(1);
  });

  it('omits the separate bubble row in bubblesBelow mode while no filter is active', () => {
    fixture.componentRef.setInput('bubblesBelow', true);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.filter-summary')).toBeNull();
  });
});
