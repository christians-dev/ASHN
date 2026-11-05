import { useFormatters } from '../../shared/format/useRegion'
import { CheckoutSummary } from '../checkout/CheckoutSummary'
import styles from './orders.module.css'
import type { OrderDetail } from './orderQueries'

/** An order as placed: lines, totals, delivery address and card. Shared by the customer and admin pages. */
export function OrderDetailView({ order }: { order: OrderDetail }) {
  const format = useFormatters()
  const { shipTo } = order

  return (
    <>
      <p>
        Placed {format.date(order.placedAt)} · <strong>{order.status}</strong>
      </p>

      <table className={styles.table}>
        <thead>
          <tr>
            <th scope="col">Item</th>
            <th scope="col">Price</th>
            <th scope="col">Qty</th>
            <th scope="col">Total</th>
          </tr>
        </thead>
        <tbody>
          {order.lines.map((line) => (
            <tr key={line.sku}>
              <td>
                {line.productName}
                <br />
                <small>
                  {Object.entries(line.optionValues)
                    .map(([name, value]) => `${name}: ${value}`)
                    .join(', ')}{' '}
                  · {line.sku}
                </small>
              </td>
              <td>{format.money(line.unitPrice)}</td>
              <td>{line.quantity}</td>
              <td>{format.money(line.lineTotal)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className={styles.columns}>
        <section aria-labelledby="totals-heading">
          <h2 id="totals-heading">Totals</h2>
          <CheckoutSummary quote={order} />
        </section>
        <section aria-labelledby="ship-heading">
          <h2 id="ship-heading">Delivery</h2>
          <address>
            {shipTo.recipientName}
            <br />
            {shipTo.line1}
            {shipTo.line2 && (
              <>
                <br />
                {shipTo.line2}
              </>
            )}
            <br />
            {shipTo.city}, {shipTo.stateCode} {shipTo.postalCode}
          </address>
          <h2>Payment</h2>
          <p>
            {order.cardBrand} ending {order.cardLast4}
            {order.status === 'Cancelled' && ' · refunded in full'}
          </p>
        </section>
      </div>
    </>
  )
}
