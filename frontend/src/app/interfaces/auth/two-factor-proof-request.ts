export interface TwoFactorProofRequest {
  readonly currentPassword: string;
  readonly code?: string;
  readonly useRecoveryCode?: boolean;
}
