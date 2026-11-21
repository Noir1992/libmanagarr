import { expect, test } from '@playwright/test';
import { login, mutation, prepareAccount } from '../fixtures/app.js';

test('E18: one stream per client updates other clients without recurring list polling', async ({ request, page, context }) => {
  await prepareAccount(request);
  await login(page);
  const observer = await context.newPage();
  const listReads: string[] = [];
  let streams = 0;
  observer.on('request', (request) => {
    const path = new URL(request.url()).pathname;
    if (/\/symlinks\/(links|suggestions|rejections)$/.test(path)) listReads.push(path);
    if (path.endsWith('/symlinks/events')) streams++;
  });
  const initial = observer.waitForResponse((response) => response.url().endsWith('/symlinks/rejections'));
  await observer.goto('/symlinks');
  await initial;
  await expect(observer.getByRole('heading', { name: 'Symlinks', exact: true })).toBeVisible();
  await expect.poll(() => streams).toBe(1);

  // A save from a separate HTTP client must invalidate the observer and only update the menu on Library.
  const menuLists: string[] = [];
  page.on('request', (request) => {
    if (/\/symlinks\/(links|suggestions|rejections)$/.test(new URL(request.url()).pathname)) menuLists.push(request.url());
  });
  const observerUpdate = observer.waitForResponse((response) => response.url().endsWith('/symlinks/rejections'));
  const countUpdate = page.waitForResponse((response) => response.url().endsWith('/symlinks/count'));
  await mutation(page.request, 'put', '/api/settings/symlinks', { mediaRoot: process.env.E2E_MEDIA_ROOT ?? '/media', enabled: false });
  await observerUpdate;
  await countUpdate;
  await expect(observer.getByRole('status')).toContainText('Symlinks are disabled.');
  expect(menuLists).toHaveLength(0);

  // Advance both former polling intervals. Keepalives never cause list requests.
  await observer.clock.install();
  await observer.clock.runFor(1000);
  const settledReads = listReads.length;
  await observer.clock.runFor(65000);
  expect(listReads).toHaveLength(settledReads);
  expect(streams).toBe(1);
  await observer.close();
});
