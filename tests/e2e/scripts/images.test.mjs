import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createServiceImagePuller } from './images.mjs';

const images = { sonarr: 'registry/sonarr@sha256:one', radarr: 'registry/radarr@sha256:two' };
const missing = () => Object.assign(new Error('image missing'), { output: 'Error: No such image' });

function fixture(
  pull = async () => {},
  inspect = async () => {
    throw missing();
  },
) {
  const calls = [];
  const waits = [];
  const ensure = createServiceImagePuller({
    images,
    execute: async (args) => {
      calls.push(args);
      return args[0] === 'pull' ? pull(args[1]) : inspect(args[2]);
    },
    wait: async (milliseconds) => {
      waits.push(milliseconds);
    },
    log: () => {},
  });
  return { ensure, calls, waits };
}

test('pulls missing service images in order and reuses them across scenarios', async () => {
  const { ensure, calls } = fixture();
  await ensure(['sonarr', 'radarr', 'sonarr', 'app']);
  await ensure(['sonarr', 'radarr']);
  assert.deepEqual(calls, [
    ['image', 'inspect', images.sonarr],
    ['pull', images.sonarr],
    ['image', 'inspect', images.radarr],
    ['pull', images.radarr],
  ]);
});

test('uses already available images without registry requests', async () => {
  const { ensure, calls } = fixture(undefined, async () => 'sha256:present');
  await ensure(['sonarr']);
  await ensure(['sonarr']);
  assert.deepEqual(calls, [['image', 'inspect', images.sonarr]]);
});

test('retries the registry retry-after failure seen in CI with bounded backoff', async () => {
  let attempts = 0;
  const { ensure, calls, waits } = fixture(async () => {
    if (++attempts < 3) throw new Error('error from registry: retry-after: 347.197µs, allowed: 44000/minute');
  });
  await ensure(['sonarr']);
  assert.equal(calls.filter((args) => args[0] === 'pull').length, 3);
  assert.deepEqual(waits, [2_000, 4_000]);
});

test('stops after four transient failures and preserves the final diagnostic', async () => {
  const failure = new Error('429 Too Many Requests');
  const { ensure, waits } = fixture(async () => {
    throw failure;
  });
  await assert.rejects(ensure(['sonarr']), (error) => error === failure);
  assert.deepEqual(waits, [2_000, 4_000, 8_000]);
});

test('does not retry permanent registry errors or hide a Docker daemon failure', async () => {
  const denied = new Error('denied: requested access to the resource is denied');
  const permanent = fixture(async () => {
    throw denied;
  });
  await assert.rejects(permanent.ensure(['sonarr']), (error) => error === denied);
  assert.deepEqual(permanent.waits, []);
  const unavailable = fixture(undefined, async () => {
    throw new Error('Cannot connect to the Docker daemon');
  });
  await assert.rejects(unavailable.ensure(['sonarr']), /Cannot connect/);
  assert.equal(unavailable.calls.length, 1);
});

test('honors cancellation instead of starting another pull', async () => {
  const controller = new AbortController();
  controller.abort();
  let called = false;
  const ensure = createServiceImagePuller({
    images,
    signal: controller.signal,
    execute: async () => {
      called = true;
    },
  });
  await assert.rejects(ensure(['sonarr']), { name: 'AbortError' });
  assert.equal(called, false);
});
