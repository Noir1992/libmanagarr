import { SymlinkOperationStatus } from '../../enums/symlink-operation-status';
import { SymlinkSetupStep } from '../../enums/symlink-setup-step';

/** File information with the instance it came from, so an unattributed value is visibly unusable. */
export interface SymlinkFileMetadata {
  origin: string | null;
  releaseGroup: string | null;
  languages: string[];
  quality: string | null;
  size: number | null;
}

/** One structured finding: the code the view translates plus the sentence that produced it. */
export interface SymlinkFinding {
  code: string;
  detail: string | null;
  detectedAt: string;
}

/** One managed link as the register reports it, with the titles the current snapshots have for its ids. */
export interface SymlinkLink {
  id: string;
  radarrInstanceId: string;
  radarrMovieId: number;
  radarrMovieTitle: string | null;
  radarrFileId: number;
  sonarrInstanceId: string;
  sonarrSeriesId: number;
  sonarrSeriesTitle: string | null;
  sonarrEpisodeId: number;
  sonarrEpisodeTitle: string | null;
  seasonNumber: number;
  episodeNumber: number;
  storageAreaId: string;
  linkPath: string;
  linkText: string;
  sourcePath: string;
  operationStatus: SymlinkOperationStatus;
  setupStep: SymlinkSetupStep | null;
  findings: SymlinkFinding[];
  monitoringOriginal: boolean | null;
  monitoringDisabled: boolean;
  autoFollowUp: boolean;
  desiredFilename: string | null;
  appliedFilename: string | null;
  sourceVersion: string | null;
  expectedMetadata: SymlinkFileMetadata | null;
  confirmedMetadata: SymlinkFileMetadata | null;
  suggestionId: string | null;
  revision: number;
  createdAt: string;
  updatedAt: string;
  /** When a health check last looked; null for an entry no check has seen yet. */
  lastCheckedAt: string | null;
  /** The last check that had nothing to complain about, which is what makes a row quietly current. */
  lastSuccessfulCheckAt: string | null;
}

/** What confirming a pair would do and what stands in the way; the draft has no id, because nothing exists yet. */
export interface SymlinkLinkPlan {
  runnable: boolean;
  draft: SymlinkLink;
  blockers: SymlinkFinding[];
  warnings: SymlinkFinding[];
}
