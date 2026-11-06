import { expect, type Page, type APIRequestContext } from '@playwright/test';
import { credentials, mutation } from './app.js';
import { freshTotp } from './totp.js';

export async function credentialsLogin(page: Page, password = credentials.password) {
  await page.goto('/login');
  await page.getByLabel('Username', { exact: true }).fill(credentials.username);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
}
export async function logout(page: Page) {
  await page.getByRole('button', { name: 'User menu', exact: true }).click();
  await page.getByRole('menuitem', { name: /Log out/ }).click();
  await expect(page).toHaveURL(/\/login$/);
}
export async function accountTab(page: Page) {
  await page.goto('/settings');
  await page.getByRole('tab', { name: 'Account', exact: true }).click();
}
export async function enableTwoFactor(request: APIRequestContext) {
  const setup = (await (await mutation(request, 'post', '/api/account/two-factor/setup', {})).json()) as { sharedKey: string };
  const proof = await freshTotp(setup.sharedKey);
  const result = (await (await mutation(request, 'post', '/api/account/two-factor/setup/confirm', { code: proof.code })).json()) as {
    recoveryCodes: string[];
  };
  expect(result.recoveryCodes.length).toBeGreaterThan(1);
  return { ...result, secret: setup.sharedKey, step: proof.step };
}
export async function accountState(request: APIRequestContext) {
  const response = await request.get('/api/account/');
  expect(response.ok()).toBe(true);
  return (await response.json()) as { username: string; twoFactorEnabled: boolean; recoveryCodesRemaining: number };
}
