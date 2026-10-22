import { SonarrLibraryEpisodeTable } from '../library/sonarr-library-episode-table';
import { SonarrLibrarySeasonTable } from '../library/sonarr-library-season-table';
import { SonarrLibrarySeriesTable } from '../library/sonarr-library-series-table';

export type SonarrTableSeriesType = SonarrLibrarySeriesTable;
export type SonarrTableSeasonType = SonarrLibrarySeasonTable;
export type SonarrTableEpisodeType = SonarrLibraryEpisodeTable;

export interface SonarrTableType {
  instanceName: string;
  instanceId: string;
  syncedAt: string | null;
  series: SonarrTableSeriesType[];
}
