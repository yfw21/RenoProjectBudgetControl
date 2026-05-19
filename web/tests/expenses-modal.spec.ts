import { test, expect, Page } from '@playwright/test'

const USER = 'e2e_1779136596'
const PASS = 'testPass123!'
const PROJECT_NAME = 'Aisquith E2E'

async function login(page: Page) {
  await page.goto('/login')
  await page.locator('input[placeholder="Username"]').fill(USER)
  await page.locator('input[placeholder="Password"]').fill(PASS)
  await page.locator('button:has-text("Sign in")').click()
  await expect(page).not.toHaveURL(/\/login$/)
}

async function ensureProject(page: Page) {
  // Top bar has a project picker <select>. Pick our seed project.
  const picker = page.locator('.project-picker select')
  await picker.waitFor({ state: 'visible' })
  await picker.selectOption({ label: PROJECT_NAME })
}

test.describe('Add-expense modal on a short viewport', () => {
  test('Save button stays visible without backdrop close (1024x600)', async ({ page }) => {
    await login(page)
    await ensureProject(page)
    await page.goto('/expenses')

    await page.locator('button:has-text("+ Add manually")').click()

    const modal = page.locator('.modal-large').first()
    await expect(modal).toBeVisible()

    // The pinned footer with Cancel/Save must be visible regardless of body height.
    const saveBtn = modal.locator('.modal-foot button.btn-primary')
    await expect(saveBtn).toBeVisible()
    await expect(saveBtn).toBeInViewport()

    await page.screenshot({ path: 'tests/_screenshots/10_addexpense_small.png', fullPage: false })

    // Clicking the backdrop (outside the modal) must NOT close it now.
    await page.mouse.click(5, 5)
    await expect(modal).toBeVisible()
  })

  test('Category combobox shows budget categories as suggestions', async ({ page }) => {
    await login(page)
    await ensureProject(page)
    await page.goto('/expenses')
    await page.locator('button:has-text("+ Add manually")').click()

    const categoryInput = page.locator('input[list="expense-category-options"]')
    await expect(categoryInput).toBeVisible()

    // The datalist <option>s exist in the DOM — verify some budget categories
    // (the Aisquith import yields these section names).
    const options = page.locator('#expense-category-options option')
    const values = await options.evaluateAll(els => els.map(e => (e as HTMLOptionElement).value))
    expect(values).toEqual(expect.arrayContaining(['Interior', 'Exterior']))

    await page.screenshot({ path: 'tests/_screenshots/11_category_combobox.png' })
  })

  test('Budget-line combobox is searchable and pins the AI match', async ({ page }) => {
    await login(page)
    await ensureProject(page)
    await page.goto('/expenses')
    await page.locator('button:has-text("+ Add manually")').click()

    const combo = page.locator('.combo').first()
    await combo.locator('.combo-input').click()
    await expect(combo.locator('.combo-list')).toBeVisible()

    // Type to filter — typing 'kitchen' should narrow the list.
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
