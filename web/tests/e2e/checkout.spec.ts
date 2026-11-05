import { expect, test } from '@playwright/test'
import { addAddress, addFirstAvailableVariantToCart, registerNewCustomer } from './helpers'

// The core journey (Story 1 → 5): browse, add to cart, sign in, pay with the test card, see the confirmation.
test('a shopper browses, signs up at checkout and places an order', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('link', { name: 'Tops' }).first().click()
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await page.getByRole('link', { name: 'Essential Tee' }).first().click()
  await expect(page.getByRole('heading', { name: 'Essential Tee' })).toBeVisible()

  await addFirstAvailableVariantToCart(page)
  await page.goto('/cart')
  await page.getByRole('button', { name: 'Checkout' }).click()
  await expect(page).toHaveURL(/\/sign-in/) // checkout needs an account

  // The guest cart follows the shopper into their new account.
  await registerNewCustomer(page, '/checkout')
  await addAddress(page)
  await page.goto('/checkout')

  await expect(page.getByLabel('Order summary')).toContainText('Total')
  await page.getByLabel('Card number').fill('4242 4242 4242 4242')
  await page.getByLabel('Expiry (MM/YY)').fill('12/30')
  await page.getByLabel('Security code').fill('123')
  await page.getByRole('button', { name: /Place order/ }).click()

  await expect(page.getByRole('heading', { name: 'Thank you!' })).toBeVisible()
  await expect(page.getByText(/Your order ASH-\d{6} is placed/)).toBeVisible()
})
