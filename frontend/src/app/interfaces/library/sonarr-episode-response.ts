import { LibraryMediaDetails } from './library-media-details';

export interface SonarrEpisodeResponse {
  number: number;
  monitored?: boolean;
  title: string;
  airDateUtc: string | null;
  customFormatScore: number | null;
  hasFile: boolean;
  hasAired?: boolean;
  mediaDetails?: LibraryMediaDetails | null;
  episodeId?: number;
}
