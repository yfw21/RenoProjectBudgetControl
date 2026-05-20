import { Page, expect } from '@playwright/test'

export const TEST_USER = 'e2e_1779136596'
export const TEST_PASS = 'testPass123!'
export const TEST_PROJECT = 'Aisquith E2E'

/** Log in via the UI. Idempotent — already-authenticated visits just redirect. */
export async function login(page: Page) {
  await page.goto('/login')
  // If we hit /login it means we're not authenticated.
  if (page.url().includes('/login')) {
    await page.locator('input[placeholder="Username"]').fill(TEST_USER)
    await page.locator('input[placeholder="Password"]').fill(TEST_PASS)
    await page.locator('button:has-text("Sign in")').click()
    await expect(page).not.toHaveURL(/\/login$/, { timeout: 10_000 })
  }
}

/** Pick the seeded test project in the top-bar dropdown. */
export async function ensureProject(page: Page, name: string = TEST_PROJECT) {
  const picker = page.locator('.project-picker select')
  await picker.waitFor({ state: 'visible' })
  const current = await picker.inputValue()
  // The select uses the project id as value, not the name — read option labels instead.
  const wantedValue = await picker
    .locator(`option:has-text("${name}")`)
    .first()
    .getAttribute('value')
  if (wantedValue && current !== wantedValue) {
    await picker.selectOption(wantedValue)
  }
}

/** Time a callback in ms with sub-ms precision. */
export async function timed<T>(label: string, fn: () => Promise<T>): Promise<{ value: T; ms: number; label: string }> {
  const t0 = performance.now()
  const value = await fn()
  const ms = performance.now() - t0
  return { value, ms, label }
}

/** Click a sidebar nav link by its visible label. */
export async function navTo(page: Page, label: string) {
  await page.locator(`.navlink:has-text("${label}")`).click()
}
