import { expect, type Page, type APIRequestContext } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { apiKey, mutation, seerrKey } from './app.js';

export interface ExpectedLibrary {
  titles: string[];
  presentTitles: string[];
  missingTitles: string[];
  resolution720Titles: string[];
  resolution1080Titles: string[];
  movies: { id: number; title: string; hasFile: boolean; monitored: boolean }[];
  series: { id: number; title: string; monitored: boolean }[];
  files: { total: number; sonarr: number; radarr: number; videoCodec: string; audioCodec: string };
  // The special/film pair the baseline ships for the symlink scenarios.
  symlinkPair: { episodeId: number; seriesId: number; season: number; number: number; movieId: number; tmdbId: number; title: string };
  regularEpisode: { id: number; title: string; season: number; number: number; airDateUtc: string; hasFile: boolean };
  missingEpisode: { id: number; title: string; season: number; number: number; hasFile: boolean };
}
export const expected: ExpectedLibrary = JSON.parse(readFileSync(new URL('../seeds/expected-library.json', import.meta.url), 'utf8'));
export interface Instances {
  sonarrInstances: { id: string; name: string; baseUrl: string; apiKey: string }[];
  radarrInstances: { id: string; name: string; baseUrl: string; apiKey: string }[];
}
export async function configureLibrary(request: APIRequestContext, sync = true) {
  for (const kind of ['sonarr', 'radarr']) {
    await mutation(request, 'put', `/api/settings/${kind}/0`, {
      name: `E2E ${kind === 'sonarr' ? 'Sonarr' : 'Radarr'}`,
      baseUrl: `http://${kind}:${kind === 'sonarr' ? 8989 : 7878}`,
      apiKey,
    });
  }
  const instances = (await (await request.get('/api/settings/instances')).json()) as Instances;
  if (sync)
    for (const kind of ['sonarr', 'radarr'] as const)
      await mutation(request, 'post', '/api/settings/sync', {
        kind: kind === 'sonarr' ? 'Sonarr' : 'Radarr',
        instanceId: instances[`${kind}Instances`][0].id,
      });
  return instances;
}
export async function configureSeerr(request: APIRequestContext) {
  await mutation(request, 'put', '/api/settings/seerr', { baseUrl: 'http://seerr:5055', apiKey: seerrKey });
}
export async function assertTitles(page: Page, titles: string[]) {
  const links = page.getByRole('article').getByRole('link').filter({ hasText: /\S/ });
  await expect(links).toHaveText(titles);
  await expect(page.getByRole('heading', { level: 1 })).toContainText(`(${titles.length})`);
}
export async function arrRequest(request: APIRequestContext, service: 'sonarr' | 'radarr', path: string, method: 'get' | 'delete' = 'get') {
  return request[method](`http://${service}:${service === 'sonarr' ? 8989 : 7878}/api/v3${path}`, { headers: { 'X-Api-Key': apiKey } });
}
