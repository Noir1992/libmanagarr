import { FilterOptionType } from '../enums/filter-option-type';
import { DEFAULT_FILTER_ALL, MultiSelectFilterOption } from '../interfaces/filter-option';
import { multiSelectFilterValues, multiSelectInstanceFilterValues } from './filter-utils';

describe('multiselect instance requests', () => {
  function filter(selectedValues: string[]): MultiSelectFilterOption {
    return {
      id: 'instances',
      label: 'Instances',
      type: FilterOptionType.MULTISELECT,
      options: [
        { value: 'first', label: 'First' },
        { value: 'second', label: 'Second' },
      ],
      selectedValues,
      onChanged: () => {},
    };
  }

  for (const selection of [[], [DEFAULT_FILTER_ALL]]) {
    it(`resolves ${JSON.stringify(selection)} to every configured instance without changing the UI selection`, () => {
      const option = filter(selection);
      expect(multiSelectInstanceFilterValues('instances', [option])).toEqual(['first', 'second']);
      expect(option.selectedValues).toEqual(selection);
      expect(multiSelectFilterValues('instances', [option])).toEqual(selection);
    });
  }

  it('keeps a partial instance selection and returns no IDs when no instances exist', () => {
    const option = filter(['second']);
    expect(multiSelectInstanceFilterValues('instances', [option])).toEqual(['second']);
    option.selectedValues = [];
    option.options = [];
    expect(multiSelectInstanceFilterValues('instances', [option])).toEqual([]);
  });
});
