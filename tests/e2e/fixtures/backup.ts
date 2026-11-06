import { expect, type Page } from '@playwright/test';
import { stat } from 'node:fs/promises';
import { credentials } from './app.js';

export const backupPassword = 'Archive-E2E-password-2026!';
export async function backupTab(page: Page) {
  await page.goto('/settings');
  await page.getByRole('tab', { name: 'Backup', exact: true }).click();
}
export async function exportBackup(page: Page) {
  await backupTab(page);
  await page.getByLabel('Backup password*', { exact: true }).fill(backupPassword);
  await page.getByLabel('Repeat password*', { exact: true }).fill(backupPassword);
  await page.getByLabel('Current password*', { exact: true }).fill(credentials.password);
  const downloaded = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download backup', exact: true }).click();
  const download = await downloaded;
  expect(await download.failure()).toBeNull();
  const path = '/artifacts/roundtrip.lmbak';
  await download.saveAs(path);
  expect((await stat(path)).size).toBeGreaterThan(0);
  return path;
}
export async function selectArchive(page: Page, path: string) {
  const chooser = page.waitForEvent('filechooser');
  await page.getByRole('button', { name: 'Choose file', exact: true }).click();
  await (await chooser).setFiles(path);
}
export async function validateArchive(page: Page, password = backupPassword) {
  await page.getByLabel('Backup password*', { exact: true }).last().fill(password);
  const response = page.waitForResponse((response) => response.url().endsWith('/api/backups/import/validate'));
  await page.getByRole('button', { name: 'Check backup', exact: true }).click();
  return response;
}
