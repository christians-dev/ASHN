import type { Schemas } from '../../api/client'
import { useFormatters } from '../../shared/format/useRegion'

export type Quote = Schemas['QuoteResponse']

/** Subtotal, discount, shipping, tax and total, exactly as the server priced them (FR-019). Also shows a placed order's totals. */
export function CheckoutSummary({
  quote,
}: {
  quote: Pick<
    Quote,
    'subtotal' | 'couponCode' | 'discount' | 'shipping' | 'taxRate' | 'tax' | 'total'
  >
}) {
  const format = useFormatters()
  const taxPercent = `${(quote.taxRate * 100).toLocaleString('en-US', { maximumFractionDigits: 3 })}%`

  return (
    <dl aria-label="Order summary">
      <dt>Subtotal</dt>
      <dd>{format.money(quote.subtotal)}</dd>
      {(quote.discount.amount ?? 0) > 0 && (
        <>
          <dt>Discount{quote.couponCode && ` (${quote.couponCode})`}</dt>
          <dd>−{format.money(quote.discount)}</dd>
        </>
      )}
      <dt>Shipping</dt>
      <dd>{format.money(quote.shipping)}</dd>
      <dt>Tax ({taxPercent})</dt>
      <dd>{format.money(quote.tax)}</dd>
      <dt>
        <strong>Total</strong>
      </dt>
      <dd>
        <strong>{format.money(quote.total)}</strong>
      </dd>
    </dl>
  )
}
