import { test, expect } from '@playwright/test';
import { prepareAccount, login } from '../fixtures/app.js';
import { configureLibrary, expected } from '../fixtures/library.js';

test('E10: task editor persists a schedule and a manual disabled task synchronizes the library', async ({ request, page }) => {
  await prepareAccount(request);
  await configureLibrary(request, false);
  await login(page);
  await expect(page.getByRole('heading', { name: 'Library (0)', exact: true })).toBeVisible();
  await page.goto('/settings');
  await page.getByRole('tab', { name: 'Tasks', exact: true }).click();
  const row = page.getByRole('row').filter({ hasText: 'Update Sonarr/Radarr data' });
  await row.getByRole('button', { name: 'Edit', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Interval value*', { exact: true }).fill('0');
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(dialog.getByText('Enter a value within the allowed range.', { exact: true })).toBeVisible();
  await dialog.getByLabel('Interval value*', { exact: true }).fill('24');
  const enabled = dialog.getByRole('switch', { name: 'Enabled', exact: true });
  await expect(enabled).not.toBeChecked();
  await enabled.click();
  await expect(enabled).toBeChecked();
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(dialog).toBeHidden();
  await page.reload();
  await page.getByRole('tab', { name: 'Tasks', exact: true }).click();
  await row.getByRole('button', { name: 'Edit', exact: true }).click();
  await expect(dialog.getByLabel('Interval value*', { exact: true })).toHaveValue('24');
  await expect(dialog.getByRole('switch', { name: 'Enabled', exact: true })).toBeChecked();
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(dialog).toBeHidden();
  await expect(row.getByRole('switch', { name: 'Enabled', exact: true })).toBeChecked();
  const disabled = page.waitForResponse(
    (response) => response.url().endsWith('/api/settings/tasks/arrDataSync') && response.request().method() === 'PUT',
  );
  // This controlled switch settles after the save response, rather than synchronously on click.
  await row.getByRole('switch', { name: 'Enabled', exact: true }).click();
  const disabledResponse = await disabled;
  expect(disabledResponse.ok()).toBe(true);
  expect(((await disabledResponse.json()) as { enabled: boolean }).enabled).toBe(false);
  await expect(row.getByRole('switch', { name: 'Enabled', exact: true })).not.toBeChecked();
  await expect(row.getByRole('cell').nth(1)).toHaveText('N/A');
  await row.getByRole('button', { name: 'Run now', exact: true }).click();
  await expect
    .poll(
      async () => {
        const tasks = (await (await page.request.get('/api/settings/tasks')).json()) as {
          taskKey: string;
          lastRunAt: string | null;
          enabled: boolean;
        }[];
        return tasks.find((task) => task.taskKey === 'arrDataSync')?.lastRunAt;
      },
      { timeout: 60_000 },
    )
    .toBeTruthy();
  await expect(row.getByRole('switch', { name: 'Enabled', exact: true })).not.toBeChecked();
  await page.goto('/library');
  // The manual run of a disabled task is what fills the library, so its size is the seed's title count.
  await expect(page.getByRole('heading', { name: `Library (${expected.titles.length})`, exact: true })).toBeVisible();
});
