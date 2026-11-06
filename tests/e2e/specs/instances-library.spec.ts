import { test, expect } from '@playwright/test';
import { prepareAccount, login, instanceSettings, addInstance, syncInstance, instanceSection, baseline } from '../fixtures/app.js';

test('save real Arr instances, synchronize media and open details', async ({ request, page }) => {
  await prepareAccount(request);
  await login(page);
  await instanceSettings(page);
  for (const kind of ['Radarr', 'Sonarr'] as const) await addInstance(page, kind);
  await page.reload();
  await page.getByRole('tab', { name: 'Arr instances', exact: true }).click();
  for (const kind of ['Radarr', 'Sonarr'] as const) {
    await expect(instanceSection(page, kind).getByLabel('Name*', { exact: true })).toHaveValue(`E2E ${kind}`);
    await syncInstance(page, kind);
  }
  await page.goto('/library');
  await expect(page.getByText(baseline.movieTitle, { exact: true }).first()).toBeVisible();
  await expect(page.getByText(baseline.seriesTitle, { exact: true }).first()).toBeVisible();
  await page.getByRole('searchbox').fill(baseline.movieTitle);
  await expect(page.getByText(baseline.seriesTitle, { exact: true })).toBeHidden();
  // Title links lead to the external Arr app; the card opens LibManagarr's own details.
  const movieRow = page.getByRole('article').filter({ has: page.getByText(baseline.movieTitle, { exact: true }) });
  await movieRow.click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await expect(page.getByRole('dialog').getByText(/1080p/).first()).toBeVisible();
  await page.keyboard.press('Escape');
  await page.getByRole('searchbox').fill(baseline.seriesTitle);
  await expect(page.getByText(baseline.movieTitle, { exact: true })).toBeHidden();
  const seriesRow = page.getByRole('article').filter({ has: page.getByText(baseline.seriesTitle, { exact: true }) });
  await seriesRow.click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await expect(
    page
      .getByRole('dialog')
      .getByText(/Specials/)
      .first(),
  ).toBeVisible();
});
