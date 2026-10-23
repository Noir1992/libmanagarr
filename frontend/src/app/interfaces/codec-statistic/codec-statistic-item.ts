import { CodecMediaFile } from './codec-media-file';

export interface CodecStatisticItem {
  label: string;
  count: number;
  percentage: number;
  fileIds?: number[];
  mediaFiles: CodecMediaFile[];
  totalSizeBytes: number;
}
