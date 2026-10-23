import { LibraryMediaDetails } from './library-media-details';

export interface SonarrLibraryEpisodeTable {
  number: number;
  hasFile?: boolean;
  monitored?: boolean;
  title: string;
  customFormatScore: number | null;
  airDateUtc: string | null;
  hasAired?: boolean;
  mediaDetails?: LibraryMediaDetails | null;
  episodeId?: number;
}
