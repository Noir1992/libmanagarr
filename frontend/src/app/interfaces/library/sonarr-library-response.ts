import { SonarrLibraryEntryResponse } from './sonarr-library-entry-response';

export interface SonarrLibraryResponse {
  syncedAt: string | null;
  rows: SonarrLibraryEntryResponse[];
}
