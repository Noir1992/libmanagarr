export interface TwoFactorSetupBackend {
  sharedKey: string;
  authenticatorUri: string;
  qrCodeSvg: string;
  expiresAt: string;
}
