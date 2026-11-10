import { test } from 'node:test';
import assert from 'node:assert/strict';
import { measurePhase } from './timing.mjs';

test('records elapsed phase time and returns the action result', async () => {
  const phases = [];
  const ticks = [100, 2350];
  const result = await measurePhase(phases, 'startup', async () => 42, 'E01', { now: () => ticks.shift(), log: () => {} });
  assert.equal(result, 42);
  assert.deepEqual(phases, [{ scenarioId: 'E01', phase: 'startup', durationSeconds: 2.3, succeeded: true }]);
});

test('records a failed phase and preserves its error', async () => {
  const phases = [];
  const ticks = [100, 600];
  const failure = new Error('Startup failed');
  await assert.rejects(
    measurePhase(
      phases,
      'startup',
      async () => {
        throw failure;
      },
      'E02',
      { now: () => ticks.shift(), log: () => {} },
    ),
    (error) => error === failure,
  );
  assert.deepEqual(phases, [{ scenarioId: 'E02', phase: 'startup', durationSeconds: 0.5, succeeded: false }]);
});
