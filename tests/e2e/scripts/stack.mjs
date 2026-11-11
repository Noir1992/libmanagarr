export function requiredServices(scenario) {
  const services = new Set(scenario.services);
  if (services.has('seerr')) {
    services.add('metadata');
  }
  return [...services];
}

// One infrastructure session per invocation. Only app state is reset unconditionally;
// external databases and media are restored after an explicitly declared mutation.
export class StackSession {
  constructor({ compose, helper, phase }) {
    this.compose = compose;
    this.helper = helper;
    this.phase = phase;
    this.started = new Set();
    this.dirty = new Set();
    this.initialized = false;
    this.appRunning = false;
  }

  async prepare(scenario) {
    const phase = (name, action) => this.phase(name, action, scenario.id);
    if (!this.initialized) {
      await phase('runner-start', () => this.compose('up', '-d', 'runner'));
      await phase('baseline-restore', () => this.helper('tar', '-xzf', 'seeds/baseline.tar.gz', '-C', '/'));
      this.initialized = true;
    }
    if (this.appRunning) {
      // Stop before touching SQLite, identity keys or caches. A fresh process also resets in-memory
      // sessions and rate limits; no test-only reset endpoint is added to the application.
      await phase('app-stop', () => this.compose('stop', 'app'));
      this.appRunning = false;
    }
    const stop = new Set([...this.dirty].filter((resource) => this.started.has(resource)));
    if (this.dirty.has('media')) {
      for (const service of ['sonarr', 'radarr']) if (this.started.has(service)) stop.add(service);
    }
    if (stop.size) {
      await phase('service-stop', () => this.compose('stop', ...stop));
      for (const service of stop) this.started.delete(service);
    }
    await phase('state-reset', () => this.helper('node', 'bootstrap/reset.mjs', 'app', ...this.dirty));
    this.dirty.clear();
    const needed = requiredServices(scenario);
    await phase('stack-start', () => this.compose('up', '-d', ...needed, 'app'));
    for (const service of needed) this.started.add(service);
    this.appRunning = true;
  }

  // Mark before the browser starts: a failed test may already have changed an external service.
  markMutations(scenario) {
    for (const resource of scenario.mutates) this.dirty.add(resource);
  }
}
