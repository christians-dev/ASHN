import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end checks against a running store with the sample catalog: start it with `aspire run` from the repo root
 * (or point E2E_BASE_URL at another environment), then `npm run e2e`. Every test registers its own fresh account.
 */
export default defineConfig({
  testDir: 'tests/e2e',
  fullyParallel: true,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5173',
    trace: 'retain-on-failure',
  },
  // FR-046: the same flows on a desktop browser and a touch phone.
  projects: [
    {
      name: 'desktop',
      use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 800 } },
    },
    {
      name: 'mobile',
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 375, height: 812 },
        hasTouch: true,
        isMobile: true,
      },
    },
  ],
})
