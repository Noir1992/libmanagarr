import { test, expect } from '@playwright/test';
import { prepareAccount, login, addInstance, instanceSettings, syncInstance } from '../fixtures/app.js';
import { expected, arrRequest } from '../fixtures/library.js';

test('E07: missing configuration and an empty real library show distinct states', async ({ request, page }) => {
  await prepareAccount(request);
  for (const series of expected.series)
    expect((await arrRequest(request, 'sonarr', `/series/${series.id}?deleteFiles=false`, 'delete')).ok()).toBe(true);
  await login(page);
  await expect(page.getByText('No Sonarr/Radarr instances configured.', { exact: true })).toBeVisible();
  await page.getByText('Click here to configure Arr instances', { exact: true }).click();
  await expect(page).toHaveURL(/\/settings/);
  await instanceSettings(page);
  await addInstance(page, 'Sonarr');
  await syncInstance(page, 'Sonarr');
  await page.goto('/library');
  await expect(page.getByText('No data available', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Library (0)', exact: true })).toBeVisible();
  await expect(page.getByText('No Sonarr/Radarr instances configured.', { exact: true })).toBeHidden();
  await expect(page.getByRole('article')).toHaveCount(0);
});
