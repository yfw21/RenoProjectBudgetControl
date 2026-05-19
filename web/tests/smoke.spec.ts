import { test, expect } from '@playwright/test'

const BASE = 'http://localhost:5163'

test('app loads and shows login or home', async ({ page }) => {
  const resp = await page.goto(BASE, { waitUntil: 'domcontentloaded' })
  expect(resp?.ok()).toBeTruthy()
  // The app routes to /login when unauthenticated; just ensure SOMETHING rendered.
  await expect(page.locator('body')).toBeVisible()
  await page.screenshot({ path: 'tests/_screenshots/00_home.png', fullPage: true })
})
