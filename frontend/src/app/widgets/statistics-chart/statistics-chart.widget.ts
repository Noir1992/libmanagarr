import { Component, Input, signal, WritableSignal, ChangeDetectionStrategy, computed, inject } from '@angular/core';
import { AppI18nService } from '../../services/app-i18n.service';
import { STATISTICS_CHART_RADIUS } from '../../utils/statistics-chart-geometry';
import {
  StatisticsChartCategory,
  StatisticsChartExpandedItem,
  StatisticsChartItem,
} from '../../interfaces/codec-statistic/statistics-chart-item';
import { StatisticsChartRowsWidget } from '../statistics-chart-rows/statistics-chart-rows.widget';

type HoverTooltip = {
  text: string;
  x: number;
  y: number;
};

@Component({
  selector: 'app-statistics-chart',
  imports: [StatisticsChartRowsWidget],
  templateUrl: './statistics-chart.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './statistics-chart.widget.scss',
})
export class StatisticsChartWidget {
  protected readonly radius = STATISTICS_CHART_RADIUS;
  private readonly i18n = inject(AppI18nService);
  @Input({ required: true }) category!: StatisticsChartCategory<StatisticsChartExpandedItem>;
  protected readonly rowsExpanded = signal(false);
  protected readonly rowsToggleLabel = computed(() => this.i18n.t(this.rowsExpanded() ? 'statistics.hideValues' : 'statistics.showValues'));
  protected readonly expandedItems: WritableSignal<ReadonlySet<string>> = signal(new Set<string>());
  protected readonly hoverTooltip: WritableSignal<HoverTooltip | null> = signal<HoverTooltip | null>(null);

  protected hoverStart(event: MouseEvent, item: StatisticsChartItem): void {
    this.hoverTooltip.set({
      text: item.tooltip,
      x: event.clientX,
      y: event.clientY,
    });
  }

  protected hoverMove(event: MouseEvent): void {
    const current: HoverTooltip | null = this.hoverTooltip();
    if (current === null) {
      return;
    }

    this.hoverTooltip.set({
      ...current,
      x: event.clientX,
      y: event.clientY,
    });
  }

  protected hoverEnd(): void {
    this.hoverTooltip.set(null);
  }

  protected toggleItem(item: StatisticsChartItem): void {
    this.expandedItems.update((current) => {
      const next: Set<string> = new Set(current);
      const itemKey: string = this.toItemKey(item.label);
      if (next.has(itemKey)) {
        next.delete(itemKey);
      } else {
        next.add(itemKey);
      }

      return next;
    });
  }

  protected toggleRows(): void {
    this.rowsExpanded.update((expanded) => !expanded);
    this.hideTooltip();
  }

  protected selectSegment(item: StatisticsChartItem): void {
    this.rowsExpanded.set(true);
    this.toggleItem(item);
    this.hideTooltip();
  }

  protected hideTooltip(): void {
    this.hoverTooltip.set(null);
  }

  private toItemKey(label: string): string {
    return `${this.category.key}::${label.toLowerCase()}`;
  }
}
