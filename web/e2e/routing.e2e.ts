import { expect, test } from '@playwright/test';

test('a deep link to a screen loads the app on that screen', async ({ page }) => {
  const response = await page.goto('/history');

  expect(response?.status()).toBe(200);
  expect(response?.headers()['content-type']).toContain('text/html');
  expect(response?.headers()['cache-control']).toBe('no-cache');
  await expect(page).toHaveURL(/\/history$/);
  await expect(page.getByRole('heading', { name: 'History' })).toBeVisible();
});

test('the root path loads the app', async ({ page }) => {
  const response = await page.goto('/');

  expect(response?.status()).toBe(200);
  await expect(page.getByRole('link', { name: 'Concepts' })).toBeVisible();
});

test('an unknown API path is a 404, not the app', async ({ request }) => {
  const response = await request.get('/api/nope');

  expect(response.status()).toBe(404);
  expect(response.headers()['content-type'] ?? '').not.toContain('text/html');
});

test('a wrong method on an API route is still a 405', async ({ request }) => {
  const response = await request.get('/api/search');

  expect(response.status()).toBe(405);
});

test('the licence notices of the bundled libraries are served', async ({ request }) => {
  const response = await request.get('/3rdpartylicenses.txt');

  expect(response.status()).toBe(200);
  expect(await response.text()).toContain('Package: @angular/core');
});

test('the health check still answers', async ({ request }) => {
  const response = await request.get('/api/health');

  expect(response.status()).toBe(200);
  expect(await response.json()).toMatchObject({ status: 'ok' });
});
