export const scenarios = [
  { id: 'S01', name: 'auth', suite: 'core' },
  { id: 'S02', name: 'instances-library', suite: 'core' },
  { id: 'S03', name: 'seerr-requests', suite: 'core' },
  { id: 'S04', name: 'instance-recovery', suite: 'core', recovery: { service: 'sonarr', stopBeforeTest: true } },
  { id: 'E01', name: 'auth-invalid-login', suite: 'core' },
  { id: 'E02', name: 'account-password', suite: 'core' },
  { id: 'E03', name: 'account-two-factor', suite: 'core' },
  { id: 'E04', name: 'account-recovery', suite: 'core' },
  { id: 'E05', name: 'library-filters', suite: 'core' },
  { id: 'E06', name: 'instance-lifecycle', suite: 'core' },
  { id: 'E07', name: 'library-empty', suite: 'core' },
  { id: 'E08', name: 'seerr-delete', suite: 'full' },
  { id: 'E09', name: 'seerr-recovery', suite: 'full', recovery: { service: 'seerr', stopBeforeTest: false } },
  { id: 'E10', name: 'tasks-manual-run', suite: 'full' },
  { id: 'E11', name: 'backup-preview', suite: 'core' },
  { id: 'E12', name: 'backup-restore', suite: 'core' },
  { id: 'E13', name: 'calendar', suite: 'full' },
  { id: 'E14', name: 'codec-statistics', suite: 'full' },
].map((scenario) => Object.freeze({ ...scenario, spec: `specs/${scenario.name}.spec.ts`, initialState: 'fresh-app-and-seed' }));

export function parseOptions(args) {
  const options = { suite: 'full', scenario: undefined, bootstrap: false, debug: false, failAfterStart: false };
  for (let index = 0; index < args.length; index++) {
    const [flag, inline] = args[index].split('=');
    if (['--suite', '--scenario'].includes(flag)) {
      const value = inline ?? args[++index];
      if (!value || value.startsWith('--')) throw new Error(`Missing value for ${flag}`);
      options[flag.slice(2)] = value;
    } else if (inline !== undefined) throw new Error(`Unknown option: ${args[index]}`);
    else if (flag === '--bootstrap') options.bootstrap = true;
    else if (flag === '--debug') options.debug = true;
    else if (flag === '--fail-after-start') options.failAfterStart = true;
    else throw new Error(`Unknown option: ${flag}`);
  }
  if (options.bootstrap && options.scenario) throw new Error('Bootstrap cannot select a browser scenario');
  if (!['core', 'full'].includes(options.suite)) throw new Error(`Unknown suite: ${options.suite}`);
  return options;
}

export function selectScenarios({ suite = 'full', scenario }, catalog = scenarios) {
  if (!['core', 'full'].includes(suite)) throw new Error(`Unknown suite: ${suite}`);
  const ids = new Set();
  const specs = new Set();
  for (const item of catalog) {
    if (ids.has(item.id) || specs.has(item.spec)) throw new Error(`Duplicate scenario: ${item.id}`);
    if (!['core', 'full'].includes(item.suite)) throw new Error(`Invalid scenario suite: ${item.id}`);
    ids.add(item.id);
    specs.add(item.spec);
  }
  const selected = scenario
    ? catalog.filter((item) => item.id === scenario || item.name === scenario)
    : catalog.filter((item) => suite === 'full' || item.suite === 'core');
  if (!selected.length) throw new Error(`No scenarios selected${scenario ? `: ${scenario}` : ''}`);
  return selected;
}

export async function verifyScenarioFiles(selected, exists) {
  for (const scenario of selected) {
    if (!(await exists(scenario.spec))) throw new Error(`Missing required spec: ${scenario.id} (${scenario.spec})`);
  }
}
