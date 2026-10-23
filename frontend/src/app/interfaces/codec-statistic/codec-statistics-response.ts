import { CodecMediaFile } from './codec-media-file';
import { CodecStatisticItem } from './codec-statistic-item';
import { LibraryFilterCounts, LibraryFilterValues } from '../library/library-filter-values';

export interface CodecStatisticsResponse {
  containerFormats: CodecStatisticItem[];
  resolutions: CodecStatisticItem[];
  dynamicRanges: CodecStatisticItem[];
  videoCodecs: CodecStatisticItem[];
  audioCodecs: CodecStatisticItem[];
  videoBitrates: CodecStatisticItem[];
  audioLanguages: CodecStatisticItem[];
  subtitleLanguages: CodecStatisticItem[];
  files?: CodecMediaFile[];
  filterValues?: LibraryFilterValues;
  counts?: LibraryFilterCounts;
}
