export type TrendChartSeries = 'media' | 'storage';

export type TrendChartPoint = {
  x: number;
  y: number;
  color: string;
  tooltipLines: readonly string[];
  occurredAt: string;
};

export type TrendChartTick = {
  y: number;
  label: string;
};

export type TrendChartLabel = {
  x: number;
  label: string;
  anchor: 'start' | 'middle' | 'end';
};

export type TrendChartModel = {
  points: readonly TrendChartPoint[];
  linePath: string;
  areaPath: string | null;
  yTicks: readonly TrendChartTick[];
  xLabels: readonly TrendChartLabel[];
};
