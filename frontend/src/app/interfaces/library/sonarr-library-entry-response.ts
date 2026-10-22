import { SonarrSeasonResponse } from './sonarr-season-response';

export interface SonarrLibraryEntryResponse {
  instanceName: string;
  seriesId: number;
  seriesTitle: string;
  seriesTitleSlug: string;
  qualityProfileName: string;
  upgradeUntilCustomFormatScore?: number | null;
  status: string;
  addedAt?: string | null;
  isAvailable?: boolean;
  monitored: boolean;
  seasons: SonarrSeasonResponse[];
  totalEpisodes: number;
  airedEpisodes: number;
  genres?: string[];
  tags?: string[];
  libraryEpisodeCount?: number;
  fileCount?: number;
}
