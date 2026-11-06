import { readFile, writeFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
import { arr, json, until } from './http.mjs';

const baseline = JSON.parse(await readFile('seeds/baseline.json'));
const expected = JSON.parse(await readFile('seeds/expected-library.json'));
const { apiKey } = JSON.parse(await readFile('seeds/seerr-key.json'));
// The symlink pairing rests on titles that stay equal only after normalization, so the restored seed has to
// still carry both sides of that pair.
const normalizeTitle = (value) =>
  value
    .normalize('NFD')
    .toLowerCase()
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[^a-z0-9]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
const status = {};
try {
  for (const service of ['sonarr', 'radarr']) status[service] = await until(`${service} readiness`, () => arr(service, '/system/status'));
  const series = await arr('sonarr', '/series');
  const movie = await arr('radarr', '/movie');
  assert.equal(series.find((value) => value.id === baseline.seriesId)?.title, baseline.seriesTitle);
  assert.equal(movie.find((value) => value.id === baseline.movieId)?.title, baseline.movieTitle);
  assert.equal(series.length, 2);
  assert.equal(movie.length, 3);
  for (const item of expected.movies) {
    const actual = movie.find((value) => value.id === item.id);
    assert.equal(actual?.title, item.title);
    assert.equal(actual?.hasFile, item.hasFile);
    assert.equal(actual?.monitored, item.monitored);
    assert.equal(actual?.tmdbId, item.tmdbId);
    assert.deepEqual(actual?.genres, item.genres);
  }
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
  const seriesFiles = await arr('sonarr', `/episodefile?seriesId=${baseline.seriesId}`);
  const movieFiles = await arr('radarr', `/moviefile?movieId=${baseline.movieId}`);
  assert.equal(seriesFiles.length, expected.files.sonarr);
  assert.equal(movieFiles.length, expected.files.radarr);
  assert.deepEqual(seriesFiles.map((file) => file.mediaInfo.resolution).sort(), ['1280x720', '1920x1080']);
  for (const file of [...seriesFiles, ...movieFiles]) {
    assert.equal(file.mediaInfo.videoCodec, expected.files.videoCodec);
    assert.equal(file.mediaInfo.audioCodec, expected.files.audioCodec);
    assert.equal(file.mediaInfo.audioLanguages, 'jpn');
  }
  const episode = (await arr('sonarr', `/episode?seriesId=${baseline.seriesId}`)).find(
    (value) => value.seasonNumber === 0 && value.episodeNumber === baseline.specialEpisode,
  );
  assert.ok(episode?.hasFile, 'Seed special must have a real media file');
  assert.ok((await arr('radarr', `/moviefile?movieId=${baseline.movieId}`)).length, 'Seed movie must have a real media file');
  // The symlink scenarios need the pair restored as it was created: film with a file, special without one.
  const paired = await arr('sonarr', `/episode/${expected.symlinkPair.episodeId}`);
  const pairedMovie = movie.find((value) => value.id === expected.symlinkPair.movieId);
  assert.equal(pairedMovie?.tmdbId, expected.symlinkPair.tmdbId);
  assert.equal(pairedMovie?.hasFile, true, 'The paired film must have a real media file');
  assert.equal(paired.hasFile, false, 'The paired special must stay without a media file');
  assert.equal(paired.seasonNumber, expected.symlinkPair.season);
  assert.equal(paired.episodeNumber, expected.symlinkPair.number);
  assert.equal(normalizeTitle(paired.title), normalizeTitle(pairedMovie?.title ?? ''), 'The pair must still share its title');
  status.seerr = await until('Seerr authenticated readiness', () =>
    json('http://seerr:5055', '/api/v1/auth/me', { headers: { 'X-Api-Key': apiKey } }),
  );
  const requests = await json('http://seerr:5055', '/api/v1/request?take=10', { headers: { 'X-Api-Key': apiKey } });
  assert.equal(requests.results.length, 1);
  assert.equal(requests.results[0].status, 1);
  const metadata = await json('http://seerr:5055', '/api/v1/movie/27205', { headers: { 'X-Api-Key': apiKey } });
  assert.equal(metadata.title, 'Inception');
  status.app = await until('App readiness', () => json('http://app:8080', '/api/auth/state'));
  assert.equal(status.app.setupRequired, true);
  // Verify the runner cannot reach an external address, independent of DNS.
  await assert.rejects(fetch('http://1.1.1.1', { signal: AbortSignal.timeout(2000) }), 'Core stack must have no external network access');
  status.offline = true;
} finally {
  await writeFile('/artifacts/bootstrap-status.json', JSON.stringify(status, null, 2));
}
