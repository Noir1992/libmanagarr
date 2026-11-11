import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execute } from './process.mjs';

test('passes arguments literally without shell expansion', async () => {
  const payload = 'space $HOME $(whoami) `literal` ; & "quote"';
  const output = await execute(process.execPath, ['-e', 'process.stdout.write(process.argv[1])', payload], { capture: true });
  assert.equal(output, payload);
});

test('preserves a failing tool exit code and diagnostic output', async () => {
  await assert.rejects(
    execute(process.execPath, ['-e', 'console.error("synthetic failure"); process.exit(23)'], { capture: true }),
    (error) => {
      assert.equal(error.exitCode, 23);
      assert.match(error.output, /synthetic failure/);
      return true;
    },
  );
});

test('bounds a hung child process', async () => {
  await assert.rejects(execute(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], { capture: true, timeout: 100 }), /failed/);
});
