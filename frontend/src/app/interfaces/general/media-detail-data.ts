import { LibraryMediaDetails } from '../library/library-media-details';

export interface MediaDetailData {
  title: string;
  url: string;
  posterUrl?: string | null;
  instanceName: string;
  seriesTitle?: string;
  seasonNumber?: number;
  episodeNumber?: number;
  status?: string;
  addedAt?: string | null;
  qualityProfileName?: string;
  upgradeUntilCustomFormatScore?: number | null;
  customFormatScore?: number | null;
  monitored?: boolean;
  seriesMonitored?: boolean;
  hasFile?: boolean;
  hasAired?: boolean;
  airDateUtc?: string | null;
  genres?: string[];
  tags?: string[];
  size?: number | null;
  mediaDetails?: LibraryMediaDetails | null;
}
