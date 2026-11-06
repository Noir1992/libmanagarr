import { fileURLToPath } from 'node:url';
import { resolve, join } from 'node:path';
import { mkdir, readFile, writeFile, copyFile, access } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import { execute } from './process.mjs';
import { validateBaseline, createBaselineManifest, seedFileNames } from './baseline.mjs';
import { parseOptions, selectScenarios, verifyScenarioFiles } from './scenarios.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const repo = resolve(root, '../..');
const versions = JSON.parse(await readFile(join(root, 'versions.json')));
const accounts = JSON.parse(await readFile(join(root, 'fixtures/accounts.json')));
const readSeedFiles = async () =>
  Object.fromEntries(await Promise.all(seedFileNames.map(async (name) => [name, await readFile(join(root, 'seeds', name))])));
const options = parseOptions(process.argv.slice(2));
const { bootstrap, debug } = options;
const selected = bootstrap ? [] : selectScenarios(options);
await verifyScenarioFiles(selected, async (spec) =>
  access(join(root, spec)).then(
    () => true,
    () => false,
  ),
);
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
const completed = [];
const timings = [];
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
  const cases = bootstrap ? [{ id: 'bootstrap', name: 'bootstrap' }] : selected;
  for (const scenario of cases) {
    if (interrupted) throw new Error('Interrupted');
    const scenarioStartedAt = Date.now();
    console.log(`Starting ${scenario.id}: ${scenario.name}`);
    env.E2E_ARTIFACTS = join(artifacts, `${scenario.id}-${scenario.name}`);
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
        await copyFile(join(env.E2E_ARTIFACTS, 'expected-library.json'), join(root, 'seeds/expected-library.json'));
        const archive = await readFile(join(root, 'seeds/baseline.tar.gz'));
        await writeFile(
          join(root, 'seeds/manifest.json'),
          JSON.stringify(createBaselineManifest(archive, versions, accounts, await readSeedFiles()), null, 2) + '\n',
        );
      } else {
        await helper('tar', '-xzf', 'seeds/baseline.tar.gz', '-C', '/');
        await compose('up', '-d', 'sonarr', 'radarr', 'seerr', 'jellyfin', 'metadata', 'app');
        if (options.failAfterStart) throw new Error('Intentional E2E lifecycle failure after stack startup');
        await helper('node', 'bootstrap/verify.mjs');
        if (scenario.recovery) {
          const { service, stopBeforeTest } = scenario.recovery;
          if (stopBeforeTest) await compose('stop', service);
          // The browser test writes a marker after displaying the failure. Only the host controls Docker.
          const test = helper('npx', 'playwright', 'test', scenario.spec);
          let settled = false;
          const result = test
            .finally(() => {
              settled = true;
            })
            .then(
              () => null,
              (error) => error,
            );
          const checkpoint = async (name) => {
            const deadline = Date.now() + 120_000;
            while (true) {
              if (
                await access(join(env.E2E_ARTIFACTS, name)).then(
                  () => true,
                  () => false,
                )
              )
                return;
              if (settled) throw (await result) ?? new Error(`Test exited before ${name}`);
              if (Date.now() > deadline || interrupted) throw new Error(`Browser did not reach ${name}`);
              await new Promise((resolve) => setTimeout(resolve, 500));
            }
          };
          if (!stopBeforeTest) {
            await checkpoint(`stop-${service}`);
            await compose('stop', service);
            await writeFile(join(env.E2E_ARTIFACTS, `${service}-stopped`), 'ready');
          }
          await checkpoint(`restart-${service}`);
          await compose('start', service);
          const error = await result;
          if (error) throw error;
        } else await helper('npx', 'playwright', 'test', scenario.spec);
      }
      completed.push(scenario.id);
    } finally {
      const logs = await execute('docker', [...composeArgs, 'logs', '--no-color'], { cwd: repo, env, capture: true }).catch(
        (error) => error.output ?? String(error),
      );
      await writeFile(join(env.E2E_ARTIFACTS, 'containers.log'), logs);
    }
    await cleanup();
    timings.push({ id: scenario.id, durationSeconds: Math.round((Date.now() - scenarioStartedAt) / 1000) });
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
      {
        project: run,
        bootstrap,
        suite: options.suite,
        selected: selected.map((item) => item.id),
        completed,
        timings,
        failed,
        exitCode: process.exitCode ?? 0,
        durationSeconds: Math.round((Date.now() - startedAt) / 1000),
      },
      null,
      2,
    ),
  );
  console.log(`Artifacts: ${artifacts}`);
}
