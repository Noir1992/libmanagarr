import { ReleaseVersion } from './release-version';

// Mirrors the backend enum; an unreachable upstream still answers with status 200 and reports
// the cause here instead of an error status.
export type ReleaseOverviewStatus = 'Ok' | 'Unreachable' | 'RateLimited' | 'Rejected';

export interface ReleaseOverview {
  readonly currentVersion: string;
  readonly updateAvailable: boolean;
  readonly status: ReleaseOverviewStatus;
  readonly checkedAt: string;
  readonly versions: readonly ReleaseVersion[];
}
