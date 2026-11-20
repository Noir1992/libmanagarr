import { expect, test } from '@playwright/test';
import { login, mutation, prepareAccount } from '../fixtures/app.js';
import { configureLibrary } from '../fixtures/library.js';
import { configureSymlinks, seededPair, waitForSuggestedPair } from '../fixtures/symlinks.js';

test('E17: switching to German uses saved titles in library and suggestions without another sync', async ({ request, page }) => {
  await prepareAccount(request);
  await configureLibrary(request);
  await configureSymlinks(request);
  const pair = await seededPair(request);
  await waitForSuggestedPair(request, pair);
  await mutation(request, 'post', '/api/media-titles/refresh', {});
  await mutation(request, 'post', '/api/settings/symlinks/suggestions/refresh', {});
  const before = (await (await request.get('/api/settings/symlinks/suggestions')).json()) as { id: string; revision: number }[];
  const syncs: string[] = [];
  page.on('request', (call) => {
    if (call.url().endsWith('/api/settings/sync')) syncs.push(call.url());
  });
  await login(page);
  await expect(page.getByText('Spirited Away', { exact: true }).first()).toBeVisible();
  await page.goto('/settings');
  await page.getByRole('combobox').filter({ hasText: 'English' }).click();
  const saved = page.waitForResponse(
    (response) => response.url().endsWith('/api/settings/general') && response.request().method() === 'PUT',
  );
  await page.getByRole('option', { name: 'Deutsch', exact: true }).click();
  expect((await saved).ok()).toBe(true);
  await page.goto('/library');
  await expect(page.getByText('Chihiros Reise ins Zauberland', { exact: true }).first()).toBeVisible();
  await page.getByRole('searchbox').fill('Spirited Away');
  await expect(page.getByText('Chihiros Reise ins Zauberland', { exact: true }).first()).toBeVisible();
  await page.goto('/symlinks');
  const after = (await (await request.get('/api/settings/symlinks/suggestions')).json()) as {
    id: string;
    revision: number;
    radarrMovieTitle: string;
  }[];
  expect(after.map(({ id, revision }) => ({ id, revision }))).toEqual(before.map(({ id, revision }) => ({ id, revision })));
  const offer = after.find((value) => value.id === before[0].id)!;
  await expect(page.getByRole('listitem').filter({ hasText: offer.radarrMovieTitle }).first()).toBeVisible();
  expect(syncs).toEqual([]);
});
