import { test, expect } from '@playwright/test'
import { login, ensureProject } from './helpers'

test.describe('Reports — new pivot tabs', () => {
  test.beforeEach(async ({ page }) => {
    await login(page)
    await ensureProject(page)
    await page.goto('/reports')
    await expect(page.locator('h1:has-text("Reports")')).toBeVisible()
  })

  test('Spend by Month tab renders a month-column pivot', async ({ page }) => {
    await page.locator('button.tab:has-text("Spend by Month")').click()
    // The pivot table appears once the lazy fetch resolves.
    const table = page.locator('.card table.data').first()
    await expect(table).toBeVisible({ timeout: 15_000 })

    // Header should contain Budget + Total + Remaining columns plus at least one month.
    // (.data th is uppercased via CSS, so normalize.)
    const headers = (await table.locator('thead th').allInnerTexts()).map(s => s.trim().toLowerCase())
    expect(headers).toEqual(expect.arrayContaining(['budget line', 'budget', 'total', 'remaining']))
    expect(headers.some(h => /\b(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)\s+\d{4}/.test(h))).toBeTruthy()

    // Footer monthly-total row exists.
    await expect(table.locator('tfoot')).toContainText('Monthly total')
    await page.screenshot({ path: 'tests/_screenshots/40_spend_by_month.png', fullPage: true })
  })

  test('Tax Pivot tab renders category × month', async ({ page }) => {
    await page.locator('button.tab:has-text("Tax Pivot")').click()
    const table = page.locator('.card table.data').first()
    await expect(table).toBeVisible({ timeout: 15_000 })

    const headers = (await table.locator('thead th').allInnerTexts()).map(s => s.trim().toLowerCase())
    expect(headers).toEqual(expect.arrayContaining(['category', 'total']))
    expect(headers.some(h => /\b(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)\s+\d{4}/.test(h))).toBeTruthy()

    // There should be at least one category row + a totals footer.
    await expect(table.locator('tbody tr').first()).toBeVisible()
    await expect(table.locator('tfoot')).toContainText('Total')
    await page.screenshot({ path: 'tests/_screenshots/41_tax_pivot.png', fullPage: true })
  })

  test('grand totals match between the two pivots', async ({ page }) => {
    // Spend by Month grand total (footer last cell).
    await page.locator('button.tab:has-text("Spend by Month")').click()
    const spendTable = page.locator('.card table.data').first()
    await expect(spendTable).toBeVisible({ timeout: 15_000 })
    const spendFootCells = await spendTable.locator('tfoot th').allInnerTexts()
    const spendTotal = spendFootCells.map(s => s.trim()).filter(s => s.startsWith('$'))

    await page.locator('button.tab:has-text("Tax Pivot")').click()
    const taxTable = page.locator('.card table.data').first()
    await expect(taxTable).toBeVisible({ timeout: 15_000 })
    const taxFootCells = await taxTable.locator('tfoot th').allInnerTexts()
    const taxTotal = taxFootCells.map(s => s.trim()).filter(s => s.startsWith('$'))

    // Last $ value in each footer is the grand total — they must agree.
    expect(spendTotal.at(-1)).toBe(taxTotal.at(-1))
  })
})
