export interface TwoFactorStateBackend {
  pending: boolean;
  expiresAt: string | null;
}
