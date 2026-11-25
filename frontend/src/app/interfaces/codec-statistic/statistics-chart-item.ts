import { MediaTreeNode } from '../media-tree/media-tree-node';

export interface StatisticsChartItem {
  label: string;
  count: number;
  percentage: number;
  color: string;
  dashArray: string;
  dashOffset: string;
  tooltip: string;
  metaText: string;
  metaTooltip?: string | null;
}

export interface StatisticsChartExpandedItem extends StatisticsChartItem {
  treeRoots: readonly MediaTreeNode[];
}

export interface StatisticsChartCategory<TItem extends StatisticsChartItem = StatisticsChartItem> {
  key: string;
  title: string;
  items: readonly TItem[];
}
