import { appendFile, readFile, readdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';

const artifacts = fileURLToPath(new URL('../artifacts/', import.meta.url));
const runs = await readdir(artifacts, { withFileTypes: true }).catch((error) => {
  if (error.code !== 'ENOENT') throw error;
  return [];
});
const reports = [];
for (const run of runs.filter((entry) => entry.isDirectory())) {
  const raw = await readFile(join(artifacts, run.name, 'summary.json'), 'utf8').catch((error) => {
    if (error.code !== 'ENOENT') throw error;
    return undefined;
  });
  if (!raw) continue;
  const summary = JSON.parse(raw);
  const totals = new Map();
  for (const phase of summary.phases ?? []) totals.set(phase.phase, (totals.get(phase.phase) ?? 0) + phase.durationSeconds);
  const rows = [...totals].map(([phase, seconds]) => `| ${phase} | ${seconds.toFixed(1)}s |`);
  reports.push(
    [
      `### E2E ${summary.project}`,
      '',
      `${summary.completed.length}/${summary.selected.length} scenarios completed in ${summary.durationSeconds}s.`,
      '',
      '| Phase | Total |',
      '| --- | ---: |',
      ...rows,
      '',
    ].join('\n'),
  );
}
const report = reports.join('\n') || 'No E2E phase timings available; the runner did not finish writing a summary.\n';
console.log(report);
if (process.env.GITHUB_STEP_SUMMARY) await appendFile(process.env.GITHUB_STEP_SUMMARY, report);
