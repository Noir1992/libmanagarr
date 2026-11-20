import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { AppI18nService } from '../../services/app-i18n.service';

@Component({
  selector: 'app-search-clear',
  imports: [MatButtonModule],
  templateUrl: './search-clear.widget.html',
  styleUrl: './search-clear.widget.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SearchClearWidget {
  private readonly i18n = inject(AppI18nService);
  readonly label = input<string | null>(null);
  readonly cleared = output<void>();
  protected readonly accessibleLabel = computed(() => this.label() ?? this.i18n.t('common.clearSearch'));
}
