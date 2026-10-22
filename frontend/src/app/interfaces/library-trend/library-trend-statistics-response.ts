import { LibraryTrendPoint } from './library-trend-point';
import { LibraryChangeHistoryEntry } from './library-change-history-entry';

export interface LibraryTrendStatisticsResponse {
  points: LibraryTrendPoint[];
  history: LibraryChangeHistoryEntry[];
  totalMediaFileCount: number;
  totalSizeBytes: number;
  availableFrom: string | null;
  availableTo: string | null;
}
