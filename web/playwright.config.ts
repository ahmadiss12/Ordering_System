import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end tests: a real browser, the real API, a real database.
 *
 * These are the slowest and least numerous tests in the repository, and they exist for the one
 * thing the other suites cannot check — that the pieces are wired to each other. A unit test
 * proves the store sends the right request; only this proves the request reaches a controller
 * that reaches a table and comes back as something a customer would see.
 *
 * The API is started by the caller (see the e2e job in ci.yml and web/README.md); only the
 * Angular dev server is started from here, because Playwright can wait for a port but not for
 * "migrations applied and seeded".
 */
const BASE_URL = process.env['E2E_BASE_URL'] ?? 'http://127.0.0.1:4200';

export default defineConfig({
  testDir: './e2e',
  // One at a time. They share one database, and a test that hides a menu section while another
  // is reading the menu would fail for a reason that has nothing to do with the code.
  workers: 1,
  fullyParallel: false,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  reporter: process.env['CI'] ? [['list'], ['html', { open: 'never' }]] : [['list']],

  use: {
    baseURL: BASE_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },

  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        launchOptions: {
          // Set when a machine already has a Chromium that Playwright did not install — this
          // container ships one at a different revision. CI leaves it unset and uses its own,
          // so the pipeline is not tied to whatever a developer's box happens to have.
          executablePath: process.env['PLAYWRIGHT_CHROMIUM_PATH'] || undefined,
        },
      },
    },
  ],

  webServer: {
    command: 'npx ng serve dashboard --host 127.0.0.1 --port 4200',
    url: BASE_URL,
    reuseExistingServer: !process.env['CI'],
    timeout: 180_000,
    stdout: 'ignore',
    stderr: 'pipe',
  },
});
