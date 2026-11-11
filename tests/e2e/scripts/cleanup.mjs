import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';
import { execute } from './process.mjs';

if (!process.argv[2]) throw new Error('Usage: node scripts/cleanup.mjs <artifact-directory>');
const invocation = JSON.parse(await readFile(join(process.argv[2], 'invocation.json')));
if (!/^lme2e-[a-f0-9]{8}-[a-f0-9]{3}$/.test(invocation.project)) throw new Error('Not an E2E project name');
const env = { ...process.env };
for (const [key, value] of Object.entries(invocation.env)) {
  if (!key.startsWith('E2E_') || typeof value !== 'string') throw new Error('Invalid invocation environment');
  env[key] = value;
}
await execute(
  'docker',
  [
    'compose',
    '-f',
    fileURLToPath(new URL('../compose.yaml', import.meta.url)),
    '-p',
    invocation.project,
    'down',
    '--volumes',
    '--remove-orphans',
    '--timeout',
    '10',
  ],
  { env },
);
const images = [];
if (env.E2E_RUNNER_IMAGE === `${invocation.project}-runner`) images.push(env.E2E_RUNNER_IMAGE);
if (env.E2E_APP_IMAGE === `${invocation.project}-app`) images.push(env.E2E_APP_IMAGE);
if (images.length) await execute('docker', ['image', 'rm', ...images], { env, capture: true }).catch(() => {});
