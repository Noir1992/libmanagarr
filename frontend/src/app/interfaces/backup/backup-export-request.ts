/** Body of an export: the password protecting the new archive plus the usual identity proof. */
export interface BackupExportRequest {
  readonly backupPassword: string;
  readonly currentPassword: string;
  readonly code?: string;
  readonly useRecoveryCode?: boolean;
}
