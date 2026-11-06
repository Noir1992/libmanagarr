import { test, expect } from '@playwright/test';
import { writeFile } from 'node:fs/promises';
import { prepareAccount, login, instanceSettings, instanceSection, mutation, apiKey } from '../fixtures/app.js';

test('connection failure is visible and succeeds after service restart', async ({ request, page }) => {
  await prepareAccount(request);
  await mutation(request, 'put', '/api/settings/sonarr/0', { name: 'E2E Sonarr', baseUrl: 'http://sonarr:8989', apiKey });
  await login(page);
  await instanceSettings(page);
  const section = instanceSection(page, 'Sonarr');
  await section.getByRole('button', { name: 'Test connection', exact: true }).click();
  await expect(section.getByText(/Connection could not be established/)).toBeVisible({ timeout: 60_000 });
  await writeFile('/artifacts/restart-sonarr', 'ready');
  await expect
    .poll(
      async () => {
        try {
          return (await request.get('http://sonarr:8989/api/v3/system/status', { headers: { 'X-Api-Key': apiKey }, timeout: 2000 })).ok();
        } catch {
          return false;
        }
      },
      { timeout: 60_000 },
    )
    .toBe(true);
  const response = page.waitForResponse(
    (response) => response.url().toLowerCase().includes('/api/settings/sonarr/0') && response.request().method() === 'PUT',
  );
  await section.getByRole('button', { name: 'Test connection', exact: true }).click();
  expect((await response).ok()).toBeTruthy();
  await expect(section.getByText(/Connection could not be established/)).toBeHidden();
  await page.reload();
  await page.getByRole('tab', { name: 'Arr instances', exact: true }).click();
  await expect(instanceSection(page, 'Sonarr').getByLabel('Name*', { exact: true })).toHaveValue('E2E Sonarr');
});
