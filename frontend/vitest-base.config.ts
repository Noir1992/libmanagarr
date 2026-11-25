import { defineConfig } from 'vitest/config';

// The Angular builder merges this file into the configuration it generates for the
// `unit-test` target and owns test discovery, the TestBed setup and the environment,
// so only assertions-relevant behaviour is declared here.
export default defineConfig({
  test: {
    // Jasmine restored every spy after each spec. Vitest keeps them in place, and the
    // builder runs all spec files in one shared context (`isolate: false`), so a spy
    // that is not restored would still be active in the following spec file.
    restoreMocks: true,
  },
});
