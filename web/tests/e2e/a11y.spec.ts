import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, test } from '@playwright/test'
import { addAddress, addFirstAvailableVariantToCart, registerNewCustomer } from './helpers'

/** No WCAG 2.1 A/AA violations, and nothing wider than the screen (no sideways scrolling at 375 px). */
async function expectAccessible(page: Page) {
  await page.waitForLoadState('networkidle')
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze()
  expect(
    results.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
  ).toEqual([])

  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  )
  expect(overflow, 'page is wider than the viewport').toBeLessThanOrEqual(0)
}

test.describe('accessibility', () => {
  for (const [name, path] of [
    ['home', '/'],
    ['product list', '/c/tops'],
    ['product detail', '/products/essential-tee'],
    ['sign-in', '/sign-in'],
  ] as const) {
    test(name, async ({ page }) => {
      await page.goto(path)
      await expectAccessible(page)
    })
  }

  test('cart and checkout', async ({ page }) => {
    await page.goto('/products/essential-tee')
    await addFirstAvailableVariantToCart(page)
    await page.goto('/cart')
    await expectAccessible(page)

    await registerNewCustomer(page)
    await addAddress(page)
    await page.goto('/checkout')
    await expect(page.getByLabel('Card number')).toBeVisible()
    await expectAccessible(page)
  })
})
