import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { api, unwrap } from '../../../api/client'
import { useFormatters } from '../../../shared/format/useRegion'
import { Alert } from '../../../shared/ui/Feedback'
import { Pagination } from '../../../shared/ui/Pagination'
import { Select } from '../../../shared/ui/Select'
import { Spinner } from '../../../shared/ui/Spinner'
import type { OrderStatus } from '../../orders/orderQueries'
import styles from '../admin.module.css'

const statuses: OrderStatus[] = ['Paid', 'Shipped', 'Delivered', 'Cancelled']

/** Every order, newest first, filterable by status (FR-036). */
export function OrdersAdminPage() {
  const [status, setStatus] = useState<OrderStatus>()
  const [page, setPage] = useState(1)
  const format = useFormatters()
  const orders = useQuery({
    queryKey: ['admin', 'orders', status ?? 'all', page],
    queryFn: () => unwrap(api.GET('/api/admin/orders', { params: { query: { status, page } } })),
  })

  return (
    <>
      <h1>Orders</h1>
      <Select
        label="Show"
        options={statuses.map((s) => ({ value: s, label: s }))}
        placeholder="All orders"
        value={status ?? ''}
        onChange={(e) => {
          setStatus((e.target.value || undefined) as OrderStatus | undefined)
          setPage(1)
        }}
      />

      {orders.isPending ? (
        <Spinner />
      ) : orders.isError ? (
        <Alert tone="error">Orders couldn’t be loaded.</Alert>
      ) : (
        <>
          <table className={styles.table}>
            <thead>
              <tr>
                <th scope="col">Order</th>
                <th scope="col">Placed</th>
                <th scope="col">Customer</th>
                <th scope="col">Items</th>
                <th scope="col">Total</th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              {orders.data.items.map((o) => (
                <tr key={o.id}>
                  <td>
                    <Link to={`/admin/orders/${o.id}`}>{o.orderNumber}</Link>
                  </td>
                  <td>{format.date(o.placedAt)}</td>
                  <td>{o.contactEmail || 'Deleted customer'}</td>
                  <td>{o.itemCount}</td>
                  <td>{format.money(o.total)}</td>
                  <td>{o.refundPending ? `${o.status} (refund pending)` : o.status}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {orders.data.items.length === 0 && <p>No orders.</p>}
          <Pagination
            page={orders.data.page}
            pageSize={orders.data.pageSize}
            totalCount={orders.data.totalCount}
            onPageChange={setPage}
          />
        </>
      )}
    </>
  )
}
