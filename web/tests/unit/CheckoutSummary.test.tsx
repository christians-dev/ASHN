import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { CheckoutSummary, type Quote } from '../../src/features/checkout/CheckoutSummary'

const usd = (amount: number) => ({ amount, currency: 'USD' })

function renderSummary(quote: Quote) {
  // No region loaded yet, so formatting falls back to en-US.
  const queryClient = new QueryClient({ defaultOptions: { queries: { enabled: false } } })
  render(
    <QueryClientProvider client={queryClient}>
      <CheckoutSummary quote={quote} />
    </QueryClientProvider>,
  )
  return within(screen.getByLabelText('Order summary'))
}

describe('CheckoutSummary', () => {
  it('shows every line in US dollars, with the tax rate', () => {
    const summary = renderSummary({
      subtotal: usd(1040),
      couponCode: null,
      discount: usd(0),
      shipping: usd(5),
      taxRate: 0.0625,
      tax: usd(65),
      total: usd(1110),
      problems: [],
    })

    expect(summary.getByText('$1,040.00')).toBeInTheDocument()
    expect(summary.getByText('$5.00')).toBeInTheDocument()
    expect(summary.getByText('Tax (6.25%)')).toBeInTheDocument()
    expect(summary.getByText('$65.00')).toBeInTheDocument()
    expect(summary.getByText('$1,110.00')).toBeInTheDocument()
    expect(summary.queryByText('Discount')).not.toBeInTheDocument()
  })

  it('shows the discount when there is one', () => {
    const summary = renderSummary({
      subtotal: usd(50),
      couponCode: 'WELCOME10',
      discount: usd(5),
      shipping: usd(5),
      taxRate: 0,
      tax: usd(0),
      total: usd(50),
      problems: [],
    })

    expect(summary.getByText('Discount (WELCOME10)')).toBeInTheDocument()
    expect(summary.getByText('−$5.00')).toBeInTheDocument()
    expect(summary.getByText('Tax (0%)')).toBeInTheDocument()
  })
})
