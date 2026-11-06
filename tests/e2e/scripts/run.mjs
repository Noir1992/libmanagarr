import { fileURLToPath } from 'node:url';
import { resolve, join } from 'node:path';
import { mkdir, readFile, writeFile, copyFile } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import { execute } from './process.mjs';
import { validateBaseline, createBaselineManifest, seedFileNames } from './baseline.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const repo = resolve(root, '../..');
const versions = JSON.parse(await readFile(join(root, 'versions.json')));
const accounts = JSON.parse(await readFile(join(root, 'fixtures/accounts.json')));
const readSeedFiles = async () =>
  Object.fromEntries(await Promise.all(seedFileNames.map(async (name) => [name, await readFile(join(root, 'seeds', name))])));
const bootstrap = process.argv.includes('--bootstrap');
const debug = process.argv.includes('--debug');
if (debug && process.env.CI) throw new Error('Debug retention is disabled in CI.');
const run = `lme2e-${randomUUID().slice(0, 12)}`;
const startedAt = Date.now();
const artifacts = join(root, 'artifacts', run);
await mkdir(artifacts, { recursive: true });
await copyFile(join(root, 'versions.json'), join(artifacts, 'versions.json'));
const env = {
  ...process.env,
  E2E_APP_IMAGE: process.env.E2E_APP_IMAGE || `${run}-app`,
  E2E_RUNNER_IMAGE: `${run}-runner`,
  E2E_OFFLINE: String(!bootstrap),
  E2E_ARTIFACTS: artifacts,
};
for (const service of ['sonarr', 'radarr', 'seerr', 'jellyfin']) env[`E2E_${service.toUpperCase()}_IMAGE`] = versions.images[service];
await writeFile(
  join(artifacts, 'invocation.json'),
  JSON.stringify(
    {
      project: run,
      env: Object.fromEntries(
        [
          'E2E_APP_IMAGE',
          'E2E_RUNNER_IMAGE',
          'E2E_OFFLINE',
          'E2E_ARTIFACTS',
          'E2E_SONARR_IMAGE',
          'E2E_RADARR_IMAGE',
          'E2E_SEERR_IMAGE',
          'E2E_JELLYFIN_IMAGE',
        ].map((key) => [key, env[key]]),
      ),
    },
    null,
    2,
  ),
);
const composeArgs = ['compose', '-f', join(root, 'compose.yaml'), '-p', run];
const abort = new AbortController();
const compose = (...args) =>
  execute('docker', [...composeArgs, ...args], {
    cwd: repo,
    env,
    logFile: join(env.E2E_ARTIFACTS, 'orchestrator.log'),
    signal: args[0] === 'down' ? undefined : abort.signal,
  });
const helper = (...args) => compose('run', '--rm', '--no-deps', 'runner', ...args);
let failed = false;
let active = false;
const cleanup = async () => {
  if (active) {
    await compose('down', '--volumes', '--remove-orphans', '--timeout', '10');
    active = false;
  }
};
let interrupted = false;
for (const signal of ['SIGINT', 'SIGTERM'])
  process.once(signal, () => {
    interrupted = true;
    abort.abort();
  });
try {
  await execute('docker', ['info', '--format', '{{.ServerVersion}}'], { env });
  for (const [name, image] of Object.entries(versions.images)) {
    if (!/@sha256:[a-f0-9]{64}$/.test(image)) throw new Error(`Unpinned ${name} image`);
  }
  if (!bootstrap) {
    const manifest = JSON.parse(await readFile(join(root, 'seeds/manifest.json')));
    const archive = await readFile(join(root, 'seeds/baseline.tar.gz'));
    validateBaseline(manifest, archive, versions, { accounts, files: await readSeedFiles() });
    await copyFile(join(root, 'seeds/manifest.json'), join(artifacts, 'seed-manifest.json'));
  }
  await execute(
    'docker',
    [
      'build',
      '-f',
      join(root, 'Dockerfile.runner'),
      '--build-arg',
      `RUNNER_IMAGE=${versions.images.runner}`,
      '-t',
      env.E2E_RUNNER_IMAGE,
      root,
    ],
    { env },
  );
  if (!process.env.E2E_APP_IMAGE && !bootstrap) await execute('docker', ['build', '-t', env.E2E_APP_IMAGE, repo], { env });
  const scenarios = bootstrap ? ['bootstrap'] : ['auth', 'instances-library', 'seerr-requests', 'instance-recovery'];
  for (const scenario of scenarios) {
    if (interrupted) throw new Error('Interrupted');
    env.E2E_ARTIFACTS = join(artifacts, scenario);
    await mkdir(env.E2E_ARTIFACTS, { recursive: true });
    active = true; // Also clean up partially started stacks.
    try {
      if (bootstrap) {
        await helper('node', 'bootstrap/init.mjs');
        await compose('up', '-d', 'sonarr', 'radarr', 'jellyfin', 'metadata', 'seerr');
        await helper('node', 'bootstrap/seed.mjs');
        await compose('stop', 'sonarr', 'radarr', 'seerr', 'jellyfin');
        await helper('tar', '-czf', '/artifacts/baseline.tar.gz', '-C', '/', 'state', 'media', 'certs');
        await copyFile(join(env.E2E_ARTIFACTS, 'baseline.tar.gz'), join(root, 'seeds/baseline.tar.gz'));
        await copyFile(join(env.E2E_ARTIFACTS, 'baseline.json'), join(root, 'seeds/baseline.json'));
        await copyFile(join(env.E2E_ARTIFACTS, 'seerr-key.json'), join(root, 'seeds/seerr-key.json'));
        const archive = await readFile(join(root, 'seeds/baseline.tar.gz'));
        await writeFile(
          join(root, 'seeds/manifest.json'),
          JSON.stringify(createBaselineManifest(archive, versions, accounts, await readSeedFiles()), null, 2) + '\n',
        );
      } else {
        await helper('tar', '-xzf', 'seeds/baseline.tar.gz', '-C', '/');
        await compose('up', '-d', 'sonarr', 'radarr', 'seerr', 'jellyfin', 'metadata', 'app');
        if (process.argv.includes('--fail-after-start')) throw new Error('Intentional E2E lifecycle failure after stack startup');
        await helper('node', 'bootstrap/verify.mjs');
        if (scenario === 'instance-recovery') {
          await compose('stop', 'sonarr');
          // The browser test writes a marker after displaying the failure. Only the host controls Docker.
          const test = helper('npx', 'playwright', 'test', `specs/${scenario}.spec.ts`);
          const result = test.then(
            () => null,
            (error) => error,
          );
          const deadline = Date.now() + 120_000;
          while (true) {
            try {
              await readFile(join(env.E2E_ARTIFACTS, 'restart-sonarr'));
              break;
            } catch {
              // The browser has not written its checkpoint yet; keep polling until the deadline.
            }
            if (Date.now() > deadline || interrupted) throw new Error('Browser did not reach recovery checkpoint');
            await new Promise((resolve) => setTimeout(resolve, 500));
          }
          await compose('start', 'sonarr');
          const error = await result;
          if (error) throw error;
        } else await helper('npx', 'playwright', 'test', `specs/${scenario}.spec.ts`);
      }
    } finally {
      const logs = await execute('docker', [...composeArgs, 'logs', '--no-color'], { cwd: repo, env, capture: true }).catch(
        (error) => error.output ?? String(error),
      );
      await writeFile(join(env.E2E_ARTIFACTS, 'containers.log'), logs);
    }
    await cleanup();
  }
} catch (error) {
  failed = true;
  console.error(error);
  await writeFile(join(artifacts, 'failure.txt'), String(error.stack ?? error));
  process.exitCode = error.exitCode || 1;
} finally {
  if (debug && failed) console.error(`Stack retained. Cleanup: node scripts/cleanup.mjs "${artifacts}"`);
  else
    await cleanup().catch((error) => {
      console.error(error);
      process.exitCode ||= 1;
    });
  // Remove only images created for this invocation. Never remove an explicitly supplied app image.
  const images = [env.E2E_RUNNER_IMAGE, ...(!process.env.E2E_APP_IMAGE && !bootstrap ? [env.E2E_APP_IMAGE] : [])];
  if (!(debug && failed)) await execute('docker', ['image', 'rm', ...images], { env, capture: true }).catch(() => {});
  await writeFile(
    join(artifacts, 'summary.json'),
    JSON.stringify(
      { project: run, bootstrap, failed, exitCode: process.exitCode ?? 0, durationSeconds: Math.round((Date.now() - startedAt) / 1000) },
      null,
      2,
    ),
  );
  console.log(`Artifacts: ${artifacts}`);
}
