import { test, expect } from '@playwright/test';
import { prepareAccount, login } from '../fixtures/app.js';
import { configureLibrary, expected } from '../fixtures/library.js';
import { selectFilter, clearFilters } from '../fixtures/filters.js';

test('E14: codec counts, percentages and instance filters match the known files', async ({ request, page }) => {
  await prepareAccount(request);
  await configureLibrary(request);
  await login(page);
  await page.goto('/codec-statistics');
  const video = page.getByRole('article').filter({ has: page.getByRole('heading', { name: 'Video codecs', exact: true }) });
  const audio = page.getByRole('article').filter({ has: page.getByRole('heading', { name: 'Audio codecs', exact: true }) });
  await video.getByRole('button', { name: /Show values/ }).click();
  await audio.getByRole('button', { name: /Show values/ }).click();
  await expect(video.getByRole('button', { name: new RegExp(`${expected.files.videoCodec}.*3 files.*100%`) })).toBeVisible();
  await expect(audio.getByRole('button', { name: /AAC.*3 files.*100%/ })).toBeVisible();
  await selectFilter(page, 'Radarr instances', 'E2E Radarr');
  await selectFilter(page, 'Sonarr instances', 'E2E Sonarr', true);
  await expect(video.getByRole('button', { name: /1 files.*100%/ })).toBeVisible();
  await clearFilters(page);
  await expect(video.getByRole('button', { name: /3 files.*100%/ })).toBeVisible();
  await selectFilter(page, 'Video codec', expected.files.videoCodec, true);
  await expect(page.getByText('No data available for the current filter', { exact: true })).toBeVisible();
});
