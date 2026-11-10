import { test } from 'node:test';
import assert from 'node:assert/strict';
import { StackSession, requiredServices } from './stack.mjs';
import { scenarios, selectScenarios } from './scenarios.mjs';

const scenario = (id) => scenarios.find((item) => item.id === id);
function fixture() {
  const calls = [];
  const running = new Set();
  const starts = new Map();
  const compose = async (...args) => {
    calls.push(['compose', ...args]);
    const services = args.filter((arg) => ['app', 'runner', 'sonarr', 'radarr', 'seerr', 'metadata'].includes(arg));
    for (const service of services) {
      if (args[0] === 'stop') running.delete(service);
      if (args[0] === 'up') {
        if (!running.has(service)) starts.set(service, (starts.get(service) ?? 0) + 1);
        running.add(service);
      }
    }
  };
  const helper = async (...args) => {
    calls.push(['helper', ...args]);
  };
  const session = new StackSession({ compose, helper, phase: (_, action) => action() });
  return { session, calls, starts, running };
}

test('login scenarios start only the app and persistent runner', async () => {
  const { session, calls, starts } = fixture();
  await session.prepare(scenario('S01'));
  await session.prepare(scenario('E01'));
  assert.deepEqual([...starts.keys()].sort(), ['app', 'runner']);
  assert.equal(starts.get('runner'), 1);
  assert.equal(calls.filter((call) => call[1] === 'tar').length, 1);
  assert.equal(calls.filter((call) => call.includes('bootstrap/reset.mjs')).length, 2);
});

test('read-only library scenarios reuse both Arr processes and their seed', async () => {
  const { session, calls, starts } = fixture();
  for (const id of ['S02', 'E05', 'E06', 'E11', 'E12', 'E13', 'E14']) {
    await session.prepare(scenario(id));
    session.markMutations(scenario(id));
  }
  assert.equal(starts.get('sonarr'), 1);
  assert.equal(starts.get('radarr'), 1);
  assert.equal(starts.get('app'), 7);
  assert.equal(
    calls.filter((call) => call[1] === 'stop').every((call) => call.length === 3 && call[2] === 'app'),
    true,
  );
  assert.equal(
    calls.some((call) => call.includes('down')),
    false,
  );
});

test('deleting the upstream library restores only Sonarr before later library tests', async () => {
  const { session, calls, starts } = fixture();
  await session.prepare(scenario('E07'));
  session.markMutations(scenario('E07'));
  calls.length = 0;
  await session.prepare(scenario('E05'));
  const stopped = calls.findIndex((call) => call[1] === 'stop' && call.includes('sonarr'));
  const restored = calls.findIndex((call) => call.includes('bootstrap/reset.mjs'));
  const restarted = calls.findIndex((call) => call[1] === 'up');
  assert.ok(stopped >= 0 && stopped < restored && restored < restarted);
  assert.deepEqual(calls[restored], ['helper', 'node', 'bootstrap/reset.mjs', 'app', 'sonarr']);
  assert.equal(starts.get('sonarr'), 2);
  assert.equal(
    calls.some((call) => call[1] === 'stop' && call.includes('radarr')),
    false,
  );
});

test('request mutations restore Seerr without restarting metadata', async () => {
  const { session, calls, starts } = fixture();
  await session.prepare(scenario('S03'));
  session.markMutations(scenario('S03'));
  await session.prepare(scenario('E08'));
  assert.ok(calls.some((call) => call.includes('bootstrap/reset.mjs') && call.includes('seerr')));
  assert.equal(starts.get('seerr'), 2);
  assert.equal(starts.get('metadata'), 1);
  assert.equal(starts.has('sonarr'), false);
  assert.equal(starts.has('radarr'), false);
});

test('restoring media stops both Arr readers and restores both databases changed by the upgrade scenario', async () => {
  const { session, calls } = fixture();
  await session.prepare(scenario('E15'));
  session.markMutations(scenario('E15'));
  calls.length = 0;
  await session.prepare(scenario('E16'));
  assert.deepEqual(calls[0], ['compose', 'stop', 'app']);
  assert.deepEqual(calls[1], ['compose', 'stop', 'sonarr', 'radarr']);
  assert.deepEqual(calls[2], ['helper', 'node', 'bootstrap/reset.mjs', 'app', 'sonarr', 'radarr', 'media']);
  assert.deepEqual(calls[3], ['compose', 'up', '-d', 'sonarr', 'radarr', 'app']);
});

test('the complete catalog restores the full baseline once and keeps external startups bounded', async () => {
  const { session, calls, starts } = fixture();
  for (const item of selectScenarios({})) {
    await session.prepare(item);
    session.markMutations(item);
  }
  assert.equal(calls.filter((call) => call[1] === 'tar').length, 1);
  assert.equal(starts.get('runner'), 1);
  assert.equal(starts.get('app'), 20);
  assert.equal(starts.get('sonarr'), 3);
  assert.equal(starts.get('radarr'), 2);
  assert.equal(starts.get('seerr'), 3);
  assert.equal(starts.get('metadata'), 1);
});

test('all single cases start independently', async () => {
  for (const item of scenarios) {
    const { session, running } = fixture();
    await session.prepare(item);
    for (const service of requiredServices(item)) assert.equal(running.has(service), true);
  }
});

test('Seerr scenarios need only Seerr and its metadata boundary', () => {
  assert.deepEqual(requiredServices(scenario('S03')).sort(), ['metadata', 'seerr']);
  assert.deepEqual(requiredServices(scenario('E08')).sort(), ['metadata', 'seerr']);
  assert.deepEqual(requiredServices(scenario('E09')).sort(), ['metadata', 'seerr']);
});
