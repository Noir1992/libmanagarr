import { test } from 'node:test';
import assert from 'node:assert/strict';
import { scenarios, parseOptions, selectScenarios, verifyScenarioFiles } from './scenarios.mjs';

test('full contains all 18 scenarios and every core scenario', () => {
  assert.equal(parseOptions([]).suite, 'full');
  assert.equal(selectScenarios({}).length, 18);
  const core = selectScenarios({ suite: 'core' });
  const full = selectScenarios({ suite: 'full' });
  assert.equal(core.length, 13);
  assert.equal(full.length, 18);
  for (const scenario of core) assert.ok(full.includes(scenario));
  assert.deepEqual(
    core.slice(0, 4).map((item) => item.id),
    ['S01', 'S02', 'S03', 'S04'],
  );
});
test('selects exactly one scenario when explicitly requested', () => {
  assert.deepEqual(
    selectScenarios(parseOptions(['--scenario', 'E09'])).map((item) => item.name),
    ['seerr-recovery'],
  );
  assert.equal(parseOptions(['--suite=full', '--debug']).debug, true);
});
test('rejects unknown, empty and ambiguous selections', () => {
  assert.throws(() => parseOptions(['--suite', 'typo']), /Unknown suite/);
  assert.throws(() => parseOptions(['--scenario']), /Missing value/);
  assert.throws(() => parseOptions(['--unknown']), /Unknown option/);
  assert.throws(() => parseOptions(['--bootstrap', '--scenario=E01']), /Bootstrap cannot/);
  assert.throws(() => selectScenarios({ scenario: 'unknown' }), /No scenarios/);
  assert.throws(() => selectScenarios({}, []), /No scenarios/);
});
test('rejects duplicate cases and missing specs before any stack starts', async () => {
  assert.throws(() => selectScenarios({}, [scenarios[0], scenarios[0]]), /Duplicate scenario/);
  await assert.rejects(
    verifyScenarioFiles(selectScenarios({ scenario: 'E12' }), async () => false),
    /Missing required spec: E12/,
  );
  await assert.doesNotReject(
    verifyScenarioFiles(selectScenarios({ scenario: 'E01' }), async (path) => path === 'specs/auth-invalid-login.spec.ts'),
  );
});
