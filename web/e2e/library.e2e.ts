import { expect, test } from '@playwright/test';

// One source goes through its whole life in order, so each step starts where the last one ended.
test.describe.configure({ mode: 'serial' });

const question = 'when is the tidal range largest';
// Ingestion embeds the document with the local model, which can take a while on a cold start.
const ingested = { timeout: 90_000 };

test('an uploaded source is processed and listed as ready', async ({ page }) => {
  await page.goto('/sources');
  await expect(page.getByText('No sources yet.')).toBeVisible();

  await page.locator('input[type=file]').setInputFiles('e2e/fixtures/tides.md');

  const source = page.locator('li.source', { hasText: 'tides.md' });
  await expect(source.locator('.status-badge')).toHaveText('Ready', ingested);
  await expect(source).toContainText(/\d+ chunks?/);
});

test('a search shows the passage that answers it under its source', async ({ page }) => {
  await page.goto('/search');
  await page.getByLabel('Question').fill(question);
  await page.getByRole('button', { name: 'Search', exact: true }).click();

  await expect(page.getByRole('heading', { name: `Results for “${question}”` })).toBeVisible();
  await expect(page.getByRole('link', { name: 'tides' }).first()).toBeVisible();
  await expect(page.getByText('These are spring tides').first()).toBeVisible();
  await expect(page.getByText('Page 1')).toHaveCount(0);
});

test('the search is kept in history with its passages', async ({ page }) => {
  await page.goto('/history');
  await page.getByRole('link', { name: question }).click();

  await expect(page.getByRole('heading', { name: question })).toBeVisible();
  await expect(page.getByText('These are spring tides').first()).toBeVisible();
});

test('a source can be added to a new notebook', async ({ page }) => {
  await page.goto('/sources');
  await page.getByRole('button', { name: 'New notebook' }).click();
  await page.getByLabel('Notebook name').fill('Oceans');
  await page.getByRole('button', { name: 'Create' }).click();

  await page.getByLabel('Add to a notebook').selectOption({ label: 'Oceans' });

  const notebook = page.getByRole('button', { name: /^Oceans/ });
  await expect(notebook).toContainText('1');
  await notebook.click();
  await expect(page.locator('li.source', { hasText: 'tides.md' })).toBeVisible();
});

test('processing a source again keeps the passages already in history', async ({ page }) => {
  await page.goto('/sources');
  const source = page.locator('li.source', { hasText: 'tides.md' });
  await source.getByRole('button', { name: 'Process again' }).click();
  await expect(source.locator('.status-badge')).toHaveText('Ready', ingested);
  await expect(source.getByRole('button', { name: 'Process again' })).toBeVisible(ingested);

  await page.goto('/history');
  await page.getByRole('link', { name: question }).click();
  await expect(page.getByText('These are spring tides').first()).toBeVisible();
  await expect(page.getByText('Source no longer available')).toHaveCount(0);
});

test('a tagged source can be found by its tag, and a search limited to it', async ({ page }) => {
  await page.goto('/sources');
  await page.getByRole('button', { name: 'Edit tags' }).click();
  for (const name of ['Coastal', 'Inland']) {
    await page.getByLabel('Tag name').fill(name);
    await page.getByRole('button', { name: 'Add tag', exact: true }).click();
    await expect(page.getByRole('button', { name: `Delete tag ${name}` })).toBeVisible();
  }

  const source = page.locator('li.source', { hasText: 'tides.md' });
  await source.getByLabel('Add a tag').selectOption({ label: 'Coastal' });
  await expect(source.getByRole('list', { name: 'Tags' })).toContainText('Coastal');

  const filter = page.getByRole('group', { name: 'Filter by tag' });
  const inland = filter.getByRole('button', { name: /^Inland/ });
  await inland.click();
  await expect(inland).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByText('No sources here with these tags.')).toBeVisible();
  await expect(source).toHaveCount(0);

  await inland.click();
  const coastal = filter.getByRole('button', { name: /^Coastal/ });
  await coastal.click();
  await expect(coastal).toHaveAttribute('aria-pressed', 'true');
  await expect(coastal).toContainText('1');
  await expect(source).toBeVisible();

  await page.goto('/search');
  const tags = page.getByRole('group', { name: 'Limit to tags' });
  const search = page.getByRole('button', { name: 'Search', exact: true });
  await tags.getByRole('button', { name: 'Coastal', exact: true }).click();
  await page.getByLabel('Question').fill('which tides have the biggest range');
  await search.click();
  await expect(page.getByText('These are spring tides').first()).toBeVisible();

  await tags.getByRole('button', { name: 'Coastal', exact: true }).click();
  await tags.getByRole('button', { name: 'Inland', exact: true }).click();
  await search.click();
  await expect(page.getByText('No matching passages.')).toBeVisible();

  await page.goto('/sources');
  await page.getByRole('button', { name: 'Edit tags' }).click();
  await page.getByRole('button', { name: 'Delete tag Inland' }).click();
  await page
    .getByRole('group', { name: 'Confirm deleting Inland' })
    .getByRole('button', { name: 'Delete' })
    .click();
  await expect(filter.getByRole('button', { name: /^Inland/ })).toHaveCount(0);
});

test('a deleted source leaves the list after confirming', async ({ page }) => {
  await page.goto('/sources');
  const source = page.locator('li.source', { hasText: 'tides.md' });
  await source.getByRole('button', { name: 'Delete' }).click();
  await source
    .getByRole('group', { name: 'Confirm delete' })
    .getByRole('button', { name: 'Delete' })
    .click();

  await expect(source).toHaveCount(0);
  await expect(page.getByText('No sources yet.')).toBeVisible();
});
