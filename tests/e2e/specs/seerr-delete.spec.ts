import { test, expect } from '@playwright/test';
import { prepareAccount, login, seerrKey } from '../fixtures/app.js';
import { configureSeerr } from '../fixtures/library.js';

test('E08: cancelling preserves a request and confirmation deletes it in real Seerr', async ({ request, page }) => {
  await prepareAccount(request);
  await configureSeerr(request);
  await login(page);
  await page.goto('/requests');
  await expect(page.getByText('Inception', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Delete', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
  await page.reload();
  await expect(page.getByText('Inception', { exact: true })).toBeVisible();
  const upstream = async () => {
    const response = await request.get('http://seerr:5055/api/v1/request?take=10', { headers: { 'X-Api-Key': seerrKey } });
    expect(response.ok()).toBe(true);
    return (await response.json()) as { results: { id: number; status: number }[] };
  };
  const original = await upstream();
  expect(original.results).toHaveLength(1);
  expect(original.results[0].status).toBe(1);
  await page.getByRole('button', { name: 'Delete', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(page.getByText('Inception', { exact: true })).toBeHidden();
  expect((await upstream()).results).toHaveLength(0);
  await page.reload();
  await expect(page.getByText('No data available', { exact: true })).toBeVisible();
});
