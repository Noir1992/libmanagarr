import { LibraryFilterCounts } from './library-filter-values';
import { RadarrLibraryEntryResponse } from './radarr-library-entry-response';

export interface RadarrLibraryBatchResponse {
  counts?: LibraryFilterCounts;
  syncedAt: string | null;
  instances: RadarrLibraryInstanceResult[];
}

export interface RadarrLibraryInstanceResult {
  instanceId: string;
  instanceName: string;
  rows: RadarrLibraryEntryResponse[];
}
