import { test, expect } from '@playwright/test'
import { login, ensureProject } from './helpers'

test.describe('Add-expense modal on a short viewport', () => {
  test.beforeEach(async ({ page }) => {
    await login(page)
    await ensureProject(page)
    await page.goto('/expenses')
  })

  test('Save button stays visible without backdrop close (1024x600)', async ({ page }) => {
    await page.locator('button:has-text("+ Add manually")').click()
    const modal = page.locator('.modal-large').first()
    await expect(modal).toBeVisible()

    const saveBtn = modal.locator('.modal-foot button.btn-primary')
    await expect(saveBtn).toBeVisible()
    await expect(saveBtn).toBeInViewport()

    await page.screenshot({ path: 'tests/_screenshots/10_addexpense_small.png' })

    // Backdrop click must NOT close the modal.
    await page.mouse.click(5, 5)
    await expect(modal).toBeVisible()
  })

  test('Category combobox shows budget categories as suggestions', async ({ page }) => {
    await page.locator('button:has-text("+ Add manually")').click()
    const options = page.locator('#expense-category-options option')
    const values = await options.evaluateAll(els => els.map(e => (e as HTMLOptionElement).value))
    expect(values).toEqual(expect.arrayContaining(['Interior', 'Exterior']))
    await page.screenshot({ path: 'tests/_screenshots/11_category_combobox.png' })
  })

  test('Budget-line combobox is searchable', async ({ page }) => {
    await page.locator('button:has-text("+ Add manually")').click()
    const combo = page.locator('.combo').first()
    await combo.locator('.combo-input').click()
    await expect(combo.locator('.combo-list')).toBeVisible()

    await combo.locator('.combo-input').fill('kitchen')
    const items = combo.locator('.combo-item')
    await expect(items).not.toHaveCount(0)
    const labels = await items.allInnerTexts()
    for (const l of labels) {
      expect(l.toLowerCase()).toContain('kitchen')
    }
    await page.screenshot({ path: 'tests/_screenshots/12_budgetline_search.png' })
  })
})
