import { Component, computed, inject, input, InputSignal, Signal, ChangeDetectionStrategy } from '@angular/core';
import { AppI18nService } from '../../../services/app-i18n.service';

@Component({
  selector: 'app-no-data-section',
  imports: [],
  templateUrl: './no-data-section.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './no-data-section.widget.scss',
})
export class NoDataSectionWidget {
  private readonly i18n: AppI18nService = inject(AppI18nService);

  readonly isFiltered: InputSignal<boolean> = input.required<boolean>();

  protected readonly noDataForFilterText: Signal<string> = computed((): string => this.i18n.t('common.noDataForFilter'));
  protected readonly noDataText: Signal<string> = computed((): string => this.i18n.t('common.noData'));
}
