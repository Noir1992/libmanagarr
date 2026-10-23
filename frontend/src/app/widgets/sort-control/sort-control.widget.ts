import { ChangeDetectionStrategy, Component, computed, inject, input, InputSignal, output, OutputEmitterRef, Signal } from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { LibrarySortDirection } from '../../enums/library-sort-direction';
import { LibrarySortField } from '../../enums/library-sort-field';
import { AppI18nService } from '../../services/app-i18n.service';

type SortFieldOption = {
  value: LibrarySortField;
  label: string;
};

@Component({
  selector: 'app-sort-control',
  imports: [MatIconButton, MatMenuModule],
  templateUrl: './sort-control.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './sort-control.widget.scss',
})
export class SortControlWidget {
  private readonly i18n: AppI18nService = inject(AppI18nService);

  readonly sortField: InputSignal<LibrarySortField> = input.required<LibrarySortField>();
  readonly sortDirection: InputSignal<LibrarySortDirection> = input.required<LibrarySortDirection>();

  readonly sortFieldChange: OutputEmitterRef<LibrarySortField> = output<LibrarySortField>();
  readonly sortDirectionChange: OutputEmitterRef<LibrarySortDirection> = output<LibrarySortDirection>();

  protected readonly labels = computed(() => ({
    title: this.i18n.t('sort.title'),
  }));

  protected readonly options: Signal<readonly SortFieldOption[]> = computed((): readonly SortFieldOption[] => [
    { value: LibrarySortField.ALPHABETICAL, label: this.i18n.t('sort.field.alphabetical') },
    { value: LibrarySortField.AIRED, label: this.i18n.t('sort.field.aired') },
    { value: LibrarySortField.ADDED, label: this.i18n.t('sort.field.added') },
  ]);

  protected readonly directionIcon: Signal<string> = computed((): string =>
    this.sortDirection() === LibrarySortDirection.ASCENDING ? 'arrow_upward' : 'arrow_downward',
  );

  // Picking another field sorts ascending; re-picking the active field flips its direction.
  protected onFieldPicked(field: LibrarySortField): void {
    if (field === this.sortField()) {
      this.sortDirectionChange.emit(
        this.sortDirection() === LibrarySortDirection.ASCENDING ? LibrarySortDirection.DESCENDING : LibrarySortDirection.ASCENDING,
      );
      return;
    }

    this.sortFieldChange.emit(field);
    if (this.sortDirection() !== LibrarySortDirection.ASCENDING) {
      this.sortDirectionChange.emit(LibrarySortDirection.ASCENDING);
    }
  }
}
