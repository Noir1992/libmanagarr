export const scenarios = [
  { id: 'S01', services: [], mutates: [], name: 'auth', suite: 'core' },
  { id: 'S02', services: ['sonarr', 'radarr'], mutates: [], name: 'instances-library', suite: 'core' },
  { id: 'S03', services: ['seerr'], mutates: ['seerr'], name: 'seerr-requests', suite: 'core' },
  {
    id: 'S04',
    services: ['sonarr'],
    mutates: [],
    name: 'instance-recovery',
    suite: 'core',
    recovery: { service: 'sonarr', stopBeforeTest: true },
  },
  { id: 'E01', services: [], mutates: [], name: 'auth-invalid-login', suite: 'core' },
  { id: 'E02', services: [], mutates: [], name: 'account-password', suite: 'core' },
  { id: 'E03', services: [], mutates: [], name: 'account-two-factor', suite: 'core' },
  { id: 'E04', services: [], mutates: [], name: 'account-recovery', suite: 'core' },
  { id: 'E05', services: ['sonarr', 'radarr'], mutates: [], name: 'library-filters', suite: 'core' },
  { id: 'E06', services: ['sonarr', 'radarr'], mutates: [], name: 'instance-lifecycle', suite: 'core' },
  { id: 'E07', services: ['sonarr'], mutates: ['sonarr'], name: 'library-empty', suite: 'core' },
  { id: 'E08', services: ['seerr'], mutates: ['seerr'], name: 'seerr-delete', suite: 'full' },
  {
    id: 'E09',
    services: ['seerr'],
    mutates: [],
    name: 'seerr-recovery',
    suite: 'full',
    recovery: { service: 'seerr', stopBeforeTest: false },
  },
  { id: 'E10', services: ['sonarr', 'radarr'], mutates: [], name: 'tasks-manual-run', suite: 'full' },
  { id: 'E11', services: ['sonarr', 'radarr'], mutates: [], name: 'backup-preview', suite: 'core' },
  { id: 'E12', services: ['sonarr', 'radarr'], mutates: [], name: 'backup-restore', suite: 'core' },
  { id: 'E13', services: ['sonarr', 'radarr'], mutates: [], name: 'calendar', suite: 'full' },
  { id: 'E14', services: ['sonarr', 'radarr'], mutates: [], name: 'codec-statistics', suite: 'full' },
  { id: 'E15', services: ['sonarr', 'radarr'], mutates: ['sonarr', 'radarr', 'media'], name: 'symlink-confirm', suite: 'full' },
  { id: 'E16', services: ['sonarr', 'radarr'], mutates: ['media'], name: 'symlink-decline', suite: 'full' },
].map((scenario) => Object.freeze({ ...scenario, spec: `specs/${scenario.name}.spec.ts`, initialState: 'fresh-app-and-verified-seed' }));

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
    if (!Array.isArray(item.services) || !Array.isArray(item.mutates)) throw new Error(`Missing state contract: ${item.id}`);
    if (item.services.some((service) => !['sonarr', 'radarr', 'seerr'].includes(service))) throw new Error(`Unknown service: ${item.id}`);
    if (item.mutates.some((resource) => resource !== 'media' && !item.services.includes(resource)))
      throw new Error(`Invalid mutation: ${item.id}`);
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
