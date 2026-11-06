import { test, expect } from '@playwright/test';
import { prepareAccount, login } from '../fixtures/app.js';
import { configureLibrary, expected } from '../fixtures/library.js';

test.use({ timezoneId: 'UTC' });

test('E13: month and agenda display the same real dated episode', async ({ request, page }) => {
  await prepareAccount(request);
  await configureLibrary(request);
  await login(page);
  await page.clock.setFixedTime(new Date(expected.regularEpisode.airDateUtc));
  const response = page.waitForResponse((response) => response.url().includes('/api/calendar?'));
  await page.goto('/calendar');
  const rangeUrl = new URL((await response).url());
  expect(rangeUrl.searchParams.get('timeZone')).toBe('UTC');
  const instant = new Date(expected.regularEpisode.airDateUtc).getTime();
  expect(new Date(rangeUrl.searchParams.get('startDate')!).getTime()).toBeLessThanOrEqual(instant);
  expect(new Date(rangeUrl.searchParams.get('endDate')!).getTime()).toBeGreaterThan(instant);
  await page.getByRole('button', { name: 'Month', exact: true }).click();
  const entry = page.getByRole('button').filter({ hasText: expected.series[0].title }).first();
  await expect(entry).toBeVisible();
  await entry.click();
  const detail = page.getByRole('dialog');
  await expect(detail.getByText(expected.regularEpisode.title, { exact: true })).toBeVisible();
  await expect(detail.getByText('S01E01', { exact: true })).toBeVisible();
  await expect(detail.getByText('15:00', { exact: true })).toBeVisible();
  await detail.getByRole('button', { name: 'Close', exact: true }).click();
  await page.getByRole('button', { name: 'Agenda', exact: true }).click();
  const agendaEntry = page.getByRole('row').filter({ hasText: expected.regularEpisode.title });
  await expect(agendaEntry.getByRole('cell', { name: '15:00', exact: true })).toBeVisible();
  await expect(agendaEntry).toContainText('Thu, 7. Apr 2016');
  await expect(agendaEntry).toContainText(`S01E01 · ${expected.regularEpisode.title}`);
  await agendaEntry.click();
  await expect(detail.getByText('S01E01', { exact: true })).toBeVisible();
});
