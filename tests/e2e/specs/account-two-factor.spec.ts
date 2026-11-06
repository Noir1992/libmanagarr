import { test, expect } from '@playwright/test';
import { prepareAccount, login } from '../fixtures/app.js';
import { accountTab, accountState, credentialsLogin, logout } from '../fixtures/account.js';
import { freshTotp } from '../fixtures/totp.js';

test('E03: enable 2FA in the UI and require a fresh code after password login', async ({ request, page, browser }) => {
  test.setTimeout(150_000);
  await prepareAccount(request);
  await login(page);
  const other = await browser.newContext();
  try {
    await login(await other.newPage());
    await accountTab(page);
    await page.getByRole('button', { name: 'Enable two-factor authentication', exact: true }).click();
    const secret = await page.getByText('Setup key:', { exact: false }).locator('code').innerText();
    const proof = await freshTotp(secret);
    await page.getByLabel('Code', { exact: true }).fill(proof.code);
    await page.getByRole('button', { name: 'Confirm & enable', exact: true }).click();
    await expect(page.getByText('Your recovery codes', { exact: true })).toBeVisible();
    const codes = await page.getByRole('list').getByRole('listitem').allTextContents();
    expect(codes.length).toBeGreaterThan(1);
    const state = await accountState(page.request);
    expect(state.twoFactorEnabled).toBe(true);
    expect(state.recoveryCodesRemaining).toBe(codes.length);
    expect((await other.request.get('/api/account/')).status()).toBe(401);
    await page.getByRole('button', { name: 'I have saved the codes', exact: true }).click();
    await logout(page);
    await credentialsLogin(page);
    await expect(page.getByRole('heading', { name: 'Two-factor verification', exact: true })).toBeVisible();
    expect((await page.request.get('/api/settings/instances')).status()).toBe(401);
    // A consumed setup code is correctly shaped but cannot authenticate again.
    await page.getByLabel('Code', { exact: true }).fill(proof.code);
    await page.getByRole('button', { name: 'Verify', exact: true }).click();
    await expect(page.getByText(/code is incorrect/i)).toBeVisible();
    const next = await freshTotp(secret, proof.step);
    await page.getByLabel('Code', { exact: true }).fill(next.code);
    await page.getByRole('button', { name: 'Verify', exact: true }).click();
    await expect(page).toHaveURL(/\/library$/);
  } finally {
    await other.close();
  }
});
