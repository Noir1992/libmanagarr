import { CodecStatisticItem } from '../../interfaces/codec-statistic/codec-statistic-item';
import { StatisticsChartCategory, StatisticsChartItem } from '../../interfaces/codec-statistic/statistics-chart-item';
import { MediaTreeNode } from '../../interfaces/media-tree/media-tree-node';

export type CodecStatisticsChartSlice = CodecStatisticItem &
  StatisticsChartItem & {
    treeRoots: MediaTreeNode[];
  };

export type CodecStatisticsChartCategory = StatisticsChartCategory<CodecStatisticsChartSlice>;
