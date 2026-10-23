import { SonarrLibrarySeasonTable } from './sonarr-library-season-table';

export interface SonarrLibrarySeriesTable {
  url: string;
  id: number;
  title: string;
  titleSlug: string;
  qualityProfileName: string;
  upgradeUntilCustomFormatScore?: number | null;
  status: string;
  addedAt?: string | null;
  isAvailable?: boolean;
  monitored: boolean;
  seasons: SonarrLibrarySeasonTable[];
  affectedSeasons: number;
  affectedEpisodes: number;
  genres?: string[];
  tags?: string[];
  libraryEpisodeCount?: number;
  fileCount?: number;
}
