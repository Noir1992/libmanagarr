import { readFile, writeFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
import { arr, json, until } from './http.mjs';

const baseline = JSON.parse(await readFile('seeds/baseline.json'));
const { apiKey } = JSON.parse(await readFile('seeds/seerr-key.json'));
const status = {};
try {
  for (const service of ['sonarr', 'radarr']) status[service] = await until(`${service} readiness`, () => arr(service, '/system/status'));
  const series = await arr('sonarr', '/series');
  const movie = await arr('radarr', '/movie');
  assert.equal(series.find((value) => value.id === baseline.seriesId)?.title, baseline.seriesTitle);
  assert.equal(movie.find((value) => value.id === baseline.movieId)?.title, baseline.movieTitle);
  const episode = (await arr('sonarr', `/episode?seriesId=${baseline.seriesId}`)).find(
    (value) => value.seasonNumber === 0 && value.episodeNumber === baseline.specialEpisode,
  );
  assert.ok(episode?.hasFile, 'Seed special must have a real media file');
  assert.ok((await arr('radarr', `/moviefile?movieId=${baseline.movieId}`)).length, 'Seed movie must have a real media file');
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
