import { expect, test } from '@playwright/test';
import { existsSync, statSync } from 'node:fs';
import { login, prepareAccount } from '../fixtures/app.js';
import { configureLibrary } from '../fixtures/library.js';
import { configureSymlinks, episode, managedLinks, rowsOf, seededPair, seriesOf } from '../fixtures/symlinks.js';

test('E16: a declined pair stays declined through another sync and changes nothing on disk', async ({ request, page }) => {
  await prepareAccount(request);
  const pair = await seededPair(request);
  await configureLibrary(request);
  await configureSymlinks(request);

  await login(page);
  await page.goto('/symlinks');
  await page.getByRole('button', { name: 'Match again', exact: true }).click();
  const offer = rowsOf(page, 'Suggestions').filter({ hasText: pair.film.title });
  await expect(offer).toHaveCount(1);
  await offer.getByRole('button', { name: 'Decline', exact: true }).click();

  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText('Decline the pair', { exact: true })).toBeVisible();
  await dialog.getByLabel('Reason (optional)', { exact: true }).fill('Belongs to the series, not to this shelf');
  await dialog.getByRole('button', { name: 'Decline', exact: true }).click();
  await expect(dialog).toBeHidden();

  await expect(rowsOf(page, 'Suggestions')).toHaveCount(0);
  const rejection = rowsOf(page, 'Rejections').filter({ hasText: pair.film.title });
  await expect(rejection).toHaveCount(1);
  await expect(rejection.getByText('Belongs to the series, not to this shelf', { exact: true })).toBeVisible();

  // Another synchronization and another matching pass must not bring the decision back as an offer.
  await configureLibrary(request);
  await page.getByRole('button', { name: 'Match again', exact: true }).click();
  await expect(rowsOf(page, 'Suggestions')).toHaveCount(0);
  await expect(rejection).toHaveCount(1);

  expect(await managedLinks(request)).toHaveLength(0);
  expect((await episode(request, pair.episode.id)).hasFile).toBe(false);
  expect((await seriesOf(request, pair.series.id)).monitored).toBe(pair.series.monitored);
  expect(existsSync(pair.filmFile.path), 'the film file must stay untouched').toBe(true);
  expect(statSync(pair.filmFile.path).size).toBe(pair.filmFile.size);
});
