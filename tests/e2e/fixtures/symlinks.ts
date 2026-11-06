// Preparation and read-back for the symlink scenarios. A pairing rests on one signal only — the title of
// a Sonarr special and the title of a Radarr movie — so the baseline ships one such pair (see
// bootstrap/seed.mjs) and this fixture only re-reads it from the live instances. Everything here runs in
// the runner container, which shares the media volume with the Arr services and can therefore read a
// created link back as a real Linux symbolic link.
import { expect, type APIRequestContext, type Page } from '@playwright/test';
import { existsSync, lstatSync, realpathSync } from 'node:fs';
import { mutation } from './app.js';
import { arrRequest, expected, type Instances } from './library.js';

export interface ArrSeries {
  id: number;
  title: string;
  monitored: boolean;
}
export interface ArrEpisode {
  id: number;
  seriesId: number;
  seasonNumber: number;
  episodeNumber: number;
  title: string;
  hasFile: boolean;
  // Sonarr 4 answers 0 here while the episode owns no file, so the number alone is not the association.
  episodeFileId: number;
  monitored: boolean;
}
export interface ArrMovie {
  id: number;
  tmdbId: number;
  title: string;
  year: number;
  path: string;
  hasFile: boolean;
  monitored: boolean;
}
export interface ArrMovieFile {
  id: number;
  movieId: number;
  path: string;
  size: number;
}
// A file row of Sonarr 4 names no episode of its own — that association is only on the episode side, which is
// why the scenarios read it from the episode rather than searching the file list for an identifier.
export interface ArrEpisodeFile {
  id: number;
  seriesId: number;
  path: string;
  size: number;
  mediaInfo?: { videoCodec?: string; audioCodec?: string; audioLanguages?: string };
}
export interface SeedPair {
  episode: ArrEpisode;
  series: ArrSeries;
  film: ArrMovie;
  filmFile: ArrMovieFile;
}
export interface ManagedLink {
  id: string;
  linkPath: string;
  sourcePath: string;
  radarrMovieTitle: string | null;
  appliedFilename: string | null;
  setupStep: string | null;
  operationStatus: string;
  monitoringDisabled: boolean;
}

const normalizeTitle = (value: string) =>
  value
    .normalize('NFD')
    .toLowerCase()
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[^a-z0-9]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();

export async function arrGet<T>(request: APIRequestContext, service: 'sonarr' | 'radarr', path: string): Promise<T> {
  const response = await arrRequest(request, service, path);
  expect(response.ok(), `GET ${service}${path}: ${await response.text()}`).toBe(true);
  return (await response.json()) as T;
}

export const episode = (request: APIRequestContext, id: number) => arrGet<ArrEpisode>(request, 'sonarr', `/episode/${id}`);
export const seriesOf = (request: APIRequestContext, id: number) => arrGet<ArrSeries>(request, 'sonarr', `/series/${id}`);
export const movieOf = (request: APIRequestContext, id: number) => arrGet<ArrMovie>(request, 'radarr', `/movie/${id}`);

/// The pair from the baseline, re-read live so a scenario can never be built on a stale identifier.
export async function seededPair(request: APIRequestContext): Promise<SeedPair> {
  const seed = expected.symlinkPair;
  const [filmEpisode, series, film] = await Promise.all([
    episode(request, seed.episodeId),
    seriesOf(request, seed.seriesId),
    movieOf(request, seed.movieId),
  ]);
  expect(filmEpisode.seasonNumber, 'The paired special must stay a special').toBe(seed.season);
  expect(filmEpisode.episodeNumber, 'The paired special must stay the same episode').toBe(seed.number);
  expect(filmEpisode.hasFile, 'The paired special must start without a file of its own').toBe(false);
  expect(film.tmdbId, 'The paired film must be the seeded one').toBe(seed.tmdbId);
  expect(film.hasFile, 'The paired film must own a real media file').toBe(true);
  expect(normalizeTitle(filmEpisode.title), 'The pair must share its title').toBe(normalizeTitle(film.title));
  const files = await arrGet<ArrMovieFile[]>(request, 'radarr', `/moviefile?movieId=${film.id}`);
  expect(files).toHaveLength(1);
  return { episode: filmEpisode, series, film, filmFile: files[0] };
}

/// One storage area over the shared volume, one path mapping per instance and then the probe the feature
/// insists on before writing anything. The stack shares a single volume, so both sides of a mapping are the
/// same string — but the instance still has to say which prefix it reports, because no path may be used as a
/// link source or target without a rule that claimed it.
export async function configureSymlinks(request: APIRequestContext): Promise<number> {
  const instances = await arrAppGet<Instances>(request, '/api/settings/instances');
  const saved = (await (
    await mutation(request, 'put', '/api/settings/symlinks', {
      storageAreas: [{ name: 'E2E pool', moviesRoot: '/media/movies', seriesRoot: '/media/serien' }],
      pathMappings: [
        {
          kind: 'Radarr',
          instanceId: instances.radarrInstances[0].id,
          instanceRoot: '/media/movies',
          libManagarrRoot: '/media/movies',
        },
        {
          kind: 'Sonarr',
          instanceId: instances.sonarrInstances[0].id,
          instanceRoot: '/media/serien',
          libManagarrRoot: '/media/serien',
        },
      ],
      readerViews: [],
    })
  ).json()) as { revision: number; storageAreas: { id: string }[] };
  const probe = (await (
    await mutation(request, 'post', `/api/settings/symlinks/storage-areas/${saved.storageAreas[0].id}/capability-test`, {})
  ).json()) as {
    status: string;
    detail?: string;
  };
  expect(probe.status, probe.detail ?? 'capability test').toBe('Available');
  return saved.revision;
}

export async function arrAppGet<T>(request: APIRequestContext, path: string): Promise<T> {
  const response = await request.get(path);
  expect(response.ok(), `GET ${path}: ${await response.text()}`).toBe(true);
  return (await response.json()) as T;
}

export const managedLinks = (request: APIRequestContext) => arrAppGet<ManagedLink[]>(request, '/api/settings/symlinks/links');

/// What one path really is in the shared volume, so a claim about a link can never rest on the UI alone.
export function linkTarget(linkPath: string): { kind: 'missing' | 'file' | 'link'; resolvesTo: string | null } {
  try {
    const stats = lstatSync(linkPath);
    if (!stats.isSymbolicLink()) return { kind: 'file', resolvesTo: null };
    return { kind: 'link', resolvesTo: existsSync(linkPath) ? realpathSync(linkPath) : null };
  } catch {
    return { kind: 'missing', resolvesTo: null };
  }
}

export const rowsOf = (page: Page, title: string) =>
  page
    .locator('section.card')
    .filter({ has: page.getByRole('heading', { name: title, exact: true }) })
    .locator('li.item');
