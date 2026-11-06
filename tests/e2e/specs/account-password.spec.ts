import { test, expect } from '@playwright/test';
import { credentials, prepareAccount, login } from '../fixtures/app.js';
import { accountTab, credentialsLogin } from '../fixtures/account.js';

test('E02: password changes preserve failed attempts and revoke every successful session', async ({ request, page, browser }) => {
  await prepareAccount(request);
  await login(page);
  const other = await browser.newContext();
  try {
    const second = await other.newPage();
    await login(second);
    await accountTab(page);
    await page.getByLabel('Current password', { exact: true }).first().fill('Incorrect-current-password!');
    await page.getByLabel('New password', { exact: true }).fill('Changed-E2E-password-2026!');
    await page.getByLabel('Confirm new password', { exact: true }).fill('Changed-E2E-password-2026!');
    await page.getByRole('button', { name: 'Change password', exact: true }).click();
    await expect(page.getByText(/current password is incorrect/i)).toBeVisible();
    expect((await page.request.get('/api/account/')).status()).toBe(200);
    expect((await other.request.get('/api/account/')).status()).toBe(200);
    await page.getByLabel('Current password', { exact: true }).first().fill(credentials.password);
    await page.getByRole('button', { name: 'Change password', exact: true }).click();
    await expect(page.getByText('Password changed. Please sign in again.', { exact: true })).toBeVisible();
    expect((await page.request.get('/api/account/')).status()).toBe(401);
    await page.getByRole('button', { name: 'Sign in again', exact: true }).click();
    await expect(page).toHaveURL(/\/login/);
    expect((await other.request.get('/api/account/')).status()).toBe(401);
    await second.goto('/library');
    await expect(second).toHaveURL(/\/login/);
    await credentialsLogin(page);
    await expect(page.getByText('Username or password is incorrect.', { exact: true })).toBeVisible();
    await credentialsLogin(page, 'Changed-E2E-password-2026!');
    await expect(page).toHaveURL(/\/library$/);
    await page.reload();
    await expect(page.getByRole('heading', { name: /Library/ })).toBeVisible();
  } finally {
    await other.close();
  }
});
