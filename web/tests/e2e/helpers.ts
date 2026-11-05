import { expect, type Page } from '@playwright/test'

/** Registers a brand-new customer through the UI; they end up signed in. */
export async function registerNewCustomer(page: Page, returnUrl = '/') {
  const email = `e2e-${crypto.randomUUID()}@test.local`
  await page.goto(`/register?returnUrl=${encodeURIComponent(returnUrl)}`)
  await page.getByLabel('Name').fill('Robin Tester')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill('e2e only, never a real password')
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page).not.toHaveURL(/\/register/)
  return email
}

/** Adds an Illinois delivery address through the API, using the page's cookies and anti-forgery token. */
export async function addAddress(page: Page) {
  await page.request.get('/api/auth/session')
  const xsrf = (await page.context().cookies()).find((c) => c.name === 'XSRF-TOKEN')?.value ?? ''
  const response = await page.request.post('/api/me/addresses', {
    headers: { 'X-XSRF-TOKEN': decodeURIComponent(xsrf) },
    data: {
      recipientName: 'Robin Tester',
      line1: '233 S Wacker Dr',
      line2: null,
      city: 'Chicago',
      stateCode: 'IL',
      postalCode: '60606',
      countryCode: 'US',
      phone: '312 555 0100',
    },
  })
  expect(response.status()).toBe(201)
}

/** Picks the first in-stock value of every option on a product page, then adds one to the cart. */
export async function addFirstAvailableVariantToCart(page: Page) {
  // Each option (Size, Colour, …) is a fieldset of radios; unavailable values are disabled.
  // Wait for the product to load first: .all() doesn't wait, it just lists what's there now.
  const groups = page.getByRole('main').getByRole('group')
  await expect(groups.first()).toBeVisible()
  for (const group of await groups.all()) {
    await group.locator('input[type=radio]:enabled').first().check()
  }
  await page.getByRole('button', { name: 'Add to cart' }).click()
  await expect(page.getByText('Added to your cart.')).toBeVisible()
}
