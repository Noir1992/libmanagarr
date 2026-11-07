import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import assert from 'node:assert/strict';
import { arr, json, until } from './http.mjs';

const baseline = JSON.parse(await readFile('seeds/baseline.json'));
const expected = JSON.parse(await readFile('seeds/expected-library.json'));
const { apiKey } = JSON.parse(await readFile('seeds/seerr-key.json'));
const serviceOption = process.argv.slice(2).find((argument) => argument.startsWith('--services='));
const services =
  serviceOption === undefined ? ['sonarr', 'radarr', 'seerr'] : serviceOption.slice('--services='.length).split(',').filter(Boolean);
const checkOffline = serviceOption === undefined || process.argv.includes('--check-offline');
if (services.some((service) => !['sonarr', 'radarr', 'seerr'].includes(service))) throw new Error('Unknown verification service');
const normalizeTitle = (value) =>
  value
    .normalize('NFD')
    .toLowerCase()
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[^a-z0-9]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
const status = {};

function verifyMediaInfo(files) {
  for (const file of files) {
    assert.equal(file.mediaInfo.videoCodec, expected.files.videoCodec);
    assert.equal(file.mediaInfo.audioCodec, expected.files.audioCodec);
    assert.equal(file.mediaInfo.audioLanguages, 'jpn');
  }
}

async function verifySonarr() {
  status.sonarr = await until('sonarr readiness', () => arr('sonarr', '/system/status'));
  const series = await arr('sonarr', '/series');
  assert.equal(series.find((value) => value.id === baseline.seriesId)?.title, baseline.seriesTitle);
  assert.equal(series.length, 2);
  for (const item of expected.series) {
    const actual = series.find((value) => value.id === item.id);
    assert.equal(actual?.title, item.title);
    assert.equal(actual?.tvdbId, item.tvdbId);
    assert.equal(actual?.monitored, item.monitored);
    assert.deepEqual(actual?.genres, item.genres);
  }
  const regular = await arr('sonarr', `/episode/${expected.regularEpisode.id}`);
  assert.equal(regular.hasFile, true);
  assert.equal(regular.title, expected.regularEpisode.title);
  assert.equal(regular.airDateUtc, expected.regularEpisode.airDateUtc);
  assert.equal(regular.seasonNumber, expected.regularEpisode.season);
  assert.equal(regular.episodeNumber, expected.regularEpisode.number);
  assert.equal((await arr('sonarr', `/episode/${expected.missingEpisode.id}`)).hasFile, false);
  const files = await arr('sonarr', `/episodefile?seriesId=${baseline.seriesId}`);
  assert.equal(files.length, expected.files.sonarr);
  assert.deepEqual(files.map((file) => file.mediaInfo.resolution).sort(), ['1280x720', '1920x1080']);
  verifyMediaInfo(files);
  const special = (await arr('sonarr', `/episode?seriesId=${baseline.seriesId}`)).find(
    (value) => value.seasonNumber === 0 && value.episodeNumber === baseline.specialEpisode,
  );
  assert.ok(special?.hasFile, 'Seed special must have a real media file');
  const paired = await arr('sonarr', `/episode/${expected.symlinkPair.episodeId}`);
  assert.equal(paired.hasFile, false, 'The paired special must start without a file');
  assert.equal(paired.seasonNumber, expected.symlinkPair.season);
  assert.equal(paired.episodeNumber, expected.symlinkPair.number);
  assert.equal(normalizeTitle(paired.title), normalizeTitle(expected.symlinkPair.title));
}

async function verifyRadarr() {
  status.radarr = await until('radarr readiness', () => arr('radarr', '/system/status'));
  const movies = await arr('radarr', '/movie');
  assert.equal(movies.find((value) => value.id === baseline.movieId)?.title, baseline.movieTitle);
  assert.equal(movies.length, 3);
  for (const item of expected.movies) {
    const actual = movies.find((value) => value.id === item.id);
    assert.equal(actual?.title, item.title);
    assert.equal(actual?.hasFile, item.hasFile);
    assert.equal(actual?.monitored, item.monitored);
    assert.equal(actual?.tmdbId, item.tmdbId);
    assert.deepEqual(actual?.genres, item.genres);
  }
  const files = await arr('radarr', `/moviefile?movieId=${baseline.movieId}`);
  assert.equal(files.length, expected.files.radarr);
  assert.ok(files.length, 'Seed movie must have a real media file');
  verifyMediaInfo(files);
  const paired = movies.find((value) => value.id === expected.symlinkPair.movieId);
  assert.equal(paired?.tmdbId, expected.symlinkPair.tmdbId);
  assert.equal(paired?.hasFile, true, 'The paired movie must have a real media file');
  assert.equal(normalizeTitle(paired?.title ?? ''), normalizeTitle(expected.symlinkPair.title));
}

async function verifySeerr() {
  status.seerr = await until('Seerr authenticated readiness', () =>
    json('http://seerr:5055', '/api/v1/auth/me', { headers: { 'X-Api-Key': apiKey } }),
  );
  const requests = await json('http://seerr:5055', '/api/v1/request?take=10', { headers: { 'X-Api-Key': apiKey } });
  assert.equal(requests.results.length, 1);
  assert.equal(requests.results[0].status, 1);
  const metadata = await json('http://seerr:5055', '/api/v1/movie/27205', { headers: { 'X-Api-Key': apiKey } });
  assert.equal(metadata.title, 'Inception');
}

try {
  const verifiers = { sonarr: verifySonarr, radarr: verifyRadarr, seerr: verifySeerr };
  await Promise.all([
    ...services.map((service) => verifiers[service]()),
    (async () => {
      status.app = await until('App readiness', () => json('http://app:8080', '/api/auth/state'));
      assert.equal(status.app.setupRequired, true);
    })(),
  ]);
  // The network stays the same throughout this session, so prove isolation once per invocation.
  if (checkOffline) {
    await assert.rejects(fetch('http://1.1.1.1', { signal: AbortSignal.timeout(2000) }), 'Core stack must have no external network access');
    status.offline = true;
  }
} finally {
  await writeFile(join(process.env.E2E_ARTIFACT_DIR ?? '/artifacts', 'bootstrap-status.json'), JSON.stringify(status, null, 2));
}
