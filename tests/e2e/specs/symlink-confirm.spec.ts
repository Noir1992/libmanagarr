import { expect, test } from '@playwright/test';
import { login, prepareAccount } from '../fixtures/app.js';
import { configureLibrary, expected } from '../fixtures/library.js';
import {
  arrGet,
  configureSymlinks,
  episode,
  linkTarget,
  managedLinks,
  rowsOf,
  seededPair,
  seriesOf,
  type ArrEpisodeFile,
} from '../fixtures/symlinks.js';

test('E15: confirming an offer links the film file that the baseline paired with the special', async ({ request, page }) => {
  await prepareAccount(request);
  const pair = await seededPair(request);
  await configureLibrary(request);
  await configureSymlinks(request);

  await login(page);
  await page.goto('/symlinks');
  await page.getByRole('button', { name: 'Match again', exact: true }).click();
  const offer = rowsOf(page, 'Offers').filter({ hasText: pair.film.title });
  await expect(offer).toHaveCount(1);
  await expect(offer.getByText(`Season ${pair.episode.seasonNumber},`)).toBeVisible();
  await offer.getByRole('button', { name: 'Create link', exact: true }).click();

  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText('What the link would create', { exact: true })).toBeVisible();
  const create = dialog.getByRole('button', { name: 'Create it now', exact: true });
  await expect(create).toBeEnabled();
  await create.click();
  await expect(dialog).toBeHidden();

  const link = rowsOf(page, 'Managed links').filter({ hasText: pair.film.title });
  await expect(link).toHaveCount(1);
  await expect(link.getByText('Active', { exact: true })).toBeVisible();
  // Only the link itself is through here: releasing the series from Sonarr is owed to a later run, so the
  // register says which half is still missing and the note about monitoring may not be shown yet.
  await expect(link.getByText('Step: Monitoring disabled', { exact: true })).toBeVisible();
  await expect(link.getByRole('button', { name: 'Delete link', exact: true })).toBeVisible();

  // The Sonarr half of the setup is owed to a background run, so the register decides when it is through —
  // and only a reload can then bring that state onto the screen.
  const settled = () => managedLinks(request).then((values) => values.find((value) => value.sourcePath === pair.filmFile.path)?.setupStep);
  await expect.poll(settled, { timeout: 180_000 }).toBe('Completed');
  await page.reload();
  await expect(link.getByText('Step: Completed', { exact: true })).toBeVisible();
  await expect(link.getByText('The linked episode is no longer monitored in Sonarr', { exact: false })).toBeVisible();

  const links = await managedLinks(request);
  expect(links).toHaveLength(1);
  expect(links[0].sourcePath).toBe(pair.filmFile.path);
  expect(linkTarget(links[0].linkPath)).toEqual({ kind: 'link', resolvesTo: pair.filmFile.path });

  // Sonarr has to find the linked file itself; the register alone would prove nothing about the volume. The
  // association is read from the episode, because a file row of the instance names no episode of its own.
  await expect.poll(() => episode(request, pair.episode.id).then((value) => value.hasFile), { timeout: 180_000 }).toBe(true);
  const withFile = await episode(request, pair.episode.id);
  const files = await arrGet<ArrEpisodeFile[]>(request, 'sonarr', `/episodefile?seriesId=${pair.series.id}`);
  const linked = files.find((value) => value.id === withFile.episodeFileId);
  expect(linked?.path).toBe(links[0].linkPath);
  expect(linked?.mediaInfo?.videoCodec).toBe(expected.files.videoCodec);
  expect(linked?.mediaInfo?.audioLanguages).toBe('jpn');

  // The special must never be downloaded a second time, while the series and everything else in it keep the
  // monitoring they had — this feature releases one episode and no more than that one episode.
  expect((await episode(request, pair.episode.id)).monitored).toBe(false);
  expect((await seriesOf(request, pair.series.id)).monitored).toBe(pair.series.monitored);
  expect(linkTarget(pair.filmFile.path)).toEqual({ kind: 'file', resolvesTo: null });
});
