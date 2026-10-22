import { SonarrLibrarySeriesTable } from './sonarr-library-series-table';

export interface SonarrLibraryTable {
  instanceName: string;
  instanceId: string;
  syncedAt: string | null;
  series: SonarrLibrarySeriesTable[];
}
