import { expect, test } from '@playwright/test';
import { apiKey, instanceSettings, login, prepareAccount, syncInstance } from '../fixtures/app.js';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { chownSync, readFileSync, renameSync, statSync } from 'node:fs';
import { join } from 'node:path';
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
  type ArrMovieFile,
} from '../fixtures/symlinks.js';

test('E15: confirming an offer links the film file that the baseline paired with the special', async ({ request, page }) => {
  test.setTimeout(480_000);
  await prepareAccount(request);
  const pair = await seededPair(request);
  await configureLibrary(request);
  await configureSymlinks(request);
  const naming = await arrGet<{ id: number; [key: string]: unknown }>(request, 'sonarr', '/config/naming');
  const format = '{Series Title} - S{season:00}E{episode:00} - {Quality Full} - {Release Group}';
  const namingSaved = await request.put(`http://sonarr:8989/api/v3/config/naming/${naming.id}`, {
    headers: { 'X-Api-Key': apiKey },
    data: {
      ...naming,
      renameEpisodes: true,
      standardEpisodeFormat: format,
      animeEpisodeFormat: format,
      specialsFolderFormat: 'Season {season:00}',
    },
  });
  expect(namingSaved.ok(), await namingSaved.text()).toBe(true);

  await login(page);
  await page.goto('/symlinks');
  await page.getByRole('button', { name: 'Match again', exact: true }).click();
  const offer = rowsOf(page, 'Suggestions').filter({ hasText: pair.film.title });
  await expect(offer).toHaveCount(1);
  await expect(offer.getByText(`Special ${pair.episode.episodeNumber}`)).toBeVisible();
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
  // Sonarr setup and the targeted library sync continue in the background after creating the file link.
  await expect(link.getByRole('button', { name: 'Delete', exact: true })).toBeVisible();

  // The Sonarr half of the setup is owed to a background run, so the register decides when it is through —
  // and only a reload can then bring that state onto the screen.
  const settled = () => managedLinks(request).then((values) => values.find((value) => value.sourcePath === pair.filmFile.path)?.setupStep);
  await expect.poll(settled, { timeout: 180_000 }).toBe('Completed');
  await expect(link.getByRole('button', { name: 'Edit', exact: true })).toBeEnabled({ timeout: 15_000 });

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

  // The fixture performs a Radarr upgrade with its own generated video. The user action that asks
  // LibManagarr to notice it still goes through the browser's synchronize button.
  const previous = `${pair.filmFile.path}.previous`;
  const previousHash = createHash('sha256').update(readFileSync(pair.filmFile.path)).digest('hex');
  renameSync(pair.filmFile.path, previous);
  const upgradedPath = join(pair.film.path, `${pair.film.title} (${pair.film.year}) - WEBDL-720p-E2EUpgrade.mp4`);
  execFileSync('ffmpeg', [
    '-hide_banner',
    '-loglevel',
    'error',
    '-f',
    'lavfi',
    '-i',
    'color=c=red:s=1280x720:r=24',
    '-f',
    'lavfi',
    '-i',
    'anullsrc=r=48000:cl=stereo',
    '-t',
    '120',
    '-c:v',
    'libx264',
    '-preset',
    'ultrafast',
    '-c:a',
    'aac',
    '-metadata:s:a:0',
    'language=deu',
    '-pix_fmt',
    'yuv420p',
    '-y',
    upgradedPath,
  ]);
  chownSync(upgradedPath, 1000, 1000);
  const upgradeScan = await request.post('http://radarr:7878/api/v3/command', {
    headers: { 'X-Api-Key': apiKey },
    data: { name: 'RescanMovie', movieId: pair.film.id },
  });
  expect(upgradeScan.ok()).toBe(true);
  const command = (await upgradeScan.json()) as { id: number };
  await expect
    .poll(() => arrGet<{ status: string }>(request, 'radarr', `/command/${command.id}`).then((value) => value.status), { timeout: 120_000 })
    .toBe('completed');
  const upgradedFiles = await arrGet<ArrMovieFile[]>(request, 'radarr', `/moviefile?movieId=${pair.film.id}`);
  expect(upgradedFiles).toHaveLength(1);
  const upgraded = upgradedFiles[0];
  expect(upgraded.path).toBe(upgradedPath);
  expect(upgraded.id).not.toBe(pair.filmFile.id);
  expect(upgraded.releaseGroup).toBe('E2EUpgrade');
  const sourceHash = createHash('sha256').update(readFileSync(upgradedPath)).digest('hex');
  const sourceStat = statSync(upgradedPath);

  await instanceSettings(page);
  await syncInstance(page, 'Radarr');
  await page.goto('/symlinks');
  await expect
    .poll(
      async () => {
        const current = await managedLinks(request);
        return current.length === 1 && current[0].sourcePath === upgradedPath && current[0].setupStep === 'Completed';
      },
      { timeout: 240_000 },
    )
    .toBe(true);
  await expect(link.getByRole('button', { name: 'Edit', exact: true })).toBeEnabled({ timeout: 15_000 });
  const [updatedLink] = await managedLinks(request);
  expect(updatedLink.id).toBe(links[0].id);
  expect(updatedLink.linkPath).toMatch(/\.mp4$/);
  expect(updatedLink.linkPath).toContain('720p');
  expect(updatedLink.linkPath).toContain('E2EUpgrade');
  expect(linkTarget(updatedLink.linkPath)).toEqual({ kind: 'link', resolvesTo: upgradedPath });
  const updatedEpisode = await episode(request, pair.episode.id);
  expect(updatedEpisode.monitored).toBe(false);
  const file = await arrGet<ArrEpisodeFile>(request, 'sonarr', `/episodefile/${updatedEpisode.episodeFileId}`);
  expect(file.path).toBe(updatedLink.linkPath);
  expect(file.releaseGroup).toBe(upgraded.releaseGroup);
  expect(file.quality?.quality?.name).toBe(upgraded.quality?.quality?.name);
  expect(file.languages?.map((value) => value.name)).toContain('German');
  expect(file.mediaInfo?.resolution).toBe(upgraded.mediaInfo?.resolution);
  expect(file.mediaInfo?.audioLanguages).toBe(upgraded.mediaInfo?.audioLanguages);
  const proposals = await arrGet<{ episodeFileId: number }[]>(request, 'sonarr', `/rename?seriesId=${pair.series.id}`);
  expect(proposals.some((value) => value.episodeFileId === updatedEpisode.episodeFileId)).toBe(false);

  page.once('dialog', (dialog) => dialog.accept());
  await link.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect.poll(() => managedLinks(request), { timeout: 120_000 }).toHaveLength(0);
  await expect(link).toHaveCount(0, { timeout: 15_000 });
  await expect(rowsOf(page, 'Suggestions').filter({ hasText: pair.film.title })).toHaveCount(1);
  expect((await episode(request, pair.episode.id)).hasFile).toBe(false);
  expect(linkTarget(updatedLink.linkPath).kind).toBe('missing');
  expect(createHash('sha256').update(readFileSync(upgradedPath)).digest('hex')).toBe(sourceHash);
  expect(statSync(upgradedPath).mtimeMs).toBe(sourceStat.mtimeMs);
  expect(statSync(upgradedPath).mode).toBe(sourceStat.mode);
  expect(createHash('sha256').update(readFileSync(previous)).digest('hex')).toBe(previousHash);
});
