import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FilterOptionType } from '../../enums/filter-option-type';
import { DEFAULT_FILTER_ALL, MultiSelectFilterOption } from '../../interfaces/filter-option';
import { AppI18nService } from '../../services/app-i18n.service';
import { PageFilterWidget } from './page-filter.widget';

describe('PageFilterWidget exclusion editor', () => {
  const storageKey = 'filters:exclusion-test';
  let fixture: ComponentFixture<PageFilterWidget>;
  let audio: MultiSelectFilterOption;
  let subtitle: MultiSelectFilterOption;
  let root: HTMLElement;

  beforeEach(async () => {
    localStorage.removeItem(storageKey);
    await TestBed.configureTestingModule({ imports: [PageFilterWidget] }).compileComponents();
    TestBed.inject(AppI18nService).setLanguage('de');
    audio = {
      id: 'audio-language-filter',
      label: 'Audiosprachen',
      type: FilterOptionType.MULTISELECT,
      options: [
        { value: 'Japanese', label: 'Japanese' },
        { value: 'German', label: 'German' },
      ],
      selectedValues: ['Japanese'],
      supportsExclusion: true,
      onChanged: jasmine.createSpy('audioChanged'),
    };
    subtitle = {
      id: 'subtitle-language-filter',
      label: 'Untertitelsprachen',
      type: FilterOptionType.MULTISELECT,
      options: [{ value: 'German', label: 'German' }],
      selectedValues: ['German'],
      supportsExclusion: true,
      onChanged: jasmine.createSpy('subtitleChanged'),
    };
    fixture = TestBed.createComponent(PageFilterWidget);
    fixture.componentRef.setInput('filters', [audio, subtitle]);
    fixture.componentRef.setInput('storagePrefix', 'exclusion-test');
    root = fixture.nativeElement;
    await fixture.whenStable();
  });

  afterEach(() => localStorage.removeItem(storageKey));

  async function openEditor(): Promise<void> {
    root.querySelector<HTMLButtonElement>('.filter-toggle-button')!.click();
    await fixture.whenStable();
  }

  it('opens a searchable editor and changes the subtitle polarity without changing audio', async () => {
    expect(root.querySelector('.filter-editor')).toBeNull();
    await openEditor();
    expect(root.querySelector('.filter-toggle-button')!.getAttribute('aria-expanded')).toBe('true');
    const cards = root.querySelectorAll('.filter-card');
    cards[1].querySelectorAll<HTMLButtonElement>('.filter-mode button')[1].click();
    await fixture.whenStable();

    expect(subtitle.exclude).toBeTrue();
    expect(subtitle.onChanged).toHaveBeenCalledOnceWith(['German']);
    expect(audio.exclude).toBeUndefined();
    expect(audio.onChanged).not.toHaveBeenCalled();
    expect(root.querySelector('.filter-bubble-excluded')!.textContent).toContain('Nicht German');
    expect(cards[1].querySelectorAll('.filter-mode button')[1].getAttribute('aria-pressed')).toBe('true');
    const stored = JSON.parse(localStorage.getItem(storageKey)!);
    expect(stored['subtitle-language-filter:exclude']).toBeTrue();
    expect(stored['audio-language-filter']).toEqual(['Japanese']);
  });

  it('can flip the mode and remove a filter directly from its bubble', async () => {
    const subtitleBubble = root.querySelectorAll('.filter-bubble')[1];
    subtitleBubble.querySelector<HTMLButtonElement>('.filter-bubble-mode')!.click();
    await fixture.whenStable();
    expect(subtitle.exclude).toBeTrue();
    subtitleBubble.querySelector<HTMLButtonElement>('.filter-bubble-remove')!.click();
    await fixture.whenStable();

    expect(subtitle.exclude).toBeFalse();
    expect(subtitle.selectedValues).toEqual([]);
    expect(subtitle.onChanged).toHaveBeenCalledWith([]);
    expect(root.querySelectorAll('.filter-bubble')).toHaveSize(1);
    expect(root.querySelector('.filter-bubble')!.textContent).toContain('Japanese');
    expect(JSON.parse(localStorage.getItem(storageKey)!)['subtitle-language-filter:exclude']).toBeFalse();
  });

  it('searches filter names without clearing selections and keeps the search across closing', async () => {
    await openEditor();
    const search = root.querySelector<HTMLInputElement>('input[type=search]')!;
    search.value = 'untertitel';
    search.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(root.querySelectorAll('.filter-card')).toHaveSize(1);
    expect(root.querySelector('.filter-card')!.textContent).toContain('Untertitelsprachen');
    expect(audio.selectedValues).toEqual(['Japanese']);
    expect(root.querySelectorAll('.filter-bubble')).toHaveSize(2);
    await openEditor();
    expect(root.querySelector('.filter-editor')).toBeNull();
    await openEditor();
    expect(root.querySelector<HTMLInputElement>('input[type=search]')!.value).toBe('untertitel');
  });

  it('restores exclusion state and preserves compatibility with existing saved selections', async () => {
    localStorage.setItem(
      storageKey,
      JSON.stringify({
        'audio-language-filter': ['German'],
        'audio-language-filter:exclude': true,
        'subtitle-language-filter': ['German'],
      }),
    );
    const restored = TestBed.createComponent(PageFilterWidget);
    restored.componentRef.setInput('filters', [audio, subtitle]);
    restored.componentRef.setInput('storagePrefix', 'exclusion-test');
    await restored.whenStable();

    expect(audio.selectedValues).toEqual(['German']);
    expect(audio.exclude).toBeTrue();
    expect(subtitle.exclude).toBeUndefined();
    expect((restored.nativeElement as HTMLElement).querySelectorAll('.filter-bubble-excluded')).toHaveSize(1);
  });

  it('keeps All neutral when setting a mode before selecting a value', async () => {
    fixture.componentInstance['onMultiSelectChanged'](audio, [DEFAULT_FILTER_ALL]);
    await fixture.whenStable();
    await openEditor();
    root.querySelectorAll('.filter-card')[0].querySelectorAll<HTMLButtonElement>('.filter-mode button')[1].click();
    await fixture.whenStable();
    expect(audio.exclude).toBeTrue();
    expect(root.querySelectorAll('.filter-bubble')).toHaveSize(1);
    fixture.componentInstance['onMultiSelectChanged'](audio, ['German']);
    await fixture.whenStable();
    expect(root.querySelector('.filter-bubble-excluded')!.textContent).toContain('Nicht German');
  });
});

describe('PageFilterWidget excluded All bubble', () => {
  const storageKey = 'filters:excluded-all-test';
  let fixture: ComponentFixture<PageFilterWidget>;
  let instances: MultiSelectFilterOption;
  let genres: MultiSelectFilterOption;
  let root: HTMLElement;

  beforeEach(async () => {
    localStorage.removeItem(storageKey);
    await TestBed.configureTestingModule({ imports: [PageFilterWidget] }).compileComponents();
    TestBed.inject(AppI18nService).setLanguage('de');
    instances = {
      id: 'radarr-instance-filter',
      label: 'Radarr-Instanzen',
      type: FilterOptionType.MULTISELECT,
      options: [
        { value: 'first', label: 'First' },
        { value: 'second', label: 'Second' },
      ],
      selectedValues: [DEFAULT_FILTER_ALL],
      supportsExclusion: true,
      exclude: true,
      excludeAllRestricts: true,
      onChanged: jasmine.createSpy('instancesChanged'),
    };
    genres = {
      id: 'genre-filter',
      label: 'Genres',
      type: FilterOptionType.MULTISELECT,
      options: [{ value: 'drama', label: 'Drama' }],
      selectedValues: [DEFAULT_FILTER_ALL],
      supportsExclusion: true,
      exclude: true,
      onChanged: jasmine.createSpy('genresChanged'),
    };
    fixture = TestBed.createComponent(PageFilterWidget);
    fixture.componentRef.setInput('filters', [instances, genres]);
    fixture.componentRef.setInput('storagePrefix', 'excluded-all-test');
    root = fixture.nativeElement;
    await fixture.whenStable();
  });

  afterEach(() => localStorage.removeItem(storageKey));

  it('shows a Nicht Alle bubble while every instance is excluded and ignores the flag-less genres filter', async () => {
    const bubbles = root.querySelectorAll('.filter-bubble');
    expect(bubbles).toHaveSize(1);
    expect(bubbles[0].textContent).toContain('Radarr-Instanzen: Nicht Alle');
    expect(fixture.componentInstance['activeFilterCount']()).toBe(1);
    expect(genres.exclude).toBeTrue();
  });

  it('hides the bubble again when the mode flips back to include', async () => {
    root.querySelector<HTMLButtonElement>('.filter-bubble-mode')!.click();
    await fixture.whenStable();
    expect(instances.exclude).toBeFalse();
    expect(instances.onChanged).toHaveBeenCalledOnceWith([DEFAULT_FILTER_ALL]);
    expect(root.querySelectorAll('.filter-bubble')).toHaveSize(0);
  });

  it('resets mode and selection when the bubble is removed', async () => {
    root.querySelector<HTMLButtonElement>('.filter-bubble-remove')!.click();
    await fixture.whenStable();
    expect(instances.exclude).toBeFalse();
    expect(instances.selectedValues).toEqual([]);
    expect(instances.onChanged).toHaveBeenCalledOnceWith([]);
    expect(root.querySelectorAll('.filter-bubble')).toHaveSize(0);
  });
});
