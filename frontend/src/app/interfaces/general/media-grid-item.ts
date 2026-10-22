import { MediaDetailData } from './media-detail-data';

export type MediaGridItemKind = 'movie' | 'series';

export type MediaGridDetailTone = 'success' | 'warning' | 'danger' | 'info';

export interface MediaGridItem {
  kind: MediaGridItemKind;
  id: number;
  title: string;
  url: string;
  posterUrl: string | null;
  detailRows: { label: string; value: string; tone?: MediaGridDetailTone }[];
  movieData?: MediaDetailData;
  seriesData?: {
    seasons: {
      number: number;
      monitored?: boolean;
      episodes: {
        details?: MediaDetailData;
        number: number;
        title?: string;
        hasFile?: boolean;
        monitored?: boolean;
        customFormatScore?: number | null;
        airDateUtc?: string | null;
      }[];
    }[];
    qualityProfileName?: string;
    instanceName?: string;
    status?: string;
    addedAt?: string | null;
    isAvailable?: boolean;
    monitored?: boolean;
    genres?: string[];
    tags?: string[];
    upgradeUntilCustomFormatScore?: number | null;
  };
}
