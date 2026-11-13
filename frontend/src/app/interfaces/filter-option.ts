import { FilterOptionType } from '../enums/filter-option-type';

export const DEFAULT_FILTER_ALL: string = '__all__';

export type FilterOption =
  | SwitchFilterOption
  | SelectFilterOption
  | MultiSelectFilterOption
  | DateRangeFilterOption
  | DateTimeRangeFilterOption
  | NumberRangeFilterOption;

interface FilterOptionBase {
  id: string;
  label: string;
  type: FilterOptionType;
  active?: boolean;
  persist?: boolean;
  supportsExclusion?: boolean;
  exclude?: boolean;
  excludeAllRestricts?: boolean;
  totalCount?: number;
}

export type FilterSelectOption = {
  value: string | null;
  label: string;
  count?: number;
};

export type FilterMultiSelectOption = {
  value: string;
  label: string;
  count?: number;
};

export interface SwitchFilterOption extends FilterOptionBase {
  type: FilterOptionType.SWITCH;
  value: boolean;
  onChanged: (value: boolean) => void;
}

export interface SelectFilterOption extends FilterOptionBase {
  type: FilterOptionType.SELECT;
  options: readonly FilterSelectOption[];
  selectedValue: string | null;
  onChanged: (values: string) => void;
  /**
   * For selects whose options are complete at restore time: a stored value an update removed
   * falls back to the first option instead of filtering every row away in silence.
   */
  validateRestoredValue?: boolean;
}

export interface MultiSelectFilterOption extends FilterOptionBase {
  normalizeValue?: (value: string) => string;
  type: FilterOptionType.MULTISELECT;
  options: readonly FilterMultiSelectOption[];
  selectedValues: string[];
  onChanged: (values: string[]) => void;
}

export interface DateRangeFilterOption extends FilterOptionBase {
  type: FilterOptionType.DATE_RANGE;
  startDate: string | null;
  endDate: string | null;
  onChanged: (value: { startDate: string | null; endDate: string | null }) => void;
}

export interface DateTimeRangeFilterOption extends FilterOptionBase {
  type: FilterOptionType.DATE_TIME_RANGE;
  startDate: Date | null;
  endDate: Date | null;
  onChanged: (value: { startDate: Date | null; endDate: Date | null }) => void;
}

export interface NumberRangeFilterOption extends FilterOptionBase {
  type: FilterOptionType.NUMBER_RANGE;
  minimum?: number;
  step?: number | 'any';
  minValue: number | null;
  maxValue: number | null;
  onChanged: (value: { minValue: number | null; maxValue: number | null }) => void;
}
