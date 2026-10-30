export interface SeerrEditOption {
  id: number;
  name: string;
}

export type SeerrSeasonState = 'none' | 'pending' | 'processing' | 'available';

export interface SeerrSeasonOption {
  number: number;
  episodeCount: number | null;
  state: SeerrSeasonState;
}

export interface SeerrRequestEditOptions {
  qualityProfiles: SeerrEditOption[];
  rootFolders: string[];
  seasons: SeerrSeasonOption[];
  profileId: number | null;
  rootFolder: string | null;
}
