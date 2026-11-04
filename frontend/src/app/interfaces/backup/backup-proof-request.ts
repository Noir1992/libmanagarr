/** Identity proof for the one action that replaces the installation: password and second factor. */
export interface BackupProofRequest {
  readonly currentPassword: string;
  readonly code?: string;
  readonly useRecoveryCode?: boolean;
}
