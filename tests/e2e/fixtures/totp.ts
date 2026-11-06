import { expect } from '@playwright/test';
import { TOTP } from 'otpauth';

export async function freshTotp(secret: string, previousStep = -1) {
  await expect
    .poll(() => Math.floor(Date.now() / 30_000) > previousStep && Date.now() % 30_000 < 25_000, {
      message: 'Wait for a fresh real TOTP step with at least five seconds remaining',
      timeout: 40_000,
    })
    .toBe(true);
  const timestamp = Date.now();
  const totp = new TOTP({ secret: secret.replaceAll(' ', ''), algorithm: 'SHA1', digits: 6, period: 30 });
  return { code: totp.generate({ timestamp }), step: Math.floor(timestamp / 30_000) };
}
