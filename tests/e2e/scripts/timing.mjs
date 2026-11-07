import { performance } from 'node:perf_hooks';

// Store failures too: a timed-out startup must not disappear from the run summary.
export async function measurePhase(phases, phase, action, scenarioId, { now = () => performance.now(), log = console.log } = {}) {
  const started = now();
  let succeeded = false;
  log(`Starting ${scenarioId ?? 'run'}: ${phase}`);
  try {
    const result = await action();
    succeeded = true;
    return result;
  } finally {
    const durationSeconds = Math.round((now() - started) / 100) / 10;
    phases.push({ scenarioId, phase, durationSeconds, succeeded });
    log(`Finished ${scenarioId ?? 'run'}: ${phase} in ${durationSeconds}s${succeeded ? '' : ' (failed)'}`);
  }
}
