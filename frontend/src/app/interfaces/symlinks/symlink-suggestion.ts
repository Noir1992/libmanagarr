import { SymlinkSuggestionStatus } from '../../enums/symlink-suggestion-status';

/** Why a pair matched: the code the view labels, plus the sentence that produced it. */
export interface SymlinkSuggestionReason {
  code: string;
  detail: string;
}

/** A competing source a person has to choose between, so equal titles never decide alone. */
export interface SymlinkSuggestionAlternative {
  radarrInstanceId: string;
  radarrInstanceName?: string | null;
  radarrMovieId: number;
  radarrFileId: number | null;
  detail: string | null;
}

/** A detected pair with the titles the current snapshots have for its ids. */
export interface SymlinkSuggestion {
  id: string;
  matcherVersion: string;
  status: SymlinkSuggestionStatus;
  radarrInstanceId: string;
  radarrInstanceName?: string | null;
  radarrMovieId: number;
  radarrMovieTitle: string | null;
  radarrFileId: number | null;
  radarrFilePath: string | null;
  sonarrInstanceId: string;
  sonarrInstanceName?: string | null;
  sonarrSeriesId: number;
  sonarrSeriesTitle: string | null;
  sonarrSeriesPath?: string | null;
  sonarrEpisodeId: number;
  sonarrEpisodeTitle: string | null;
  seasonNumber: number;
  episodeNumber: number;
  reasons: SymlinkSuggestionReason[];
  existingFileCheck?: ExistingEpisodeFileCheck | null;
  alternatives: SymlinkSuggestionAlternative[];
  detail: string | null;
  configRevision: number;
  stale: boolean;
  linkId: string | null;
  revision: number;
  createdAt: string;
  updatedAt: string;
  decidedAt: string | null;
}

export interface ExistingEpisodeFileCheck {
  status: 'Matches' | 'TargetMismatch' | 'BrokenSymlink' | 'RegularFile' | 'MissingFile' | 'Unavailable' | 'NoFile';
  detail: string;
  episodePath: string | null;
  expectedTarget: string | null;
  actualTarget: string | null;
  sonarrFileId?: number | null;
  linkText?: string | null;
  checkedAt: string;
}

/** What one matching pass did; a pass that changed nothing is the normal case and says so quietly. */
export interface SymlinkMatchingRun {
  instancesConsidered: number;
  created: number;
  updated: number;
  markedStale: number;
  skippedRejected: number;
  didWork: boolean;
}
