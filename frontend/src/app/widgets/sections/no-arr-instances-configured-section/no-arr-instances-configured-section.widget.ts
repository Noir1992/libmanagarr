import { Component, computed, inject, Signal, ChangeDetectionStrategy } from '@angular/core';
import { MatIcon } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { AppI18nService } from '../../../services/app-i18n.service';

@Component({
  selector: 'app-no-arr-instances-configured-section',
  imports: [MatIcon, RouterLink],
  templateUrl: './no-arr-instances-configured-section.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './no-arr-instances-configured-section.widget.scss',
})
export class NoArrInstancesConfiguredSectionWidget {
  private readonly i18n: AppI18nService = inject(AppI18nService);

  protected readonly noArrInstancesText: Signal<string> = computed((): string => this.i18n.t('common.noArrInstances'));
  protected readonly goToArrInstanceSettingsText: Signal<string> = computed((): string => this.i18n.t('common.goToArrInstanceSettings'));
}
