/** Answer of the import check. Only counts, versions and dates: the preview never carries credentials. */
export interface BackupImportPreview {
  readonly importId: string;
  readonly expiresAt: string;
  readonly applicationVersion: string;
  readonly createdAt: string;
  readonly librarySchemaVersion: number;
  readonly sonarrInstanceCount: number;
  readonly radarrInstanceCount: number;
  readonly hasSeerrInstance: boolean;
  readonly archiveBytes: number;
  readonly uncompressedBytes: number;
  readonly sameApplicationVersion: boolean;
}
