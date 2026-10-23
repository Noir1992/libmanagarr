import { Component, computed, inject, Signal, ChangeDetectionStrategy } from '@angular/core';
import { MatIcon } from '@angular/material/icon';
import { AppI18nService } from '../../../services/app-i18n.service';

@Component({
  selector: 'app-loading-section',
  imports: [MatIcon],
  templateUrl: './loading-section.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './loading-section.widget.scss',
})
export class LoadingSectionWidget {
  private readonly i18n: AppI18nService = inject(AppI18nService);

  protected readonly loadingText: Signal<string> = computed((): string => this.i18n.t('common.loading'));
}
