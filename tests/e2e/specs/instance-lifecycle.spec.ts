import { test, expect } from '@playwright/test';
import { prepareAccount, login, instanceSettings, instanceSection } from '../fixtures/app.js';
import { configureLibrary, expected, arrRequest, type Instances } from '../fixtures/library.js';

test('E06: invalid edits preserve saved settings and deleting an instance preserves its source', async ({ request, page }) => {
  await prepareAccount(request);
  const original = await configureLibrary(request);
  await login(page);
  await instanceSettings(page);
  const section = instanceSection(page, 'Sonarr');
  await section.getByLabel('API key*', { exact: true }).fill('invalid-api-key');
  await section.getByRole('button', { name: 'Test connection', exact: true }).click();
  await expect(section.getByText(/Connection could not be established/)).toBeVisible();
  const saved = (await (await request.get('/api/settings/instances')).json()) as Instances;
  expect(saved.sonarrInstances[0]).toEqual(original.sonarrInstances[0]);
  page.once('dialog', async (dialog) => {
    expect(dialog.message()).toContain('Unsaved changes');
    await dialog.dismiss();
  });
  await page.getByRole('link', { name: 'Library', exact: true }).click();
  await expect(page).toHaveURL(/\/settings/);
  await expect(section.getByLabel('API key*', { exact: true })).toHaveValue('invalid-api-key');
  page.once('dialog', async (dialog) => {
    await dialog.accept();
  });
  await page.getByRole('link', { name: 'Library', exact: true }).click();
  await expect(page).toHaveURL(/\/library$/);
  await instanceSettings(page);
  await expect(section.getByLabel('API key*', { exact: true })).toHaveValue(original.sonarrInstances[0].apiKey);
  await section.getByLabel('Name*', { exact: true }).fill('Renamed E2E Sonarr');
  const renamed = page.waitForResponse((response) => /\/sonarr\/0$/i.test(response.url()) && response.request().method() === 'PUT');
  await section.getByLabel('Name*', { exact: true }).press('Tab');
  expect((await renamed).ok()).toBe(true);
  await page.reload();
  await page.getByRole('tab', { name: 'Arr instances', exact: true }).click();
  await expect(section.getByLabel('Name*', { exact: true })).toHaveValue('Renamed E2E Sonarr');
  const removed = page.waitForResponse((response) => /\/sonarr\/0$/i.test(response.url()) && response.request().method() === 'DELETE');
  await section.getByRole('button', { name: 'Remove', exact: true }).click();
  expect((await removed).ok()).toBe(true);
  await page.goto('/library');
  await expect(page.getByRole('link', { name: expected.series[0].title, exact: true })).toBeHidden();
  await expect(page.getByRole('link', { name: expected.movies[0].title, exact: true })).toBeVisible();
  const source = await arrRequest(request, 'sonarr', '/series');
  expect(((await source.json()) as { title: string }[]).map((item) => item.title).sort()).toEqual(
    expected.series.map((item) => item.title).sort(),
  );
  expect((await (await arrRequest(request, 'sonarr', `/episodefile?seriesId=${expected.series[0].id}`)).json()) as unknown[]).toHaveLength(
    expected.files.sonarr,
  );
});
