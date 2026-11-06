import { test, expect } from '@playwright/test';
import { prepareAccount, login } from '../fixtures/app.js';
import { configureLibrary, expected } from '../fixtures/library.js';
import { exportBackup, selectArchive, validateArchive } from '../fixtures/backup.js';

test('E11: encrypted backup validates the archive password and discard preserves application state', async ({ request, page }) => {
  await prepareAccount(request);
  await configureLibrary(request);
  await login(page);
  const before = (await (await page.request.get('/api/settings/instances')).json()) as unknown;
  const file = await exportBackup(page);
  await selectArchive(page, file);
  expect((await validateArchive(page, 'Incorrect-archive-password!')).ok()).toBe(false);
  await expect(page.getByText('The backup password is incorrect.', { exact: true })).toBeVisible();
  expect(await (await page.request.get('/api/settings/instances')).json()).toEqual(before);
  expect((await validateArchive(page)).ok()).toBe(true);
  await expect(page.getByText('Backup contents', { exact: true })).toBeVisible();
  await expect(page.getByLabel('Contents', { exact: true })).toHaveValue('Settings and library');
  await expect(page.getByLabel('Instances', { exact: true })).toHaveValue('1 Sonarr and 1 Radarr instances');
  page.once('dialog', async (dialog) => {
    await dialog.accept();
  });
  const discarded = page.waitForResponse(
    (response) => response.request().method() === 'DELETE' && response.url().includes('/api/backups/import/'),
  );
  await page.getByRole('button', { name: 'Discard', exact: true }).click();
  expect((await discarded).ok()).toBe(true);
  await expect(page.getByText('Backup contents', { exact: true })).toBeHidden();
  expect(await (await page.request.get('/api/settings/instances')).json()).toEqual(before);
  await page.goto('/library');
  // The discard must have changed nothing, so the library still holds every title the seed carries.
  await expect(page.getByRole('heading', { name: `Library (${expected.titles.length})`, exact: true })).toBeVisible();
});
