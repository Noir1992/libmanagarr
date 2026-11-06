import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createBaselineManifest, validateBaseline } from './baseline.mjs';

function fixture() {
  const archive = Buffer.from('synthetic baseline');
  const versions = { seedVersion: 1, images: { sonarr: 'image@sha256:original' } };
  const contract = {
    accounts: { administrator: { username: 'test-admin', password: 'synthetic-password' } },
    files: { 'baseline.json': Buffer.from('{"seriesId":1}'), 'seerr-key.json': Buffer.from('{"apiKey":"synthetic"}') },
  };
  const manifest = createBaselineManifest(archive, versions, contract.accounts, contract.files, '2026-11-06T00:00:00.000Z');
  return { archive, versions, contract, manifest };
}

test('records accounts and both seed sidecars and accepts the complete contract', () => {
  const { manifest, archive, versions, contract } = fixture();
  assert.deepEqual(manifest.accounts, contract.accounts);
  assert.deepEqual(Object.keys(manifest.files), ['baseline.json', 'seerr-key.json']);
  assert.match(manifest.files['baseline.json'], /^[a-f0-9]{64}$/);
  assert.match(manifest.files['seerr-key.json'], /^[a-f0-9]{64}$/);
  assert.equal(manifest.generatedAt, '2026-11-06T00:00:00.000Z');
  assert.doesNotThrow(() => validateBaseline(manifest, archive, versions, contract));
});

test('rejects account drift against a previously generated seed', () => {
  const { manifest, archive, versions, contract } = fixture();
  contract.accounts = { administrator: { username: 'different-admin', password: 'different-password' } };
  assert.throws(() => validateBaseline(manifest, archive, versions, contract), /Seed test accounts differ/);
});

test('rejects a manifest without an account contract', () => {
  const { manifest, archive, versions, contract } = fixture();
  delete manifest.accounts;
  assert.throws(() => validateBaseline(manifest, archive, versions, contract), /Seed test accounts differ/);
});

for (const name of ['baseline.json', 'seerr-key.json']) {
  test(`rejects altered ${name} bytes before restoring state`, () => {
    const { manifest, archive, versions, contract } = fixture();
    contract.files[name] = Buffer.from('tampered');
    assert.throws(() => validateBaseline(manifest, archive, versions, contract), { message: `Seed checksum mismatch: ${name}` });
  });
}

test('rejects a missing seed sidecar', () => {
  const { manifest, archive, versions, contract } = fixture();
  delete contract.files['seerr-key.json'];
  assert.throws(() => validateBaseline(manifest, archive, versions, contract), { message: 'Missing seed file: seerr-key.json' });
});

test('rejects a missing sidecar checksum', () => {
  const { manifest, archive, versions, contract } = fixture();
  delete manifest.files['baseline.json'];
  assert.throws(() => validateBaseline(manifest, archive, versions, contract), { message: 'Seed checksum mismatch: baseline.json' });
});

test('accepts equivalent version metadata with a different key order', () => {
  const { manifest, archive, versions, contract } = fixture();
  const reordered = { images: versions.images, seedVersion: versions.seedVersion };
  assert.doesNotThrow(() => validateBaseline(manifest, archive, reordered, contract));
});
