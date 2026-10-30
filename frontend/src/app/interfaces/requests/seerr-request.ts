export type SeerrRequestStatus = 'pending' | 'approved' | 'declined' | 'processing' | 'available' | 'failed' | 'unknown';

export type SeerrRequestType = 'movie' | 'tv';

export interface SeerrRequest {
  id: string;
  status: SeerrRequestStatus;
  is4kRequest: boolean;
  type: SeerrRequestType | null;
  requestedBy: string | null;
  requestedAt: string | null;
  title: string | null;
  posterPath: string | null;
  year: number | null;
  // Sorted distinct season numbers for tv requests; null for movies and whole-series requests.
  seasons: number[] | null;
}
