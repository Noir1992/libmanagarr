import { test, expect } from '@playwright/test';
import { prepareAccount, login } from '../fixtures/app.js';
import { credentialsLogin } from '../fixtures/account.js';

test('E01: invalid login stays anonymous and valid credentials still work', async ({ request, page }) => {
  await prepareAccount(request);
  await credentialsLogin(page, 'Incorrect-test-password!');
  await expect(page.getByText('Username or password is incorrect.', { exact: true })).toBeVisible();
  expect((await page.request.get('/api/settings/instances')).status()).toBe(401);
  await page.goto('/library');
  await expect(page).toHaveURL(/\/login/);
  await login(page);
  await page.reload();
  await expect(page.getByRole('heading', { name: /Library/ })).toBeVisible();
});
