import { expect, type Page } from '@playwright/test';

export async function openFilters(page: Page) {
  const toggle = page.getByRole('button', { name: 'Filter', exact: true });
  if ((await toggle.getAttribute('aria-expanded')) !== 'true') await toggle.click();
}
export async function selectFilter(page: Page, label: string, value: string, exclude = false) {
  await openFilters(page);
  await page.getByRole('searchbox', { name: 'Find a filter', exact: true }).fill(label);
  const control = page.getByRole('combobox', { name: label, exact: true });
  if (exclude) await page.getByRole('group', { name: label, exact: true }).getByRole('button', { name: 'Exclude', exact: true }).click();
  await control.click();
  await page.getByRole('option', { name: new RegExp(`^${value}(?:\\s*\\(\\d+\\))?$`) }).click();
  await page.keyboard.press('Escape');
  // The existing outside-click backdrop closes this non-modal popover.
  await page.locator('.filter-popover-backdrop').click({ position: { x: 5, y: 5 } });
  await expect(page.getByRole('searchbox', { name: 'Find a filter', exact: true })).toBeHidden();
}
export async function clearFilters(page: Page) {
  const buttons = page.getByRole('button', { name: /^Remove filter / });
  while (await buttons.count()) {
    const label = await buttons.first().getAttribute('aria-label');
    expect(label).toBeTruthy();
    const remove = page.getByRole('button', { name: label!, exact: true });
    await remove.click();
    // Wait for Angular's removal before deciding whether another chip remains.
    await expect(remove).toHaveCount(0);
  }
}
