import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  InputSignal,
  output,
  OutputEmitterRef,
  signal,
  Signal,
  WritableSignal,
} from '@angular/core';
import { MatBadge } from '@angular/material/badge';
import { NgTemplateOutlet } from '@angular/common';
import { MatIconButton } from '@angular/material/button';
import { MatInput } from '@angular/material/input';
import { MatSelectChange, MatSelectModule } from '@angular/material/select';
import { MatSlideToggleChange, MatSlideToggleModule } from '@angular/material/slide-toggle';
import {
  DateRangeFilterOption,
  DateTimeRangeFilterOption,
  DEFAULT_FILTER_ALL,
  FilterOption,
  NumberRangeFilterOption,
} from '../../interfaces/filter-option';
import { FilterOptionType } from '../../enums/filter-option-type';
import { AppI18nService } from '../../services/app-i18n.service';
import { DateRangeTimePickerWidget, DateRangeTimeValue } from '../date-range-time-picker/date-range-time-picker.widget';
import { Multiselect } from '../multiselect/multiselect';

type StoredFilterState = Record<string, StoredFilterValue>;

type StoredFilterValue = boolean | string | string[] | null | StoredDateTimeRangeValue | StoredDateRangeValue | StoredNumberRangeValue;

type StoredDateRangeValue = {
  startDate: string | null;
  endDate: string | null;
};

type StoredDateTimeRangeValue = {
  startDate: string | null;
  endDate: string | null;
};

type StoredNumberRangeValue = {
  minValue: number | null;
  maxValue: number | null;
};

@Component({
  selector: 'app-page-filter',
  host: {
    '[class.page-filter--bubbles-below]': 'bubblesBelow()',
  },
  imports: [
    NgTemplateOutlet,
    MatInput,
    MatSelectModule,
    MatSlideToggleModule,
    MatIconButton,
    MatBadge,
    DateRangeTimePickerWidget,
    Multiselect,
  ],
  templateUrl: './page-filter.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './page-filter.widget.scss',
})
export class PageFilterWidget {
  private static readonly EMPTY_MULTISELECT_DEFAULTS_KEY = '__emptyMultiselectDefaults';
  private readonly i18n: AppI18nService = inject(AppI18nService);

  protected readonly filterTitle: Signal<string> = computed((): string => this.i18n.t('filter.title'));

  readonly filters: InputSignal<readonly FilterOption[]> = input.required<readonly FilterOption[]>();
  readonly storagePrefix: InputSignal<string> = input.required<string>();
  // Opt-in for pages that place the widget inside their own toolbar row: the host box dissolves
  // (see the host-class rules in the stylesheet) so the active-filter bubbles can be rendered in a
  // full-width row of their own below the toolbar instead of inside the filter bar.
  readonly bubblesBelow: InputSignal<boolean> = input<boolean>(false);

  protected readonly expanded = signal(false);
  protected readonly search = signal('');
  protected readonly labels = computed(() => ({
    include: this.i18n.t('filter.include'),
    exclude: this.i18n.t('filter.exclude'),
    not: this.i18n.t('filter.not'),
    search: this.i18n.t('filter.search'),
    hint: this.i18n.t('filter.hint'),
    noResults: this.i18n.t('filter.noResults'),
    min: this.i18n.t('filter.min'),
    max: this.i18n.t('filter.max'),
    from: this.i18n.t('filter.from'),
    to: this.i18n.t('filter.to'),
  }));
  protected readonly visibleFilters = computed(() => {
    const search = this.search().trim().toLocaleLowerCase();
    return this.filters().filter((filter) => filter.label.toLocaleLowerCase().includes(search));
  });

  readonly activeFilterCountChange: OutputEmitterRef<number> = output<number>();

  private readonly filterChangeTick: WritableSignal<number> = signal(0);

  protected readonly activeFilters: Signal<readonly FilterOption[]> = computed(() => {
    this.filterChangeTick();
    return this.filters().filter((filter) => {
      if (false === filter.active) {
        return false;
      }

      if (filter.type === FilterOptionType.MULTISELECT) {
        if (filter.selectedValues.length === 0) {
          return false;
        }

        if (filter.selectedValues.includes(DEFAULT_FILTER_ALL)) {
          return !!filter.exclude && !!filter.excludeAllRestricts;
        }

        return true;
      }

      if (filter.type === FilterOptionType.NUMBER_RANGE) {
        return filter.minValue !== null || filter.maxValue !== null;
      }

      if (filter.type === FilterOptionType.SELECT) {
        return filter.selectedValue !== null && filter.selectedValue !== filter.options[0]?.value;
      }

      if (filter.type === FilterOptionType.DATE_TIME_RANGE) {
        return filter.startDate || filter.endDate;
      }

      if (filter.type === FilterOptionType.DATE_RANGE) {
        return filter.startDate || filter.endDate;
      }

      return filter.value;
    });
  });

  protected readonly activeFilterCount: Signal<number> = computed(() => this.activeFilters().length);

  protected filterValueLabel(filter: FilterOption): string {
    switch (filter.type) {
      case FilterOptionType.SELECT:
        return filter.options.find((option) => option.value === filter.selectedValue)?.label ?? filter.selectedValue ?? '';
      case FilterOptionType.MULTISELECT:
        return filter.selectedValues.length === 0
          ? this.i18n.t('filter.none')
          : filter.selectedValues
              .map((value) =>
                value === DEFAULT_FILTER_ALL
                  ? this.i18n.t('filter.all')
                  : (filter.options.find((option) => option.value === value)?.label ?? value),
              )
              .join(', ');
      case FilterOptionType.NUMBER_RANGE:
        return `${filter.minValue ?? '…'} – ${filter.maxValue ?? '…'}`;
      case FilterOptionType.DATE_RANGE:
        return `${filter.startDate || '…'} – ${filter.endDate || '…'}`;
      case FilterOptionType.DATE_TIME_RANGE:
        return `${filter.startDate?.toLocaleString() ?? '…'} – ${filter.endDate?.toLocaleString() ?? '…'}`;
      case FilterOptionType.SWITCH:
        return '';
    }
  }

  protected removeFilterLabel(filter: FilterOption): string {
    return this.i18n.t('filter.remove', { filter: filter.label });
  }

  protected changeModeLabel(filter: FilterOption): string {
    return this.i18n.t('filter.changeMode', { filter: filter.label, mode: filter.exclude ? this.labels().include : this.labels().exclude });
  }

  protected onSearchChanged(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  protected onPolarityChanged(filter: FilterOption, exclude: boolean): void {
    if (!filter.supportsExclusion || !!filter.exclude === exclude) {
      return;
    }
    filter.exclude = exclude;
    this.persistCurrentFilters();
    switch (filter.type) {
      case FilterOptionType.SELECT:
        filter.onChanged(filter.selectedValue!);
        break;
      case FilterOptionType.MULTISELECT:
        filter.onChanged(filter.selectedValues);
        break;
      case FilterOptionType.NUMBER_RANGE:
        filter.onChanged({ minValue: filter.minValue, maxValue: filter.maxValue });
        break;
      case FilterOptionType.DATE_RANGE:
        filter.onChanged({ startDate: filter.startDate, endDate: filter.endDate });
        break;
      case FilterOptionType.DATE_TIME_RANGE:
        filter.onChanged({ startDate: filter.startDate, endDate: filter.endDate });
        break;
      case FilterOptionType.SWITCH:
        filter.onChanged(filter.value);
        break;
    }
  }

  protected removeFilter(filter: FilterOption): void {
    filter.exclude = false;
    switch (filter.type) {
      case FilterOptionType.SELECT:
        filter.selectedValue = filter.options[0]?.value ?? null;
        filter.onChanged(filter.selectedValue!);
        break;
      case FilterOptionType.MULTISELECT:
        filter.selectedValues = [];
        filter.onChanged(filter.selectedValues);
        break;
      case FilterOptionType.NUMBER_RANGE:
        filter.minValue = null;
        filter.maxValue = null;
        filter.onChanged({ minValue: null, maxValue: null });
        break;
      case FilterOptionType.DATE_RANGE:
      case FilterOptionType.DATE_TIME_RANGE:
        filter.startDate = null;
        filter.endDate = null;
        filter.onChanged({ startDate: null, endDate: null });
        break;
      case FilterOptionType.SWITCH:
        filter.value = false;
        filter.onChanged(false);
        break;
    }
    this.persistCurrentFilters();
  }

  constructor() {
    effect(() => {
      const filters: readonly FilterOption[] = this.filters();
      const prefix: string = this.storagePrefix();
      this.restoreFilters(prefix, filters);
    });

    effect(() => {
      this.activeFilterCount();
      this.activeFilterCountChange.emit(this.activeFilterCount());
    });
  }

  protected onToggleChanged(filter: FilterOption, event: MatSlideToggleChange): void {
    if (filter.type !== FilterOptionType.SWITCH) {
      return;
    }

    filter.value = event.checked;
    filter.onChanged(event.checked);
    this.persistCurrentFilters();
  }

  protected onSelectChanged(filter: FilterOption, event: MatSelectChange): void {
    if (filter.type !== FilterOptionType.SELECT) {
      return;
    }

    filter.selectedValue = event.value;
    filter.onChanged(event.value);
    this.persistCurrentFilters();
  }

  protected onMultiSelectChanged(filter: FilterOption, selectedValues: string[]): void {
    if (filter.type !== FilterOptionType.MULTISELECT) {
      return;
    }

    filter.selectedValues = selectedValues;
    filter.onChanged(selectedValues);
    this.persistCurrentFilters();
  }

  protected onDateTimeRangeChanged(filter: FilterOption, value: DateRangeTimeValue): void {
    if (filter.type !== FilterOptionType.DATE_TIME_RANGE) {
      return;
    }

    filter.startDate = value.startDate;
    filter.endDate = value.endDate;
    filter.onChanged({ startDate: value.startDate, endDate: value.endDate });
    this.persistCurrentFilters();
  }

  protected onDateOnlyStartChanged(filter: FilterOption, event: Event): void {
    if (filter.type !== FilterOptionType.DATE_RANGE) {
      return;
    }

    const target = event.target as HTMLInputElement;
    filter.startDate = target.value;
    filter.onChanged({ startDate: target.value, endDate: filter.endDate });
    this.persistCurrentFilters();
  }

  protected onDateOnlyEndChanged(filter: FilterOption, event: Event): void {
    if (filter.type !== FilterOptionType.DATE_RANGE) {
      return;
    }

    const target = event.target as HTMLInputElement;
    filter.endDate = target.value;
    filter.onChanged({ startDate: filter.startDate, endDate: target.value });
    this.persistCurrentFilters();
  }

  protected onRangeMinChanged(filter: FilterOption, event: Event): void {
    if (filter.type !== FilterOptionType.NUMBER_RANGE) {
      return;
    }

    const target = event.target as HTMLInputElement;
    const val: number | null = target.value === '' ? null : Number(target.value);
    filter.minValue = val;
    filter.onChanged({ minValue: val, maxValue: filter.maxValue });
    this.persistCurrentFilters();
  }

  protected onRangeMaxChanged(filter: FilterOption, event: Event): void {
    if (filter.type !== FilterOptionType.NUMBER_RANGE) {
      return;
    }

    const target = event.target as HTMLInputElement;
    const val: number | null = target.value === '' ? null : Number(target.value);
    filter.maxValue = val;
    filter.onChanged({ minValue: filter.minValue, maxValue: val });
    this.persistCurrentFilters();
  }

  private restoreFilters(prefix: string, filters: readonly FilterOption[]): void {
    const storageKey: string = this.storageKey(prefix);
    const storedState: StoredFilterState = this.readStoredState(storageKey);
    const migrateDefaults = storedState[PageFilterWidget.EMPTY_MULTISELECT_DEFAULTS_KEY] !== true;
    const nextState: StoredFilterState = { [PageFilterWidget.EMPTY_MULTISELECT_DEFAULTS_KEY]: true };

    for (const filter of filters) {
      if (filter.persist === false) {
        continue;
      }

      const storedValue: StoredFilterValue | undefined = storedState[filter.id];

      if (storedValue !== undefined) {
        this.restorePersistentFilter(filter, storedValue, migrateDefaults && storedState[`${filter.id}:exclude`] !== true);
        nextState[filter.id] = this.currentlySelectedValue(filter, storedValue);
      }
      if (filter.supportsExclusion) {
        const key = `${filter.id}:exclude`;
        if (typeof storedState[key] === 'boolean') {
          filter.exclude = storedState[key] as boolean;
        }
        nextState[key] = filter.exclude ?? false;
      }
    }

    this.writeStoredState(storageKey, nextState);
    this.filterChangeTick.update((n) => n + 1);
  }

  /**
   * The value that replaces a stored one on restore. A multiselect always writes back its normalised
   * selection; a validated select writes back the validated one, so a value an update removed migrates
   * out of storage on first sight instead of being repaired on every later visit. Every other filter
   * keeps its stored value untouched.
   */
  private currentlySelectedValue(filter: FilterOption, storedValue: StoredFilterValue): StoredFilterValue {
    if (filter.type === FilterOptionType.MULTISELECT) {
      return [...filter.selectedValues];
    }
    if (filter.type === FilterOptionType.SELECT && filter.validateRestoredValue) {
      return filter.selectedValue;
    }
    return storedValue;
  }

  private restorePersistentFilter(filter: FilterOption, storedValue: StoredFilterValue, migrateDefaults: boolean): void {
    if (filter.type === FilterOptionType.SWITCH) {
      filter.value = storedValue === true || storedValue === 'true';
    }

    if (filter.type === FilterOptionType.MULTISELECT) {
      if (typeof storedValue === 'string') {
        const value = filter.normalizeValue?.(storedValue) ?? storedValue;
        filter.selectedValues = filter.options.some((option) => option.value === value) ? [value] : [];
        return;
      }
      if (!Array.isArray(storedValue) || !storedValue.every((value) => typeof value === 'string')) return;
      const wasAll =
        storedValue.includes(DEFAULT_FILTER_ALL) ||
        (filter.options.length > 0 &&
          storedValue.length === filter.options.length &&
          filter.options.every((option) => storedValue.includes(option.value)));
      filter.selectedValues =
        migrateDefaults && wasAll ? [] : [...new Set(storedValue.map((value) => filter.normalizeValue?.(value) ?? value))];
    }

    if (filter.type === FilterOptionType.SELECT) {
      const known = typeof storedValue === 'string' && filter.options.some((option) => option.value === storedValue);
      filter.selectedValue = !filter.validateRestoredValue || known ? (storedValue as string) : (filter.options[0]?.value ?? null);
    }

    if (filter.type === FilterOptionType.NUMBER_RANGE) {
      this.restoreNumberRangeFilter(filter, storedValue as StoredNumberRangeValue);
    }

    if (filter.type === FilterOptionType.DATE_RANGE) {
      this.restoreDateRangeFilter(filter, storedValue as StoredDateRangeValue);
    }

    if (filter.type === FilterOptionType.DATE_TIME_RANGE) {
      this.restoreDateTimetRangeFilter(filter, storedValue as StoredDateTimeRangeValue);
    }
  }

  private restoreNumberRangeFilter(filter: NumberRangeFilterOption, storedValue: StoredNumberRangeValue): void {
    let desiredMin: number | null = filter.minValue;
    let desiredMax: number | null = filter.maxValue;

    if (typeof storedValue === 'object' && storedValue !== null && 'minValue' in storedValue && 'maxValue' in storedValue) {
      desiredMin = storedValue.minValue;
      desiredMax = storedValue.maxValue;
    }

    filter.minValue = desiredMin;
    filter.maxValue = desiredMax;
  }

  private restoreDateRangeFilter(filter: DateRangeFilterOption, storedValue: StoredDateRangeValue): void {
    let desiredStart: string | null = filter.startDate;
    let desiredEnd: string | null = filter.endDate;

    if (typeof storedValue === 'object' && storedValue !== null && 'startDate' in storedValue && 'endDate' in storedValue) {
      desiredStart = storedValue.startDate;
      desiredEnd = storedValue.endDate;
    }

    filter.startDate = desiredStart;
    filter.endDate = desiredEnd;
  }

  private restoreDateTimetRangeFilter(filter: DateTimeRangeFilterOption, storedValue: StoredDateTimeRangeValue): void {
    let desiredStart: Date | null = filter.startDate;
    let desiredEnd: Date | null = filter.endDate;

    if (storedValue !== null && 'startDate' in storedValue && 'endDate' in storedValue) {
      desiredStart = storedValue.startDate ? new Date(storedValue.startDate) : null;
      desiredEnd = storedValue.endDate ? new Date(storedValue.endDate) : null;
    }

    filter.startDate = desiredStart;
    filter.endDate = desiredEnd;
  }

  private persistCurrentFilters(): void {
    this.filterChangeTick.update((n) => n + 1);
    this.writeStoredState(this.storageKey(this.storagePrefix()), this.buildCurrentState(this.filters()));
  }

  private buildCurrentState(filters: readonly FilterOption[]): StoredFilterState {
    const payload: StoredFilterState = { [PageFilterWidget.EMPTY_MULTISELECT_DEFAULTS_KEY]: true };

    for (const filter of filters) {
      if (filter.persist === false) {
        continue;
      }

      if (filter.supportsExclusion) {
        payload[`${filter.id}:exclude`] = filter.exclude ?? false;
      }

      switch (filter.type) {
        case FilterOptionType.SWITCH:
          payload[filter.id] = filter.value;
          break;
        case FilterOptionType.NUMBER_RANGE:
          payload[filter.id] = { minValue: filter.minValue, maxValue: filter.maxValue };
          break;
        case FilterOptionType.SELECT:
          payload[filter.id] = filter.selectedValue;
          break;
        case FilterOptionType.MULTISELECT:
          payload[filter.id] = filter.selectedValues;
          break;
        case FilterOptionType.DATE_RANGE:
          payload[filter.id] = { startDate: filter.startDate, endDate: filter.endDate };
          break;
        case FilterOptionType.DATE_TIME_RANGE:
          payload[filter.id] = {
            startDate: filter.startDate ? filter.startDate.toISOString() : null,
            endDate: filter.endDate ? filter.endDate.toISOString() : null,
          };
          break;
      }
    }

    return payload;
  }

  private readStoredState(storageKey: string): StoredFilterState {
    const storedValue: string | null = globalThis.localStorage.getItem(storageKey);
    if (storedValue === null) {
      return {};
    }

    try {
      const parsed: unknown = JSON.parse(storedValue);
      return parsed && typeof parsed === 'object' ? (parsed as StoredFilterState) : {};
    } catch {
      return {};
    }
  }

  private writeStoredState(storageKey: string, state: StoredFilterState): void {
    globalThis.localStorage.setItem(storageKey, JSON.stringify(state));
  }

  private storageKey(prefix: string): string {
    return `filters:${prefix}`;
  }
}
