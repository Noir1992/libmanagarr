import { LibraryMediaDetails } from './library-media-details';

export interface RadarrLibraryEntryResponse {
  instanceName: string;
  movieId: number;
  movieTitle: string;
  movieTitleSlug: string;
  qualityProfileName: string;
  upgradeUntilCustomFormatScore?: number | null;
  status: string;
  addedAt?: string | null;
  airDateUtc: string | null;
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
