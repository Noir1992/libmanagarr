import { test, expect } from '@playwright/test';
import { prepareAccount, login, instanceSettings, seerrKey } from '../fixtures/app.js';

test('decline a pending real Seerr request through the UI', async ({ request, page }) => {
  await prepareAccount(request);
  await login(page);
  await instanceSettings(page);
  const section = page.getByRole('heading', { name: 'Seerr', exact: true }).locator('..').locator('..');
  await section.getByLabel('Base URL*', { exact: true }).fill('http://seerr:5055');
  await section.getByLabel('API key*', { exact: true }).fill(seerrKey);
  const saved = page.waitForResponse((response) => response.url().endsWith('/api/settings/seerr') && response.request().method() === 'PUT');
  await section.getByRole('button', { name: 'Test connection', exact: true }).click();
  expect((await saved).ok()).toBeTruthy();
  await page.goto('/requests');
  await expect(page.getByText('Inception', { exact: true })).toBeVisible();
  await expect(page.getByText('Pending', { exact: true }).last()).toBeVisible();
  await page.getByRole('button', { name: 'Decline', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('textbox').fill('Declined by browser E2E');
  await dialog.getByRole('button', { name: /Decline/, exact: false }).click();
  await expect(page.getByText('Declined', { exact: true })).toBeVisible();
  const upstream = await request.get('http://seerr:5055/api/v1/request?take=10', { headers: { 'X-Api-Key': seerrKey } });
  expect(upstream.ok()).toBeTruthy();
  expect((await upstream.json()).results[0].status).toBe(3);
  await page.reload();
  await expect(page.getByText('Declined', { exact: true })).toBeVisible();
});
