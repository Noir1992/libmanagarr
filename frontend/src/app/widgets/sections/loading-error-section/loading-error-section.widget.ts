import { Component, computed, inject, input, InputSignal, Signal, ChangeDetectionStrategy } from '@angular/core';
import { MatIcon } from '@angular/material/icon';
import { AppI18nService } from '../../../services/app-i18n.service';

@Component({
  selector: 'app-loading-error-section',
  imports: [MatIcon],
  templateUrl: './loading-error-section.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './loading-error-section.widget.scss',
})
export class LoadingErrorSectionWidget {
  private readonly i18n: AppI18nService = inject(AppI18nService);

  readonly error: InputSignal<string> = input.required<string>();

  protected readonly loadingErrorText: Signal<string> = computed((): string => this.i18n.t('common.loading.error'));
}
