import { test, expect } from '@playwright/test';
import { credentials, login } from '../fixtures/app.js';

test('setup, validation, logout and login protect the library', async ({ page, browser }) => {
  await page.goto('/library');
  await expect(page).toHaveURL(/\/setup$/);
  // TEMPORARY: remove after verifying that a failing E2E test blocks image publishing in CI.
  await expect(page, 'Intentional CI failure: verify E2E failures block image publishing').toHaveURL(/\/intentional-ci-failure$/, {
    timeout: 2000,
  });
  await expect(page.getByRole('button', { name: 'Create account' })).toBeDisabled();
  await page.getByLabel('Username', { exact: true }).fill(credentials.username);
  await page.getByLabel('Password', { exact: true }).fill('short');
  await expect(page.getByText('The password must be between 12 and 128 characters long.')).toBeVisible();
  await page.getByLabel('Password', { exact: true }).fill(credentials.password);
  await page.getByLabel('Confirm password', { exact: true }).fill(credentials.password);
  const showPassword = page.getByRole('button', { name: 'Show password', exact: true }).first();
  const eye = showPassword.getByText('visibility', { exact: true });
  const font = await eye.evaluate(async (element) => {
    const faces = await document.fonts.load('24px "Material Symbols Outlined"', 'visibility');
    const range = document.createRange();
    range.selectNodeContents(element);
    return { loaded: faces.some((face) => face.status === 'loaded'), width: range.getBoundingClientRect().width };
  });
  expect(font.loaded, 'Icon font must load from the app in the offline stack').toBe(true);
  expect(font.width, 'The eye must render as one glyph rather than the word visibility').toBeCloseTo(24, 0);
  await showPassword.click();
  await expect(page.getByLabel('Password', { exact: true })).toHaveAttribute('type', 'text');
  await page.getByRole('button', { name: 'Hide password', exact: true }).click();
  await expect(page.getByLabel('Password', { exact: true })).toHaveAttribute('type', 'password');
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page).toHaveURL(/\/library$/);
  await page.getByRole('button', { name: 'User menu', exact: true }).click();
  await page.getByRole('menuitem', { name: /Log out/ }).click();
  await expect(page).toHaveURL(/\/login$/);
  await page.goto('/settings');
  await expect(page).toHaveURL(/\/login/);
  await page.screenshot({ path: '/artifacts/password-icon.png' });
  await test.info().attach('Password icon in the offline stack', { path: '/artifacts/password-icon.png', contentType: 'image/png' });
  await login(page);
  await page.reload();
  await expect(page.getByRole('heading', { name: /Library/ })).toBeVisible();
  const anonymous = await browser.newContext();
  try {
    const response = await anonymous.request.get(`${test.info().project.use.baseURL}/api/settings/instances`);
    expect(response.status()).toBe(401);
  } finally {
    await anonymous.close();
  }
});
