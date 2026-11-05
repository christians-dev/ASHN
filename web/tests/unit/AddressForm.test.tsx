import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../src/api/client'
import { AddressForm } from '../../src/features/addresses/AddressForm'

const region = {
  countryCode: 'US',
  currencyCode: 'USD',
  locale: 'en-US',
  shippingCountries: ['US'],
  states: [
    { code: 'IL', name: 'Illinois' },
    { code: 'TX', name: 'Texas' },
  ],
  postalCodePattern: '^\\d{5}(-\\d{4})?$',
}

beforeEach(() =>
  vi.stubGlobal(
    'fetch',
    vi.fn(
      async () =>
        new Response(JSON.stringify(region), { headers: { 'Content-Type': 'application/json' } }),
    ),
  ),
)
afterEach(() => vi.unstubAllGlobals())

function renderForm(onSubmit: (address: unknown) => Promise<unknown>) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={queryClient}>
      <AddressForm submitLabel="Add address" onSubmit={onSubmit} />
    </QueryClientProvider>,
  )
}

async function fillValidAddress(zip = '60601') {
  await userEvent.type(await screen.findByLabelText('Recipient name'), 'Sam Lee')
  await userEvent.type(screen.getByLabelText('Street address'), '1 Main St')
  await userEvent.type(screen.getByLabelText('City'), 'Chicago')
  await userEvent.selectOptions(screen.getByLabelText('State'), 'IL')
  await userEvent.type(screen.getByLabelText('ZIP code'), zip)
  await userEvent.type(screen.getByLabelText('Phone'), '312 555 0100')
}

describe('AddressForm', () => {
  it('fills the state dropdown from the region', async () => {
    renderForm(vi.fn())

    const state = await screen.findByLabelText('State')
    expect([...state.querySelectorAll('option')].map((o) => o.textContent)).toEqual([
      'Choose…',
      'Illinois',
      'Texas',
    ])
  })

  it('flags every missing required field without calling the server', async () => {
    const onSubmit = vi.fn()
    renderForm(onSubmit)

    await userEvent.click(await screen.findByRole('button', { name: 'Add address' }))

    for (const message of [
      'Enter the recipient’s name.',
      'Enter the street address.',
      'Enter the city.',
      'Choose a state.',
      'Enter a valid postal code.',
      'Enter a phone number.',
    ]) {
      expect(screen.getByText(message)).toBeInTheDocument()
    }
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('checks the ZIP code against the region pattern', async () => {
    const onSubmit = vi.fn()
    renderForm(onSubmit)

    await fillValidAddress('6060')
    await userEvent.click(screen.getByRole('button', { name: 'Add address' }))

    expect(screen.getByLabelText('ZIP code')).toHaveAccessibleDescription(
      'Enter a valid postal code.',
    )
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('shows server field errors next to the right input', async () => {
    renderForm(async () => {
      throw new ApiError(400, { errors: { countryCode: ['We currently ship to US only.'] } })
    })

    await fillValidAddress()
    await userEvent.click(screen.getByRole('button', { name: 'Add address' }))

    expect(await screen.findByText('We currently ship to US only.')).toBeInTheDocument()
    expect(screen.getByLabelText('Country')).toHaveAttribute('aria-invalid', 'true')
  })

  it('disables the button while saving and sends a clean address', async () => {
    let finish: () => void = () => {}
    const onSubmit = vi.fn(() => new Promise<void>((resolve) => (finish = resolve)))
    renderForm(onSubmit)

    await fillValidAddress()
    await userEvent.click(screen.getByRole('button', { name: 'Add address' }))

    expect(screen.getByRole('button', { name: 'Saving…' })).toBeDisabled()
    expect(onSubmit).toHaveBeenCalledWith({
      recipientName: 'Sam Lee',
      line1: '1 Main St',
      line2: null,
      city: 'Chicago',
      stateCode: 'IL',
      postalCode: '60601',
      countryCode: 'US',
      phone: '312 555 0100',
    })
    finish()
  })
})
