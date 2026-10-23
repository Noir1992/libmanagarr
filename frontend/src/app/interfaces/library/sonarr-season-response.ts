import { SonarrEpisodeResponse } from './sonarr-episode-response';

export interface SonarrSeasonResponse {
  number: number;
  monitored: boolean;
  episodes: SonarrEpisodeResponse[];
}
