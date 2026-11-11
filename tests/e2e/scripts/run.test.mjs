import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import { execute } from './process.mjs';

// Exercise the real orchestrator with an executable Docker shim on the Linux CI platform.
// This verifies orchestration and cleanup; it does not stand in for a real browser/container run.
const skip = process.platform === 'win32';
const root = fileURLToPath(new URL('../', import.meta.url));
async function fixture(t, browserExit = 0) {
  const temporary = await mkdtemp(join(tmpdir(), 'lme2e-command-test-'));
  const record = join(temporary, 'calls.jsonl');
  await writeFile(
    join(temporary, 'docker'),
    `#!${process.execPath}
const fs = require('node:fs');
const path = require('node:path');
const args = process.argv.slice(2);
fs.appendFileSync(process.env.FAKE_DOCKER_RECORD, JSON.stringify({ args, artifacts: process.env.E2E_ARTIFACTS }) + '\\n');
if (args.includes('playwright')) {
  const flag = args.find((arg) => arg.startsWith('E2E_ARTIFACT_DIR='));
  const directory = path.join(process.env.E2E_ARTIFACTS, flag.slice('E2E_ARTIFACT_DIR='.length).replace(/^\\/artifacts\\/?/, ''));
  fs.mkdirSync(directory, { recursive: true });
  for (const name of ['restart-sonarr', 'stop-seerr', 'restart-seerr']) fs.writeFileSync(path.join(directory, name), 'ready');
  process.exit(Number(process.env.FAKE_BROWSER_EXIT));
}
`,
    { mode: 0o755 },
  );
  const calls = async () =>
    (await readFile(record, 'utf8'))
      .trim()
      .split('\n')
      .map((line) => JSON.parse(line));
  t.after(async () => {
    const commands = await calls().catch(() => []);
    const artifacts = commands[0]?.artifacts;
    if (artifacts) await rm(artifacts, { recursive: true, force: true });
    await rm(temporary, { recursive: true, force: true });
  });
  const env = {
    ...process.env,
    PATH: `${temporary}:${process.env.PATH}`,
    E2E_APP_IMAGE: 'shared-candidate',
    E2E_RUNNER_IMAGE: 'shared-runner',
    FAKE_DOCKER_RECORD: record,
    FAKE_BROWSER_EXIT: String(browserExit),
  };
  return {
    calls,
    run: (...args) => execute(process.execPath, ['scripts/run.mjs', ...args], { cwd: root, env, capture: true, timeout: 60_000 }),
    summary: async () => JSON.parse(await readFile(join((await calls())[0].artifacts, 'summary.json'), 'utf8')),
  };
}

test('orchestrator executes all 20 cases with one runner, one baseline restore and one final teardown', { skip }, async (t) => {
  const fixtureOfRun = await fixture(t);
  await fixtureOfRun.run('--suite', 'full');
  const calls = (await fixtureOfRun.calls()).map((item) => item.args);
  const summary = await fixtureOfRun.summary();
  assert.equal(summary.failed, false);
  assert.equal(summary.completed.length, 20);
  assert.equal(calls.filter((args) => args.includes('playwright')).length, 20);
  assert.equal(calls.filter((args) => args.includes('tar') && args.includes('-xzf')).length, 1);
  assert.equal(calls.filter((args) => args.includes('up') && args.at(-1) === 'runner').length, 1);
  assert.equal(calls.filter((args) => args.includes('down')).length, 1);
  assert.equal(
    calls.some((args) => args.includes('--rm')),
    false,
  );
  assert.equal(calls.filter((args) => args.includes('--check-offline')).length, 1);
  assert.equal(calls.filter((args) => args.includes('bootstrap/reset.mjs')).length, 20);
  assert.equal(new Set((await fixtureOfRun.calls()).map((item) => item.artifacts)).size, 1);
  assert.equal(
    calls.some((args) => args[0] === 'image' && args[1] === 'rm'),
    false,
  );
});

test('orchestrator records a browser failure and still tears down the shared stack', { skip }, async (t) => {
  const fixtureOfRun = await fixture(t, 23);
  await assert.rejects(fixtureOfRun.run('--scenario', 'E01'), (error) => error.exitCode === 23);
  const summary = await fixtureOfRun.summary();
  assert.equal(summary.failed, true);
  assert.equal(summary.exitCode, 23);
  assert.deepEqual(summary.completed, []);
  assert.equal(summary.phases.find((phase) => phase.phase === 'browser-test').succeeded, false);
  assert.equal((await fixtureOfRun.calls()).filter((item) => item.args.includes('down')).length, 1);
});
