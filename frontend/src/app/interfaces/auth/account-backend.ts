export interface AccountBackend {
  username: string;
  twoFactorEnabled: boolean;
  recoveryCodesRemaining: number;
}
