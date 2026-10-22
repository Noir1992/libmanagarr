export interface ChangePasswordRequest {
  readonly currentPassword: string;
  readonly newPassword: string;
  readonly confirmPassword: string;
  readonly code?: string;
  readonly useRecoveryCode?: boolean;
}
