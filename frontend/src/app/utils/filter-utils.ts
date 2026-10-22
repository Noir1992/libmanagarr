import {
  DateRangeFilterOption,
  DEFAULT_FILTER_ALL,
  FilterOption,
  MultiSelectFilterOption,
  NumberRangeFilterOption,
  SelectFilterOption,
  SwitchFilterOption,
} from '../interfaces/filter-option';
import { FilterOptionType } from '../enums/filter-option-type';

export function switchFilterValue(id: string, filters: readonly FilterOption[]): boolean {
  return filterOption<SwitchFilterOption>(id, FilterOptionType.SWITCH, filters).value;
}

export function selectFilterValue(id: string, filters: readonly FilterOption[]): string | undefined {
  const filter: SelectFilterOption = filterOption<SelectFilterOption>(id, FilterOptionType.SELECT, filters);
  return filter.selectedValue ?? undefined;
}

export function multiSelectFilterValues(id: string, filters: readonly FilterOption[]): string[] {
  return [...filterOption<MultiSelectFilterOption>(id, FilterOptionType.MULTISELECT, filters).selectedValues];
}

export function multiSelectInstanceFilterValues(id: string, filters: readonly FilterOption[]): string[] {
  const filter = filterOption<MultiSelectFilterOption>(id, FilterOptionType.MULTISELECT, filters);
  return filter.selectedValues.length === 0 || filter.selectedValues.includes(DEFAULT_FILTER_ALL)
    ? filter.options.map((option) => option.value)
    : [...filter.selectedValues];
}

export function numberRangeFilter(id: string, filters: readonly FilterOption[]): NumberRangeFilterOption {
  return filterOption<NumberRangeFilterOption>(id, FilterOptionType.NUMBER_RANGE, filters);
}

export function dateRangeFilter(id: string, filters: readonly FilterOption[]): DateRangeFilterOption {
  return filterOption<DateRangeFilterOption>(id, FilterOptionType.DATE_RANGE, filters);
}

export function filterOption<T extends FilterOption>(id: string, type: FilterOptionType, filters: readonly FilterOption[]): T {
  const filter: FilterOption | undefined = filters.find((option: FilterOption): boolean => option.id === id);

  if (filter === undefined) {
    throw new Error(`Filter option "${id}" not found.`);
  }

  if (filter.type !== type) {
    throw new Error(`Filter option "${id}" has unexpected type "${filter.type}".`);
  }

  return filter as T;
}
