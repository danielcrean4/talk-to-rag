import { defineConfig } from '@playwright/test';

// Run through `make test-e2e`, which starts the stack these tests talk to. The API on 5001 serves
// the Angular app built into its image, so the tests use what ships.
export default defineConfig({
  testDir: 'e2e',
  testMatch: '*.e2e.ts',
  workers: 1,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  reporter: [['list']],
  use: {
    baseURL: 'http://127.0.0.1:5001',
    browserName: 'chromium',
    trace: 'retain-on-failure',
  },
});
