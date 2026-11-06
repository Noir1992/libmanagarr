import { test, expect } from '@playwright/test';
import { prepareAccount } from '../fixtures/app.js';
import { accountState, credentialsLogin, enableTwoFactor, logout } from '../fixtures/account.js';

test('E04: a recovery code permits exactly one login', async ({ request, page }) => {
  await prepareAccount(request);
  const setup = await enableTwoFactor(request);
  const enter = async (code: string) => {
    await credentialsLogin(page);
    await page.getByRole('button', { name: 'Use a recovery code instead', exact: true }).click();
    await page.getByLabel('Recovery code', { exact: true }).fill(code);
    await page.getByRole('button', { name: 'Verify', exact: true }).click();
  };
  await enter(setup.recoveryCodes[0]);
  await expect(page).toHaveURL(/\/library$/);
  expect((await accountState(page.request)).recoveryCodesRemaining).toBe(setup.recoveryCodes.length - 1);
  await logout(page);
  await enter(setup.recoveryCodes[0]);
  await expect(page.getByText(/code is incorrect/i)).toBeVisible();
  expect((await page.request.get('/api/account/')).status()).toBe(401);
  await page.getByLabel('Recovery code', { exact: true }).fill(setup.recoveryCodes[1]);
  await page.getByRole('button', { name: 'Verify', exact: true }).click();
  await expect(page).toHaveURL(/\/library$/);
  expect((await accountState(page.request)).recoveryCodesRemaining).toBe(setup.recoveryCodes.length - 2);
});
