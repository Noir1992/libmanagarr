import { createHash } from 'node:crypto';
import { isDeepStrictEqual } from 'node:util';

export const seedFileNames = ['baseline.json', 'seerr-key.json'];
const checksum = (bytes) => createHash('sha256').update(bytes).digest('hex');

export function createBaselineManifest(archive, versions, accounts, files, generatedAt = new Date().toISOString()) {
  return {
    versions,
    accounts,
    files: Object.fromEntries(seedFileNames.map((name) => [name, checksum(files[name])])),
    sha256: checksum(archive),
    generatedAt,
  };
}

export function validateBaseline(manifest, archive, versions, contract) {
  if (manifest.sha256 !== checksum(archive)) throw new Error('Seed checksum mismatch');
  if (!isDeepStrictEqual(manifest.versions, versions)) throw new Error('Seed and image versions differ; regenerate the baseline.');
  if (contract) {
    if (!isDeepStrictEqual(manifest.accounts, contract.accounts)) throw new Error('Seed test accounts differ; regenerate the baseline.');
    for (const name of seedFileNames) {
      if (!contract.files?.[name]) throw new Error(`Missing seed file: ${name}`);
      if (manifest.files?.[name] !== checksum(contract.files[name])) throw new Error(`Seed checksum mismatch: ${name}`);
    }
  }
}
