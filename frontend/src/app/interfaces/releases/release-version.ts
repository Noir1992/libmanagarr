export interface ReleaseVersion {
  readonly version: string;
  readonly name: string;
  readonly tag: string;
  readonly publishedAt: string;
  readonly notes: string | null;
  readonly url: string;
  readonly isPrerelease: boolean;
  readonly isCurrent: boolean;
  readonly isNewerThanCurrent: boolean;
  readonly pullReference: string;
}
