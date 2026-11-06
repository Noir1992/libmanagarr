import { test, expect } from '@playwright/test';
import { credentials, prepareAccount, login, instanceSettings, instanceSection } from '../fixtures/app.js';
import { configureLibrary, expected, assertTitles, arrRequest } from '../fixtures/library.js';
import { backupTab, exportBackup, selectArchive, validateArchive } from '../fixtures/backup.js';
import { logout } from '../fixtures/account.js';

test('E12: applying a backup restarts the real host and restores settings and library without replacing identity', async ({
  request,
  page,
}) => {
  test.setTimeout(150_000);
  await prepareAccount(request);
  await configureLibrary(request);
  await login(page);
  const file = await exportBackup(page);
  await instanceSettings(page);
  const section = instanceSection(page, 'Sonarr');
  const renamed = page.waitForResponse((response) => /\/sonarr\/0$/i.test(response.url()) && response.request().method() === 'PUT');
  await section.getByLabel('Name*', { exact: true }).fill('Changed after backup');
  await section.getByLabel('Name*', { exact: true }).press('Tab');
  expect((await renamed).ok()).toBe(true);
  const removed = page.waitForResponse((response) => /\/radarr\/0$/i.test(response.url()) && response.request().method() === 'DELETE');
  await instanceSection(page, 'Radarr').getByRole('button', { name: 'Remove', exact: true }).click();
  expect((await removed).ok()).toBe(true);
  await page.goto('/library');
  await expect(page.getByRole('heading', { name: 'Library (2)', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: expected.movies[0].title, exact: true })).toBeHidden();
  await backupTab(page);
  await selectArchive(page, file);
  expect((await validateArchive(page)).ok()).toBe(true);
  await page.getByLabel('Current password*', { exact: true }).last().fill(credentials.password);
  page.once('dialog', async (dialog) => {
    await dialog.accept();
  });
  const applied = page.waitForResponse((response) => /\/api\/backups\/import\/.+\/apply$/.test(response.url()));
  await page.getByRole('button', { name: 'Restore and restart', exact: true }).click();
  expect((await applied).ok()).toBe(true);
  await expect(
    page
      .getByRole('status')
      .filter({ hasText: /restart|restor|start/i })
      .first(),
  ).toBeVisible();
  await expect
    .poll(
      async () => {
        try {
          const response = await page.request.get('/api/settings/instances', { timeout: 2000 });
          if (!response.ok()) return '';
          const settings = (await response.json()) as { sonarrInstances: { name: string }[] };
          return settings.sonarrInstances[0]?.name;
        } catch {
          return '';
        }
      },
      { timeout: 60_000 },
    )
    .toBe('E2E Sonarr');
  await page.reload();
  await instanceSettings(page);
  await expect(section.getByLabel('Name*', { exact: true })).toHaveValue('E2E Sonarr');
  await page.goto('/library');
  await expect(page.getByRole('link', { name: expected.movies[0].title, exact: true })).toBeVisible();
  await assertTitles(page, expected.titles);
  expect((await (await arrRequest(request, 'radarr', `/moviefile?movieId=${expected.movies[0].id}`)).json()) as unknown[]).toHaveLength(1);
  await logout(page);
  await login(page);
  expect((await page.request.get('/api/account/')).status()).toBe(200);
});
