/** Answer to a confirmed import: the restore waits for the next start and the application stops. */
export interface BackupApplyResult {
  readonly operationId: string;
  readonly appliedAt: string;
  readonly restartRequired: boolean;
}
