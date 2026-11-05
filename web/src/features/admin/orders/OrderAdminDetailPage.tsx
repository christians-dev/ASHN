import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { api, unwrap } from '../../../api/client'
import { Button } from '../../../shared/ui/Button'
import { Alert } from '../../../shared/ui/Feedback'
import { Spinner } from '../../../shared/ui/Spinner'
import { OrderDetailView } from '../../orders/OrderDetailView'
import styles from '../admin.module.css'
import { useAdminAction } from '../adminQueries'

type Action = 'ship' | 'deliver' | 'cancel'

/** One order with the next step it can take: Paid → Shipped → Delivered, or Cancelled while Paid (FR-036). */
export function OrderAdminDetailPage() {
  const { id = '' } = useParams()
  const order = useQuery({
    queryKey: ['admin', 'order', id],
    queryFn: () => unwrap(api.GET('/api/admin/orders/{id}', { params: { path: { id } } })),
  })
  const { run, error, busy } = useAdminAction()

  function act(action: Action) {
    if (
      action === 'cancel' &&
      !window.confirm('Cancel this order, restock its items and refund the customer in full?')
    )
      return
    const path = { params: { path: { id } } }
    run(() =>
      unwrap(
        action === 'ship'
          ? api.POST('/api/admin/orders/{id}/ship', path)
          : action === 'deliver'
            ? api.POST('/api/admin/orders/{id}/deliver', path)
            : api.POST('/api/admin/orders/{id}/cancel', path),
      ),
    )
  }

  if (order.isPending) return <Spinner />
  if (order.isError) return <Alert tone="error">This order couldn’t be found.</Alert>

  const { status, canCancel, contactEmail } = order.data

  return (
    <>
      <p>
        <Link to="/admin/orders">← Orders</Link>
      </p>
      <h1>Order {order.data.orderNumber}</h1>
      <p>Customer: {contactEmail || 'Deleted customer'}</p>
      {error && <Alert tone="error">{error}</Alert>}

      <div className={styles.inline}>
        {status === 'Paid' && (
          <Button onClick={() => act('ship')} disabled={busy}>
            Mark shipped
          </Button>
        )}
        {status === 'Shipped' && (
          <Button onClick={() => act('deliver')} disabled={busy}>
            Mark delivered
          </Button>
        )}
        {canCancel && (
          <Button variant="secondary" onClick={() => act('cancel')} disabled={busy}>
            Cancel and refund
          </Button>
        )}
      </div>

      <OrderDetailView order={order.data} />
    </>
  )
}
