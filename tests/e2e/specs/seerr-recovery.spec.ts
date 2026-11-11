import { test, expect } from '@playwright/test';
import { access, writeFile } from 'node:fs/promises';
import { artifactPath, prepareAccount, login, seerrKey } from '../fixtures/app.js';
import { configureSeerr } from '../fixtures/library.js';

test('E09: request loading reports Seerr failure and recovers after restart', async ({ request, page }) => {
  test.setTimeout(150_000);
  await prepareAccount(request);
  await configureSeerr(request);
  await login(page);
  await page.goto('/requests');
  await expect(page.getByText('Inception', { exact: true })).toBeVisible();
  const original = (await (await page.request.get('/api/settings/seerr')).json()) as unknown;
  await writeFile(artifactPath('stop-seerr'), 'ready');
  await expect
    .poll(
      () =>
        access(artifactPath('seerr-stopped')).then(
          () => true,
          () => false,
        ),
      { timeout: 40_000 },
    )
    .toBe(true);
  await page.reload();
  await expect(page.getByText(/Error loading data:/)).toBeVisible({ timeout: 50_000 });
  await expect(page.getByText('The configured Seerr instance did not answer the requests query.', { exact: true })).toBeVisible();
  await expect(page.getByText('Inception', { exact: true })).toBeHidden();
  await writeFile(artifactPath('restart-seerr'), 'ready');
  await expect
    .poll(
      async () => {
        try {
          return (await request.get('http://seerr:5055/api/v1/auth/me', { headers: { 'X-Api-Key': seerrKey }, timeout: 2000 })).ok();
        } catch {
          return false;
        }
      },
      { timeout: 60_000 },
    )
    .toBe(true);
  await page.reload();
  await expect(page.getByText('Inception', { exact: true })).toBeVisible();
  await expect(page.getByText('Pending', { exact: true }).last()).toBeVisible();
  expect(await (await page.request.get('/api/settings/seerr')).json()).toEqual(original);
});
