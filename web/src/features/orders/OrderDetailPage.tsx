import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { api, unwrap } from '../../api/client'
import { formMessageFrom } from '../../shared/auth/formErrors'
import { Button } from '../../shared/ui/Button'
import { Alert } from '../../shared/ui/Feedback'
import { Spinner } from '../../shared/ui/Spinner'
import { OrderDetailView } from './OrderDetailView'
import styles from './orders.module.css'
import { myOrdersQueryKey, useMyOrder } from './orderQueries'

/**
 * One of the customer's orders. Within an hour of placing it, while still Paid, they can cancel it (FR-035);
 * if it ships meanwhile, the server says no and the page shows why.
 */
export function OrderDetailPage() {
  const { id = '' } = useParams()
  const order = useMyOrder(id)
  const queryClient = useQueryClient()
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<{ tone: 'error' | 'success'; text: string }>()

  async function cancel() {
    setBusy(true)
    try {
      await unwrap(api.POST('/api/me/orders/{id}/cancel', { params: { path: { id } } }))
      setMessage({
        tone: 'success',
        text: 'Your order is cancelled and your card has been refunded in full.',
      })
    } catch (e) {
      setMessage({ tone: 'error', text: formMessageFrom(e) })
    } finally {
      setBusy(false)
      setConfirming(false)
      // Either way, show the order as it is now (cancelled, or shipped in the meantime).
      await queryClient.invalidateQueries({ queryKey: myOrdersQueryKey })
    }
  }

  if (order.isPending) return <Spinner />
  if (order.isError) return <Alert tone="error">This order couldn’t be found.</Alert>

  return (
    <>
      <p>
        <Link to="/account/orders">← Your orders</Link>
      </p>
      <h1>Order {order.data.orderNumber}</h1>
      {message && <Alert tone={message.tone}>{message.text}</Alert>}

      {order.data.canCancel && (
        <div className={styles.actions}>
          {confirming ? (
            <>
              <span>
                Cancel this order? Your items go back on sale and you’re refunded in full.
              </span>
              <Button onClick={cancel} disabled={busy}>
                Yes, cancel order
              </Button>
              <Button variant="secondary" onClick={() => setConfirming(false)} disabled={busy}>
                Keep order
              </Button>
            </>
          ) : (
            <Button variant="secondary" onClick={() => setConfirming(true)}>
              Cancel order
            </Button>
          )}
        </div>
      )}

      <OrderDetailView order={order.data} />
    </>
  )
}
