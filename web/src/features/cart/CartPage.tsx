import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { formMessageFrom } from '../../shared/auth/formErrors'
import { useFormatters } from '../../shared/format/useRegion'
import { Button } from '../../shared/ui/Button'
import { Alert, EmptyState } from '../../shared/ui/Feedback'
import { Spinner } from '../../shared/ui/Spinner'
import { CouponInput } from './CouponInput'
import { type CartLine, useCart, useSetCartQuantity } from './cartQueries'
import styles from './cart.module.css'

/** Problems that stop checkout, in plain words. */
function lineNotice(line: CartLine): string | undefined {
  if (!line.isPurchasable) return 'No longer available. Remove it to continue.'
  if (line.quantity > line.available)
    return `Only ${line.available} left. Lower the quantity to continue.`
  if (line.priceChanged) return 'The price has changed since you added this.'
  return undefined
}

export function CartPage() {
  const cart = useCart()
  const setQuantity = useSetCartQuantity()
  const format = useFormatters()
  const navigate = useNavigate()
  const [error, setError] = useState<string>()

  async function change(variantId: string, quantity: number) {
    setError(undefined)
    try {
      await setQuantity(variantId, quantity)
    } catch (e) {
      setError(formMessageFrom(e))
    }
  }

  if (cart.isPending) return <Spinner />
  if (cart.isError) return <Alert tone="error">Your cart couldn’t be loaded.</Alert>
  if (cart.data.items.length === 0) {
    return (
      <EmptyState title="Your cart is empty">
        <p>
          <Link to="/">Keep shopping</Link>
        </p>
      </EmptyState>
    )
  }

  return (
    <>
      <h1>Your cart</h1>
      {error && <Alert tone="error">{error}</Alert>}
      <ul className={styles.lines}>
        {cart.data.items.map((line) => {
          const notice = lineNotice(line)
          const max = Math.max(1, Math.min(99, line.available, Math.max(line.quantity, 1)))
          return (
            <li key={line.variantId} className={styles.line}>
              {line.imageUrl && <img src={line.imageUrl} alt="" className={styles.image} />}
              <div>
                <Link to={`/products/${line.productSlug}`}>{line.productName}</Link>
                <p className={styles.muted}>
                  {Object.entries(line.optionValues)
                    .map(([name, value]) => `${name}: ${value}`)
                    .join(' · ')}
                </p>
                <p>{format.money(line.unitPrice)} each</p>
                {notice && <p className={styles.notice}>{notice}</p>}
              </div>
              <div>
                <label htmlFor={`qty-${line.variantId}`}>Quantity</label>
                <select
                  id={`qty-${line.variantId}`}
                  value={line.quantity}
                  onChange={(e) => change(line.variantId, Number(e.target.value))}
                >
                  {Array.from({ length: Math.max(max, line.quantity) }, (_, i) => i + 1).map(
                    (n) => (
                      <option key={n} value={n}>
                        {n}
                      </option>
                    ),
                  )}
                </select>
                <p>{format.money(line.lineTotal)}</p>
                <Button variant="secondary" onClick={() => change(line.variantId, 0)}>
                  Remove
                </Button>
              </div>
            </li>
          )
        })}
      </ul>
      {cart.data.subtotal && (
        <p className={styles.subtotal}>
          Subtotal: <strong>{format.money(cart.data.subtotal)}</strong> (shipping and tax at
          checkout)
        </p>
      )}
      <CouponInput cart={cart.data} />
      <Button disabled={!cart.data.canCheckout} onClick={() => navigate('/checkout')}>
        Checkout
      </Button>
    </>
  )
}
