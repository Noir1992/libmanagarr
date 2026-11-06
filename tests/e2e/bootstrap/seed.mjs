import { readFile, writeFile, mkdir, chown } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import assert from 'node:assert/strict';
import { arr, json, until, credentials, accounts } from './http.mjs';

const versions = JSON.parse(await readFile(new URL('../versions.json', import.meta.url)));
const status = {};
try {
  for (const service of ['sonarr', 'radarr']) {
    status[service] = await until(`${service} readiness`, () => arr(service, '/system/status'));
    console.log(`${service} ${status[service].version} ready`);
    const root = service === 'sonarr' ? '/media/serien' : '/media/movies';
    await arr(service, '/rootfolder', { method: 'POST', body: { path: root } });
  }
  await until('Jellyfin readiness', () => json('http://jellyfin:8096', '/Startup/Configuration'));
  await json('http://jellyfin:8096', '/Startup/Configuration', {
    method: 'POST',
    body: { UICulture: 'en-US', MetadataCountryCode: 'US', PreferredMetadataLanguage: 'en' },
  });
  await json('http://jellyfin:8096', '/Startup/User');
  await json('http://jellyfin:8096', '/Startup/User', {
    method: 'POST',
    body: { Name: credentials.username, Password: credentials.password },
  });
  await json('http://jellyfin:8096', '/Startup/RemoteAccess', {
    method: 'POST',
    body: { EnableRemoteAccess: true, EnableAutomaticPortMapping: false },
  });
  await json('http://jellyfin:8096', '/Startup/Complete', { method: 'POST' });
  await until('Seerr readiness', () => json('http://seerr:5055', '/api/v1/settings/public'));
  const login = await fetch('http://seerr:5055/api/v1/auth/jellyfin', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      ...credentials,
      hostname: 'jellyfin',
      port: 8096,
      useSsl: false,
      urlBase: '',
      serverType: 2,
      email: accounts.administrator.email,
    }),
    signal: AbortSignal.timeout(30_000),
  });
  if (!login.ok) throw new Error(`Seerr onboarding: ${login.status} ${await login.text()}`);
  // Read the generated test key after normal onboarding, as a human would from settings.
  const { main } = JSON.parse(await readFile('/state/seerr/settings.json', 'utf8'));
  await writeFile('/artifacts/seerr-key.json', JSON.stringify({ apiKey: main.apiKey }));
  const headers = { 'X-Api-Key': main.apiKey };
  await json('http://seerr:5055', '/api/v1/settings/main', {
    method: 'POST',
    headers,
    body: { initialized: true, defaultPermissions: accounts.requester.permissions },
  });
  status.seerrUser = await json('http://seerr:5055', '/api/v1/auth/me', { headers });
  console.log('Jellyfin and Seerr onboarded using synthetic accounts');
  // A separate non-admin requester keeps the request pending and never dispatches a download.
  const user = await json('http://seerr:5055', '/api/v1/user', {
    method: 'POST',
    headers,
    body: {
      email: accounts.requester.email,
      username: accounts.requester.username,
      password: accounts.requester.password,
      permissions: accounts.requester.permissions,
    },
  });
  status.request = await json('http://seerr:5055', '/api/v1/request', {
    method: 'POST',
    headers: { ...headers, 'X-API-User': String(user.id) },
    body: { mediaType: 'movie', mediaId: versions.media.requestTmdbId },
  });
  if (status.request.status !== 1) throw new Error('Seed request must be pending.');
  console.log('Real pending Seerr request created');

  const seriesLookup = await arr('sonarr', `/series/lookup?term=tvdb:${versions.media.seriesTvdbId}`);
  if (seriesLookup.length !== 1) throw new Error('Series metadata could not be resolved.');
  const profiles = await arr('sonarr', '/qualityprofile');
  const series = await arr('sonarr', '/series', {
    method: 'POST',
    body: {
      ...seriesLookup[0],
      qualityProfileId: profiles[0].id,
      rootFolderPath: '/media/serien',
      monitored: false,
      seasonFolder: true,
      addOptions: { searchForMissingEpisodes: false },
    },
  });
  const episodes = await until('series episode metadata', async () => {
    const values = await arr('sonarr', `/episode?seriesId=${series.id}`);
    return values.some((value) => value.seasonNumber === 0) && values;
  });
  const special = episodes.find((value) => value.seasonNumber === 0);
  console.log(`Resolved ${series.title} special S00E${special.episodeNumber}`);
  const dir = `${series.path}/Season 00`;
  await mkdir(dir, { recursive: true });
  const episodePath = `${dir}/${series.title} - S00E${String(special.episodeNumber).padStart(2, '0')} - E2E WEBDL-1080p.mkv`;
  const movieLookup = await arr('radarr', `/movie/lookup?term=tmdb:${versions.media.movieTmdbId}`);
  const movieProfiles = await arr('radarr', '/qualityprofile');
  const movie = await arr('radarr', '/movie', {
    method: 'POST',
    body: {
      ...movieLookup[0],
      qualityProfileId: movieProfiles[0].id,
      rootFolderPath: '/media/movies',
      monitored: false,
      minimumAvailability: 'released',
      addOptions: { searchForMovie: false },
    },
  });
  await mkdir(movie.path, { recursive: true });
  const moviePath = `${movie.path}/${movie.title} (${movie.year}) - E2E WEBDL-1080p.mkv`;
  for (const path of [episodePath, moviePath]) {
    execFileSync('ffmpeg', [
      '-hide_banner',
      '-loglevel',
      'error',
      '-f',
      'lavfi',
      '-i',
      'color=c=blue:s=1920x1080:r=24',
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
      'language=jpn',
      '-pix_fmt',
      'yuv420p',
      '-y',
      path,
    ]);
    await chown(path, 1000, 1000);
  }
  for (const [service, body] of [
    ['sonarr', { name: 'RescanSeries', seriesId: series.id }],
    ['radarr', { name: 'RescanMovie', movieId: movie.id }],
  ]) {
    const command = await arr(service, '/command', { method: 'POST', body });
    await until(`${service} media scan`, async () => {
      const state = await arr(service, `/command/${command.id}`);
      if (state.status === 'failed') throw new Error(JSON.stringify(state));
      return state.status === 'completed';
    });
  }
  status.episodeFile = await until('Sonarr episode file', async () => (await arr('sonarr', `/episodefile?seriesId=${series.id}`))[0]);
  status.movieFile = await until('Radarr movie file', async () => (await arr('radarr', `/moviefile?movieId=${movie.id}`))[0]);
  const secondMovieLookup = await arr('radarr', `/movie/lookup?term=tmdb:${versions.media.secondMovieTmdbId}`);
  const secondMovie = await arr('radarr', '/movie', {
    method: 'POST',
    body: {
      ...secondMovieLookup[0],
      qualityProfileId: movieProfiles[0].id,
      rootFolderPath: '/media/movies',
      monitored: true,
      minimumAvailability: 'released',
      addOptions: { searchForMovie: false },
    },
  });
  const secondSeriesLookup = await arr('sonarr', `/series/lookup?term=tvdb:${versions.media.secondSeriesTvdbId}`);
  const secondSeries = await arr('sonarr', '/series', {
    method: 'POST',
    body: {
      ...secondSeriesLookup[0],
      qualityProfileId: profiles[0].id,
      rootFolderPath: '/media/serien',
      monitored: true,
      seasonFolder: true,
      addOptions: { searchForMissingEpisodes: false },
    },
  });
  const regular = episodes.find((value) => value.seasonNumber === 1 && value.episodeNumber === 1);
  const missing = episodes.find((value) => value.seasonNumber === 1 && value.episodeNumber === 2);
  if (!regular || !missing) throw new Error('Baseline requires regular episodes 1 and 2');
  const regularDir = `${series.path}/Season 01`;
  await mkdir(regularDir, { recursive: true });
  const regularPath = `${regularDir}/${series.title} - S01E01 - E2E WEBDL-720p.mkv`;
  execFileSync('ffmpeg', [
    '-hide_banner',
    '-loglevel',
    'error',
    '-f',
    'lavfi',
    '-i',
    'color=c=green:s=1280x720:r=24',
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
    'language=jpn',
    '-y',
    regularPath,
  ]);
  await chown(regularPath, 1000, 1000);
  await arr('sonarr', '/command', { method: 'POST', body: { name: 'RescanSeries', seriesId: series.id } });
  await until('regular episode imported', async () => (await arr('sonarr', `/episode/${regular.id}`)).hasFile);
  await until('second series metadata', async () => (await arr('sonarr', `/episode?seriesId=${secondSeries.id}`)).length);
  const expected = {
    titles: [secondSeries.title, series.title, secondMovie.title, movie.title].sort((a, b) => a.localeCompare(b)),
    presentTitles: [series.title, movie.title].sort((a, b) => a.localeCompare(b)),
    missingTitles: [secondSeries.title, secondMovie.title].sort((a, b) => a.localeCompare(b)),
    resolution720Titles: [series.title],
    resolution1080Titles: [series.title, movie.title].sort((a, b) => a.localeCompare(b)),
    movies: [
      { id: movie.id, tmdbId: movie.tmdbId, title: movie.title, genres: movie.genres, hasFile: true, monitored: false },
      {
        id: secondMovie.id,
        tmdbId: secondMovie.tmdbId,
        title: secondMovie.title,
        genres: secondMovie.genres,
        hasFile: false,
        monitored: true,
      },
    ],
    series: [
      { id: series.id, tvdbId: series.tvdbId, title: series.title, genres: series.genres, monitored: false },
      { id: secondSeries.id, tvdbId: secondSeries.tvdbId, title: secondSeries.title, genres: secondSeries.genres, monitored: true },
    ],
    files: { total: 3, sonarr: 2, radarr: 1, videoCodec: 'x264', audioCodec: 'AAC', audioLanguage: 'Japanese' },
    specialEpisode: { id: special.id, season: 0, number: special.episodeNumber, hasFile: true },
    regularEpisode: { id: regular.id, title: regular.title, season: 1, number: 1, airDateUtc: regular.airDateUtc, hasFile: true },
    missingEpisode: { id: missing.id, title: missing.title, season: 1, number: 2, hasFile: false },
  };
  for (const file of [status.episodeFile, status.movieFile, ...(await arr('sonarr', `/episodefile?seriesId=${series.id}`))]) {
    assert.equal(file.mediaInfo.videoCodec, expected.files.videoCodec);
    assert.equal(file.mediaInfo.audioCodec, expected.files.audioCodec);
    assert.equal(file.mediaInfo.audioLanguages, 'jpn');
  }
  await writeFile('/artifacts/expected-library.json', JSON.stringify(expected, null, 2) + '\n');
  console.log('Real video/audio files indexed in Sonarr and Radarr');
  status.media = {
    seriesTitle: series.title,
    movieTitle: movie.title,
    specialEpisode: special.episodeNumber,
    seriesId: series.id,
    movieId: movie.id,
    secondMovieId: secondMovie.id,
    secondSeriesId: secondSeries.id,
  };
  await writeFile('/artifacts/baseline.json', JSON.stringify(status.media, null, 2));
} finally {
  await writeFile('/artifacts/bootstrap-status.json', JSON.stringify(status, null, 2));
}
