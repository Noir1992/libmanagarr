import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, readdir, rm, writeFile, symlink, stat } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { resetState } from '../bootstrap/reset.mjs';

async function fixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'lme2e-reset-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  for (const directory of ['app-state/config', 'app-state/data', 'state/sonarr', 'state/radarr', 'media']) {
    await mkdir(join(root, directory), { recursive: true });
    await writeFile(join(root, directory, 'existing'), 'original');
  }
  return root;
}

test('clears app identity, databases and caches while preserving external data', async (t) => {
  const root = await fixture(t);
  await resetState(['app'], { root });
  assert.deepEqual(await readdir(join(root, 'app-state/config')), []);
  assert.deepEqual(await readdir(join(root, 'app-state/data')), []);
  assert.equal(await readFile(join(root, 'state/sonarr/existing'), 'utf8'), 'original');
  assert.equal(await readFile(join(root, 'media/existing'), 'utf8'), 'original');
});

test('restores only the selected service after clearing all of its database sidecars', async (t) => {
  const root = await fixture(t);
  await writeFile(join(root, 'state/sonarr/sonarr.db-wal'), 'uncommitted');
  let restored;
  await resetState(['sonarr'], {
    root,
    restore: async (members) => {
      restored = members;
      assert.deepEqual(await readdir(join(root, 'state/sonarr')), []);
      await writeFile(join(root, 'state/sonarr/existing'), 'baseline');
    },
  });
  assert.deepEqual(restored, ['state/sonarr']);
  assert.equal(await readFile(join(root, 'state/sonarr/existing'), 'utf8'), 'baseline');
  assert.equal(await readFile(join(root, 'state/radarr/existing'), 'utf8'), 'original');
});

test('rejects unknown paths before removing any test data', async (t) => {
  const root = await fixture(t);
  await assert.rejects(resetState(['app', '../outside'], { root }), /known test resources/);
  assert.equal(await readFile(join(root, 'app-state/config/existing'), 'utf8'), 'original');
});

test('media reset removes added symlinks without touching their targets', async (t) => {
  const root = await fixture(t);
  // Use junctions on Windows so the test does not require developer-mode symlink privileges.
  await symlink(join(root, 'state/radarr'), join(root, 'media/added-link'), process.platform === 'win32' ? 'junction' : 'dir');
  await resetState(['media'], { root, restore: async (members) => assert.deepEqual(members, ['media']) });
  assert.deepEqual(await readdir(join(root, 'media')), []);
  assert.equal(await readFile(join(root, 'state/radarr/existing'), 'utf8'), 'original');
});

// The container and Linux CI both supply tar. Exercise the real committed archive as well as the
// injected restoration callback, including removal of files that extraction alone would leave behind.
test(
  'selectively restores the real Sonarr baseline without changing Radarr or media',
  { skip: process.platform === 'win32' },
  async (t) => {
    const root = await fixture(t);
    await writeFile(join(root, 'state/sonarr/leftover.db-wal'), 'uncommitted');
    await resetState(['sonarr'], { root });
    assert.ok((await stat(join(root, 'state/sonarr/sonarr.db'))).size > 0);
    const entries = await readdir(join(root, 'state/sonarr'));
    assert.equal(entries.includes('leftover.db-wal'), false);
    assert.equal(entries.includes('existing'), false);
    assert.equal(await readFile(join(root, 'state/radarr/existing'), 'utf8'), 'original');
    assert.equal(await readFile(join(root, 'media/existing'), 'utf8'), 'original');
  },
);
