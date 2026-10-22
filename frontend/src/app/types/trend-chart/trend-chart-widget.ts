import { TrendChartSeries } from './trend-chart';

export type TrendChartWidgetTooltipState = {
  chartId: TrendChartSeries;
  x: number;
  y: number;
  pointX: number;
  pointY: number;
  lines: readonly string[];
} | null;
