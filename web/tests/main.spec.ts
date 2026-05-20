import { test, expect, Page } from '@playwright/test'
import { login, ensureProject, navTo } from './helpers'

/**
 * MAIN UI walkthrough. One authenticated session, click through every nav target,
 * verify each page's signature element rendered, screenshot it, and surface any
 * console errors. Use this as the canary for "did I break the app?".
 */

const PAGES: Array<{ label: string; route: string; expect: string; screenshot: string }> = [
  { label: 'Dashboard',  route: '/dashboard',  expect: 'h1, h2',                              screenshot: '20_dashboard.png' },
  { label: 'Projects',   route: '/projects',   expect: 'h1:has-text("Projects")',             screenshot: '21_projects.png' },
  { label: 'Budget',     route: '/budget',     expect: 'h1',                                   screenshot: '22_budget.png' },
  { label: 'Expenses',   route: '/expenses',   expect: 'h1:has-text("Expenses")',             screenshot: '23_expenses.png' },
  { label: 'Tasks',      route: '/tasks',      expect: 'h1',                                   screenshot: '24_tasks.png' },
  { label: 'Resources',  route: '/resources',  expect: 'h1',                                   screenshot: '25_resources.png' },
  { label: 'Deliveries', route: '/deliveries', expect: 'h1',                                   screenshot: '26_deliveries.png' },
  { label: 'Notes',      route: '/notes',      expect: 'h1',                                   screenshot: '27_notes.png' },
  { label: 'Reports',    route: '/reports',    expect: 'h1',                                   screenshot: '28_reports.png' },
  { label: 'Settings',   route: '/settings',   expect: 'h1, h2',                               screenshot: '29_settings.png' },
]

/** Record JS console errors as the page runs — failures fail the test. */
function collectConsoleErrors(page: Page): string[] {
  const errors: string[] = []
  page.on('pageerror', e => errors.push(`pageerror: ${e.message}`))
  page.on('console', m => {
    if (m.type() === 'error') errors.push(`console.error: ${m.text()}`)
  })
  return errors
}

test.describe('Main UI walkthrough', () => {
  test.beforeEach(async ({ page }) => {
    await login(page)
    await ensureProject(page)
  })

  for (const p of PAGES) {
    test(`renders ${p.label}`, async ({ page }) => {
      const errors = collectConsoleErrors(page)
      await page.goto(p.route)
      await expect(page.locator(p.expect).first()).toBeVisible({ timeout: 10_000 })
      await page.screenshot({ path: `tests/_screenshots/${p.screenshot}`, fullPage: true })

      // Filter benign errors (favicon 404, expected 401 on unauth probes) — fail on the rest.
      const real = errors.filter(e =>
        !/favicon/i.test(e) &&
        !/401/.test(e) &&
        !/Failed to load resource: the server responded with a status of 401/.test(e))
      expect.soft(real, `console errors on ${p.label}:\n${real.join('\n')}`).toEqual([])
    })
  }
})

test.describe('Expenses page core interactions', () => {
  test.beforeEach(async ({ page }) => {
    await login(page)
    await ensureProject(page)
    await page.goto('/expenses')
  })

  test('Smart upload button is present and clickable', async ({ page }) => {
    const btn = page.locator('button:has-text("Smart upload")')
    await expect(btn).toBeVisible()
    await expect(btn).toBeEnabled()
  })

  test('Smart invoice button is present and clickable', async ({ page }) => {
    const btn = page.locator('button:has-text("Smart invoice")')
    await expect(btn).toBeVisible()
    await expect(btn).toBeEnabled()
  })

  test('KPI tiles render (Spent, Budget, Items)', async ({ page }) => {
    const kpis = page.locator('.kpi .kpi-label')
    // .kpi-label is uppercased via CSS (text-transform), so normalize.
    const labels = (await kpis.allInnerTexts()).map(s => s.trim().toLowerCase())
    expect(labels).toEqual(expect.arrayContaining(['spent', 'budget', 'items']))
  })

  test('Add manually → fill → close keeps modal stable through interactions', async ({ page }) => {
    await page.locator('button:has-text("+ Add manually")').click()
    const modal = page.locator('.modal-large').first()
    await expect(modal).toBeVisible()

    await modal.locator('input').first().fill('Test expense from Playwright')
    await modal.locator('input[type="number"]').fill('123.45')

    // Open & filter the budget-line combobox — confirms it doesn't crash the form.
    const combo = modal.locator('.combo')
    await combo.locator('.combo-input').click()
    await combo.locator('.combo-input').fill('lighting')
    const filtered = combo.locator('.combo-item')
    await expect(filtered).not.toHaveCount(0)

    // Close everything without saving (no DB writes).
    await modal.locator('button:has-text("Cancel")').click()
    await expect(modal).not.toBeVisible()
  })
})

test.describe('Budget page', () => {
  test.beforeEach(async ({ page }) => {
    await login(page)
    await ensureProject(page)
    await page.goto('/budget')
  })

  test('Budget table renders rows from the imported xlsx', async ({ page }) => {
    // We don't know the table's structure precisely; assert at least one row of data
    // appears (the Aisquith import seeded 29 lines).
    const rows = page.locator('table tbody tr')
    await expect(rows.first()).toBeVisible({ timeout: 10_000 })
    const count = await rows.count()
    expect(count).toBeGreaterThan(5)
    await page.screenshot({ path: 'tests/_screenshots/30_budget_rows.png', fullPage: true })
  })
})
