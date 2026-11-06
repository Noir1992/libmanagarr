import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './specs',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: !!process.env.CI,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  outputDir: '/artifacts/test-results',
  reporter: [['list'], ['html', { outputFolder: '/artifacts/report', open: 'never' }], ['junit', { outputFile: '/artifacts/junit.xml' }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://app:8080',
    locale: 'en-US',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
