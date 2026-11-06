import { expect, type Page, type APIRequestContext } from '@playwright/test';
import { readFileSync } from 'node:fs';

const accounts: { administrator: { username: string; password: string } } = JSON.parse(
  readFileSync(new URL('./accounts.json', import.meta.url), 'utf8'),
);
export const credentials = { username: accounts.administrator.username, password: accounts.administrator.password };
export const apiKey = '0123456789abcdef0123456789abcdef';
export const baseline: { seriesTitle: string; movieTitle: string; specialEpisode: number; seriesId: number; movieId: number } = JSON.parse(
  readFileSync(new URL('../seeds/baseline.json', import.meta.url), 'utf8'),
);
export const seerrKey: string = JSON.parse(readFileSync(new URL('../seeds/seerr-key.json', import.meta.url), 'utf8')).apiKey;

export async function mutation(request: APIRequestContext, method: 'post' | 'put', path: string, data: unknown) {
  expect((await request.get('/api/auth/csrf')).ok()).toBeTruthy();
  const state = await request.storageState();
  const token = state.cookies.find((cookie) => cookie.name === 'XSRF-TOKEN')?.value;
  expect(token).toBeTruthy();
  const result = await request[method](path, { data, headers: { 'X-XSRF-TOKEN': decodeURIComponent(token!) } });
  expect(result.ok(), `${method} ${path}: ${await result.text()}`).toBeTruthy();
  return result;
}

export async function prepareAccount(request: APIRequestContext) {
  await mutation(request, 'post', '/api/auth/setup', { ...credentials, confirmPassword: credentials.password, language: 'En' });
}

export async function login(page: Page) {
  await page.goto('/login');
  await page.getByLabel('Username', { exact: true }).fill(credentials.username);
  await page.getByLabel('Password', { exact: true }).fill(credentials.password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/library$/);
}

export async function instanceSettings(page: Page) {
  await page.goto('/settings');
  await page.getByRole('tab', { name: 'Arr instances', exact: true }).click();
}

export function instanceSection(page: Page, kind: 'Sonarr' | 'Radarr') {
  return page
    .getByRole('heading', { name: `${kind} instances`, exact: true })
    .locator('..')
    .locator('..');
}

export async function addInstance(page: Page, kind: 'Sonarr' | 'Radarr') {
  const section = instanceSection(page, kind);
  await section.getByRole('button', { name: 'Add instance' }).click();
  await section.getByLabel('Name*', { exact: true }).fill(`E2E ${kind}`);
  await section.getByLabel('Base URL*', { exact: true }).fill(`http://${kind.toLowerCase()}:${kind === 'Sonarr' ? 8989 : 7878}`);
  await section.getByLabel('API key*', { exact: true }).fill(apiKey);
  const saved = page.waitForResponse(
    (response) => response.url().toLowerCase().includes(`/api/settings/${kind.toLowerCase()}/0`) && response.request().method() === 'PUT',
  );
  await section.getByRole('button', { name: 'Test connection', exact: true }).click();
  expect((await saved).ok()).toBeTruthy();
  await expect(section.getByRole('button', { name: 'Synchronize data' })).toBeEnabled();
}

export async function syncInstance(page: Page, kind: 'Sonarr' | 'Radarr') {
  const response = page.waitForResponse(
    (response) => response.url().endsWith('/api/settings/sync') && response.request().method() === 'POST',
  );
  await instanceSection(page, kind).getByRole('button', { name: 'Synchronize data' }).click();
  expect((await response).ok()).toBeTruthy();
}
