import { Link } from 'react-router'
import { useFormatters } from '../../shared/format/useRegion'
import { Alert, EmptyState } from '../../shared/ui/Feedback'
import { Spinner } from '../../shared/ui/Spinner'
import styles from './orders.module.css'
import { useMyOrders } from './orderQueries'

/** The customer's orders, newest first (FR-035). */
export function OrderHistoryPage() {
  const orders = useMyOrders()
  const format = useFormatters()

  if (orders.isPending) return <Spinner />
  if (orders.isError) return <Alert tone="error">Your orders couldn’t be loaded.</Alert>

  return (
    <>
      <h1>Your orders</h1>
      {orders.data.length === 0 ? (
        <EmptyState title="No orders yet">
          <Link to="/">Start shopping</Link>
        </EmptyState>
      ) : (
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">Order</th>
              <th scope="col">Placed</th>
              <th scope="col">Items</th>
              <th scope="col">Total</th>
              <th scope="col">Status</th>
            </tr>
          </thead>
          <tbody>
            {orders.data.map((o) => (
              <tr key={o.id}>
                <td>
                  <Link to={`/account/orders/${o.id}`}>{o.orderNumber}</Link>
                </td>
                <td>{format.date(o.placedAt)}</td>
                <td>{o.itemCount}</td>
                <td>{format.money(o.total)}</td>
                <td>{o.status}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  )
}
