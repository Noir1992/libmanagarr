import { readdir, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { execFileSync } from 'node:child_process';

const directories = {
  app: ['app-state/config', 'app-state/data'],
  sonarr: ['state/sonarr'],
  radarr: ['state/radarr'],
  seerr: ['state/seerr'],
  media: ['media'],
};

// Only called with the owning processes stopped. Empty mount contents rather than removing the
// mount itself. Removing an entry that is a symlink removes that entry and never follows its target.
export async function resetState(resources, { root = '/', restore } = {}) {
  if (!resources.length || resources.some((resource) => !Object.hasOwn(directories, resource))) {
    throw new Error('Reset requires known test resources: app, sonarr, radarr, seerr, media');
  }
  const selected = [...new Set(resources)];
  for (const resource of selected) {
    for (const directory of directories[resource]) {
      const target = join(root, directory);
      const entries = await readdir(target, { withFileTypes: true });
      await Promise.all(entries.map((entry) => rm(join(target, entry.name), { recursive: true, force: true })));
    }
  }
  const members = selected.filter((resource) => resource !== 'app').flatMap((resource) => directories[resource]);
  if (members.length) {
    if (restore) await restore(members);
    else execFileSync('tar', ['-xzf', fileURLToPath(new URL('../seeds/baseline.tar.gz', import.meta.url)), '-C', root, ...members]);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) await resetState(process.argv.slice(2));
