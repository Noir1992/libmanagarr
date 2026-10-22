import { Component, EventEmitter, Input, Output, ChangeDetectionStrategy } from '@angular/core';
import { MatIcon } from '@angular/material/icon';
import { StatisticsChartExpandedItem, StatisticsChartItem } from '../../interfaces/codec-statistic/statistics-chart-item';
import { MediaFilesTreeWidget } from '../media-files-tree/media-files-tree.widget';

@Component({
  selector: 'app-statistics-chart-rows',
  imports: [MatIcon, MediaFilesTreeWidget],
  templateUrl: './statistics-chart-rows.widget.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './statistics-chart-rows.widget.scss',
})
export class StatisticsChartRowsWidget {
  @Input({ required: true }) categoryKey!: string;
  @Input({ required: true }) items!: readonly StatisticsChartExpandedItem[];
  @Input() expandedItems: ReadonlySet<string> = new Set<string>();

  @Output() readonly itemToggle: EventEmitter<StatisticsChartItem> = new EventEmitter<StatisticsChartItem>();
  @Output() readonly treeInteraction: EventEmitter<void> = new EventEmitter<void>();

  protected toggleItem(item: StatisticsChartItem): void {
    this.itemToggle.emit(item);
  }

  protected isExpanded(item: StatisticsChartItem): boolean {
    return this.expandedItems.has(this.toItemKey(item.label));
  }

  protected toItemKey(label: string): string {
    return `${this.categoryKey}::${label.toLowerCase()}`;
  }

  protected scopeKey(label: string): string {
    return this.toItemKey(label);
  }
}
