import { RadarrLibraryEntryResponse } from './radarr-library-entry-response';

export interface RadarrLibraryResponse {
  syncedAt: string | null;
  rows: RadarrLibraryEntryResponse[];
}
