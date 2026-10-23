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
  ViewChild,
  WritableSignal,
} from '@angular/core';
import { MatSelect, MatSelectChange, MatSelectModule } from '@angular/material/select';
import { DEFAULT_FILTER_ALL, FilterMultiSelectOption, FilterSelectOption } from '../../interfaces/filter-option';
import { AppI18nService } from '../../services/app-i18n.service';

@Component({
  selector: 'app-multiselect',
  imports: [MatSelectModule],
  templateUrl: './multiselect.html',
  styleUrl: './multiselect.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Multiselect {
  private readonly i18n: AppI18nService = inject(AppI18nService);

  protected readonly DEFAULT_FILTER_ALL: string = DEFAULT_FILTER_ALL;

  protected readonly allLabel: Signal<string> = computed((): string => this.i18n.t('filter.all'));

  @ViewChild('select')
  private readonly select?: MatSelect;

  readonly showSelectAll: InputSignal<boolean> = input<boolean>(true);
  readonly ariaLabel: InputSignal<string> = input('');
  readonly totalCount: InputSignal<number | undefined> = input<number>();
  readonly options: InputSignal<readonly FilterMultiSelectOption[]> = input.required<readonly FilterMultiSelectOption[]>();
  readonly selectedValues: InputSignal<string[]> = input<string[]>([]);
  readonly previousSelectedValues: WritableSignal<string[]> = signal<string[]>([]);

  readonly selectedValuesChange: OutputEmitterRef<string[]> = output<string[]>();

  protected readonly selectedValuesInternal: WritableSignal<string[]> = signal<string[]>([]);
  protected isAllSelectedInternal: Signal<boolean> = computed(
    (): boolean =>
      this.selectedValuesInternal().includes(DEFAULT_FILTER_ALL) ||
      (this.options().length > 0 &&
        this.options().every((option: FilterMultiSelectOption): boolean => this.selectedValuesInternal().includes(option.value))),
  );

  constructor() {
    effect((): void => {
      const incoming: string[] = this.selectedValues();
      const opts: readonly FilterMultiSelectOption[] = this.options();

      if (incoming[0] === DEFAULT_FILTER_ALL) {
        this.selectedValuesInternal.set([...opts.map((o) => o.value), DEFAULT_FILTER_ALL]);
      } else {
        this.selectedValuesInternal.set([...incoming]);
      }

      this.previousSelectedValues.set(this.selectedValuesInternal());
    });
  }

  protected onSelectionChange(event: MatSelectChange): void {
    const eventValues: string[] = event.value ?? [];
    const hasAll: boolean = eventValues.includes(DEFAULT_FILTER_ALL);
    const hasAllPrevious: boolean = this.previousSelectedValues().includes(DEFAULT_FILTER_ALL);
    const allOptionValues: string[] = this.options().map((o) => o.value);

    if (hasAll && !hasAllPrevious) {
      this.selectedValuesInternal.set([...allOptionValues, DEFAULT_FILTER_ALL]);
      this.selectedValuesChange.emit([DEFAULT_FILTER_ALL]);
    } else if (!hasAll && hasAllPrevious) {
      this.selectedValuesInternal.set([]);
      this.selectedValuesChange.emit([]);
    } else {
      if (hasAllPrevious) {
        this.selectedValuesInternal.set(eventValues.filter((v) => v !== DEFAULT_FILTER_ALL));
      } else {
        this.selectedValuesInternal.set(eventValues);
      }

      this.selectedValuesChange.emit(this.selectedValuesInternal());
    }

    this.previousSelectedValues.set(this.selectedValuesInternal());
  }

  protected itemLabel(value: string): string {
    return this.options().find((o: FilterSelectOption): boolean => o.value === value)?.label ?? value;
  }
}
