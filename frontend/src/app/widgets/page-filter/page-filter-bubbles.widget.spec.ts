import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSelectChange } from '@angular/material/select';
import { FilterOptionType } from '../../enums/filter-option-type';
import { DEFAULT_FILTER_ALL, FilterOption, MultiSelectFilterOption, SelectFilterOption } from '../../interfaces/filter-option';
import { AppI18nService } from '../../services/app-i18n.service';
import { PageFilterWidget } from './page-filter.widget';

describe('PageFilterWidget active filter bubbles', () => {
  const storageKey = 'filters:bubble-test';
  let fixture: ComponentFixture<PageFilterWidget>;
  let genre: MultiSelectFilterOption;
  let aired: SelectFilterOption;

  beforeEach(async () => {
    localStorage.removeItem(storageKey);
    await TestBed.configureTestingModule({ imports: [PageFilterWidget] }).compileComponents();
    TestBed.inject(AppI18nService).setLanguage('de');
    genre = {
      id: 'genre',
      label: 'Genres',
      type: FilterOptionType.MULTISELECT,
      options: [
        { value: 'drama', label: 'Drama' },
        { value: 'comedy', label: 'Comedy' },
      ],
      selectedValues: [DEFAULT_FILTER_ALL],
      onChanged: jasmine.createSpy('genreChanged'),
    };
    aired = {
      id: 'aired',
      label: 'Ausgestrahlt',
      type: FilterOptionType.SELECT,
      options: [
        { value: 'all', label: 'Alle' },
        { value: 'aired', label: 'Bereits ausgestrahlt' },
      ],
      selectedValue: 'all',
      onChanged: jasmine.createSpy('airedChanged'),
    };
    fixture = TestBed.createComponent(PageFilterWidget);
    fixture.componentRef.setInput('filters', [genre, aired]);
    fixture.componentRef.setInput('storagePrefix', 'bubble-test');
  });

  afterEach(() => localStorage.removeItem(storageKey));

  function bubbles(): HTMLElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.filter-bubble'));
  }

  it('shows selected labels next to the title and removes only the clicked filter', async () => {
    genre.selectedValues = ['drama', 'comedy'];
    aired.selectedValue = 'aired';
    await fixture.whenStable();
    expect(bubbles().map((bubble) => bubble.textContent)).toEqual([
      jasmine.stringContaining('Genres: Drama, Comedy'),
      jasmine.stringContaining('Ausgestrahlt: Bereits ausgestrahlt'),
    ]);

    const remove = bubbles()[0].querySelector('button')!;
    expect(remove.getAttribute('aria-label')).toBe('Filter „Genres“ entfernen');
    remove.click();
    await fixture.whenStable();

    expect(genre.onChanged).toHaveBeenCalledOnceWith([]);
    expect(aired.onChanged).not.toHaveBeenCalled();
    expect(bubbles()).toHaveSize(1);
    expect(bubbles()[0].textContent).toContain('Ausgestrahlt');
    expect(JSON.parse(localStorage.getItem(storageKey)!).genre).toEqual([]);
  });

  it('hides default filters and updates bubbles and count when the selection changes', async () => {
    const counts: number[] = [];
    fixture.componentInstance.activeFilterCountChange.subscribe((count) => counts.push(count));
    await fixture.whenStable();
    expect(bubbles()).toEqual([]);
    expect(counts.at(-1)).toBe(0);

    fixture.componentInstance['onSelectChanged'](aired, { value: 'aired' } as MatSelectChange);
    await fixture.whenStable();
    expect(bubbles()[0].textContent).toContain('Bereits ausgestrahlt');
    expect(counts.at(-1)).toBe(1);

    bubbles()[0].querySelector('button')!.click();
    await fixture.whenStable();
    expect(aired.onChanged).toHaveBeenCalledWith('all');
    expect(bubbles()).toEqual([]);
    expect(counts.at(-1)).toBe(0);
  });

  it('treats a restored empty multiselect as inactive', async () => {
    localStorage.setItem(storageKey, JSON.stringify({ genre: [] }));
    await fixture.whenStable();
    expect(genre.selectedValues).toEqual([]);
    expect(bubbles()).toEqual([]);
    expect(fixture.componentInstance['activeFilterCount']()).toBe(0);
  });

  it('clears numeric and date ranges and persists their empty values', async () => {
    const rangeFilters: FilterOption[] = [
      {
        id: 'score',
        label: 'Score',
        type: FilterOptionType.NUMBER_RANGE,
        minValue: 0,
        maxValue: null,
        onChanged: jasmine.createSpy('scoreChanged'),
      },
      {
        id: 'date',
        label: 'Erstausstrahlung',
        type: FilterOptionType.DATE_RANGE,
        startDate: null,
        endDate: '2026-10-01',
        onChanged: jasmine.createSpy('dateChanged'),
      },
    ];
    fixture.componentRef.setInput('filters', rangeFilters);
    await fixture.whenStable();
    expect(bubbles()[0].textContent).toContain('Score: 0 – …');
    expect(bubbles()[1].textContent).toContain('Erstausstrahlung: … – 2026-10-01');

    for (const bubble of bubbles()) {
      bubble.querySelector('button')!.click();
    }
    await fixture.whenStable();
    expect(rangeFilters[0].onChanged).toHaveBeenCalledOnceWith({ minValue: null, maxValue: null });
    expect(rangeFilters[1].onChanged).toHaveBeenCalledOnceWith({ startDate: null, endDate: null });
    expect(bubbles()).toEqual([]);
    expect(JSON.parse(localStorage.getItem(storageKey)!)).toEqual({
      __emptyMultiselectDefaults: true,
      score: { minValue: null, maxValue: null },
      date: { startDate: null, endDate: null },
    });
  });
});
