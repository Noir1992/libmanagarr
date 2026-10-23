import { SonarrLibraryEpisodeTable } from './sonarr-library-episode-table';

export interface SonarrLibrarySeasonTable {
  number: number;
  monitored: boolean;
  episodes: SonarrLibraryEpisodeTable[];
}
