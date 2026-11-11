import { LibraryMediaDetails } from '../library/library-media-details';

export interface CodecMediaFile {
  title: string;
  source: string;
  instanceName: string;
  titleSlug: string | null;
  sizeBytes: number | null;
  relativePath: string | null;
  rootTitle: string;
  seasonNumber?: number | null;
  episodeNumber?: number | null;
  monitored?: boolean | null;
  airDateUtc?: string | null;
  status?: string | null;
  addedAt?: string | null;
  qualityProfileName?: string | null;
  upgradeUntilCustomFormatScore?: number | null;
  posterUrl?: string | null;
  genres?: string[] | null;
  tags?: string[] | null;
  customFormatScore?: number | null;
  mediaDetails?: LibraryMediaDetails | null;
}
