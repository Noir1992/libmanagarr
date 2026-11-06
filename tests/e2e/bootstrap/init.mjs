import { mkdir, writeFile, chown } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { apiKey } from './http.mjs';

for (const [service, port] of [
  ['sonarr', 8989],
  ['radarr', 7878],
]) {
  await mkdir(`/state/${service}`, { recursive: true });
  await writeFile(
    `/state/${service}/config.xml`,
    `<Config><BindAddress>*</BindAddress><Port>${port}</Port><SslPort>0</SslPort><EnableSsl>False</EnableSsl><LaunchBrowser>False</LaunchBrowser><ApiKey>${apiKey}</ApiKey><AuthenticationMethod>External</AuthenticationMethod><AuthenticationRequired>DisabledForLocalAddresses</AuthenticationRequired><AnalyticsEnabled>False</AnalyticsEnabled><UpdateMechanism>Docker</UpdateMechanism></Config>`,
  );
  await chown(`/state/${service}`, 1000, 1000);
  await chown(`/state/${service}/config.xml`, 1000, 1000);
}
await mkdir('/state/seerr', { recursive: true });
await chown('/state/seerr', 1000, 1000);
for (const path of ['/media/serien', '/media/movies', '/certs']) {
  await mkdir(path, { recursive: true });
  await chown(path, 1000, 1000);
}
execFileSync(
  'openssl',
  [
    'req',
    '-x509',
    '-newkey',
    'rsa:2048',
    '-nodes',
    '-keyout',
    '/certs/metadata.key',
    '-out',
    '/certs/metadata.crt',
    '-days',
    '3650',
    '-subj',
    '/CN=api.themoviedb.org',
    '-addext',
    'subjectAltName=DNS:api.themoviedb.org',
  ],
  { stdio: 'ignore' },
);
console.log('Isolated configuration and test-only TLS certificate initialized.');
