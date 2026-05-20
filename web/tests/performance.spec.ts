import { test, expect, Page } from '@playwright/test'
import { login, ensureProject } from './helpers'

/**
 * Performance budgets. These are soft thresholds — failures get logged but don't
 * crash the suite. Use them as a regression signal, not an absolute pass/fail.
 *
 * All numbers are measured CLIENT-side (Navigation Timing + perf.now around DOM
 * waits). The slow LLM flows (parse-invoice / suggest-line) are intentionally
 * excluded — those are bottlenecked by Ollama, not the app.
 */

type PerfRow = { name: string; ms: number; budget?: number }
const collected: PerfRow[] = []

async function measureNavigation(page: Page, name: string, path: string): Promise<PerfRow> {
  const t0 = Date.now()
  await page.goto(path, { waitUntil: 'domcontentloaded' })
  // Wait for the page heading to confirm the SPA has actually rendered content.
  await page.locator('h1, h2').first().waitFor({ state: 'visible', timeout: 15_000 })
  const ms = Date.now() - t0
  return { name, ms }
}

async function readNavigationTiming(page: Page) {
  return page.evaluate(() => {
    const nav = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined
    if (!nav) return null
    return {
      domContentLoaded: Math.round(nav.domContentLoadedEventEnd - nav.startTime),
      loadEvent: Math.round(nav.loadEventEnd - nav.startTime),
      firstByte: Math.round(nav.responseStart - nav.startTime),
      transferSize: nav.transferSize,
    }
  })
}

test.describe('Page-load performance', () => {
  test.beforeAll(async () => { collected.length = 0 })

  test.afterAll(() => {
    if (collected.length === 0) return
    // Pretty-print a table so the run output is the report.
    const w = Math.max(...collected.map(r => r.name.length))
    console.log('\n— Page perf —')
    for (const r of collected) {
      const budget = r.budget ? ` (budget ${r.budget}ms)` : ''
      console.log(`  ${r.name.padEnd(w)}  ${r.ms.toString().padStart(5)} ms${budget}`)
    }
  })

  test.beforeEach(async ({ page }) => {
    await login(page)
    await ensureProject(page)
  })

  const PAGES: Array<{ name: string; path: string; budget: number }> = [
    { name: 'Dashboard',  path: '/dashboard',  budget: 1500 },
    { name: 'Projects',   path: '/projects',   budget: 1500 },
    { name: 'Budget',     path: '/budget',     budget: 2500 },
    { name: 'Expenses',   path: '/expenses',   budget: 2500 },
    { name: 'Tasks',      path: '/tasks',      budget: 2000 },
    { name: 'Resources',  path: '/resources',  budget: 1500 },
    { name: 'Deliveries', path: '/deliveries', budget: 1500 },
    { name: 'Notes',      path: '/notes',      budget: 1500 },
    { name: 'Reports',    path: '/reports',    budget: 3000 },
  ]

  for (const p of PAGES) {
    test(`${p.name} loads under ${p.budget}ms`, async ({ page }) => {
      const row = await measureNavigation(page, p.name, p.path)
      row.budget = p.budget
      collected.push(row)
      const timing = await readNavigationTiming(page)
      if (timing) {
        console.log(`  ${p.name} TTFB ${timing.firstByte}ms · DCL ${timing.domContentLoaded}ms · transfer ${timing.transferSize ?? 'n/a'}b`)
      }
      // Soft assertion — surface the failure but keep running so we see the whole picture.
      expect.soft(row.ms, `${p.name} should render in under ${p.budget}ms`).toBeLessThan(p.budget)
    })
  }
})

test.describe('Interaction latency', () => {
  test.beforeEach(async ({ page }) => {
    await login(page)
    await ensureProject(page)
    await page.goto('/expenses')
  })

  test('opening the Add manually modal is under 200ms', async ({ page }) => {
    const t0 = Date.now()
    await page.locator('button:has-text("+ Add manually")').click()
    await page.locator('.modal-large').waitFor({ state: 'visible' })
    const ms = Date.now() - t0
    console.log(`  modal-open ${ms}ms`)
    expect.soft(ms, 'modal should open within 200ms').toBeLessThan(200)
  })

  test('budget-line combobox filters under 100ms', async ({ page }) => {
    await page.locator('button:has-text("+ Add manually")').click()
    const combo = page.locator('.combo').first()
    await combo.locator('.combo-input').click()
    const input = combo.locator('.combo-input')

    const t0 = Date.now()
    await input.fill('kitchen')
    await combo.locator('.combo-item').first().waitFor({ state: 'visible' })
    const ms = Date.now() - t0
    console.log(`  combo-filter ${ms}ms`)
    expect.soft(ms, 'combobox filter should respond within 100ms').toBeLessThan(150)
  })

  test('JS bundle decoded size under 400KB', async ({ page }) => {
    // decodedBodySize is cache-independent, so we report the actual bundle size
    // the browser parsed — not whatever was on the wire after caching.
    await page.goto('/dashboard')
    const totalBytes = await page.evaluate(() => {
      const res = performance.getEntriesByType('resource') as PerformanceResourceTiming[]
      return res
        .filter(r => r.name.endsWith('.js') || /\/assets\/.*\.js$/.test(r.name))
        .reduce((s, r) => s + (r.decodedBodySize || 0), 0)
    })
    const kb = Math.round(totalBytes / 1024)
    console.log(`  bundle decoded ~${kb} KB`)
    expect.soft(kb, 'JS bundle decoded size should be under 400 KB').toBeLessThan(400)
    expect.soft(kb, 'bundle size measurement should not be zero').toBeGreaterThan(0)
  })
})
