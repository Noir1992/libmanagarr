import { LibraryFilterCounts } from './library-filter-values';
import { SonarrLibraryEntryResponse } from './sonarr-library-entry-response';

export interface SonarrLibraryBatchResponse {
  counts?: LibraryFilterCounts;
  syncedAt: string | null;
  instances: SonarrLibraryInstanceResult[];
}

export interface SonarrLibraryInstanceResult {
  instanceId: string;
  instanceName: string;
  rows: SonarrLibraryEntryResponse[];
}
