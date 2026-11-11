import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { validateBaseline } from './baseline.mjs';

const archive = Buffer.from('synthetic baseline');
const versions = { seedVersion: 1, images: { sonarr: 'image@sha256:original' } };
const manifest = { versions, sha256: createHash('sha256').update(archive).digest('hex') };

test('accepts the exact baseline and matching images', () => {
  assert.doesNotThrow(() => validateBaseline(manifest, archive, versions));
});
test('rejects altered seed bytes before restoring state', () => {
  assert.throws(() => validateBaseline(manifest, Buffer.from('tampered'), versions), /checksum/);
});
test('rejects an image upgrade paired with an old database', () => {
  assert.throws(() => validateBaseline(manifest, archive, { ...versions, images: { sonarr: 'image@sha256:changed' } }), /versions differ/);
});
