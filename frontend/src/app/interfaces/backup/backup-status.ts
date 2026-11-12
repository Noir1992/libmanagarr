/** Phase of a running backup operation, as reported by the status route. */
export type BackupOperationPhase =
  'Idle' | 'Exporting' | 'Validating' | 'PreparingRestore' | 'RestartPending' | 'Restored' | 'RestoreRolledBack';

export interface BackupStatus {
  readonly phase: BackupOperationPhase;
  readonly code: string | null;
  readonly changedAt: string;
  readonly operationId: string | null;
}
