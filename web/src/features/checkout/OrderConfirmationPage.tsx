import { Link, useSearchParams } from 'react-router'
import { Alert } from '../../shared/ui/Feedback'

export function OrderConfirmationPage() {
  const [params] = useSearchParams()
  const orderNumber = params.get('order')

  return (
    <>
      <h1>Thank you!</h1>
      <Alert tone="success">
        {orderNumber ? `Your order ${orderNumber} is placed.` : 'Your order is placed.'} A
        confirmation email is on its way.
      </Alert>
      <p>
        <Link to="/account/orders">See your orders</Link> · <Link to="/">Keep shopping</Link>
      </p>
    </>
  )
}
