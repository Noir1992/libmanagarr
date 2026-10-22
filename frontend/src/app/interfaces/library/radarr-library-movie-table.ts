import { LibraryMediaDetails } from './library-media-details';

export interface RadarrLibraryMovieTable {
  url: string;
  id: number;
  title: string;
  titleSlug: string;
  status: string;
  addedAt?: string | null;
  airDateUtc: string | null;
  qualityProfileName: string;
  upgradeUntilCustomFormatScore?: number | null;
  monitored: boolean;
  hasFile: boolean;
  customFormatScore: number | null;
  resolution: string | null;
  size: number | null;
  hasAired?: boolean;
  mediaDetails?: LibraryMediaDetails | null;
  genres?: string[];
  tags?: string[];
}
