import { CodecMediaFile } from '../codec-statistic/codec-media-file';

export interface MediaTreeNode {
  key: string;
  label: string;
  isDirectory: boolean;
  children: MediaTreeNode[];
  fileCount: number;
  totalSizeBytes: number;
  source: string;
  instanceName: string;
  url?: string | null;
  file?: CodecMediaFile;
}
