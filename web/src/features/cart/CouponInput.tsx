import { useQueryClient } from '@tanstack/react-query'
import { type FormEvent, useState } from 'react'
import { api, unwrap } from '../../api/client'
import { fieldErrorsFrom, formMessageFrom } from '../../shared/auth/formErrors'
import { useFormatters } from '../../shared/format/useRegion'
import { Button } from '../../shared/ui/Button'
import { Alert } from '../../shared/ui/Feedback'
import { TextField } from '../../shared/ui/TextField'
import { type Cart, cartQueryKey } from './cartQueries'
import styles from './cart.module.css'

/**
 * Apply, replace or remove the cart's one coupon (FR-038). Used on the cart and at checkout; checkout's quote is
 * refreshed afterwards so the discount and tax update together.
 */
export function CouponInput({ cart }: { cart: Cart }) {
  const queryClient = useQueryClient()
  const format = useFormatters()
  const [code, setCode] = useState('')
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)

  async function change(action: () => Promise<Cart>) {
    setBusy(true)
    setError(undefined)
    try {
      queryClient.setQueryData(cartQueryKey, await action())
      setCode('')
      await queryClient.invalidateQueries({ queryKey: ['checkout', 'quote'] })
    } catch (e) {
      setError(fieldErrorsFrom(e).code ?? formMessageFrom(e))
    } finally {
      setBusy(false)
    }
  }

  function apply(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    change(() => unwrap(api.PUT('/api/cart/coupon', { body: { code } })))
  }

  const remove = () => change(() => unwrap(api.DELETE('/api/cart/coupon')))

  return (
    <div className={styles.coupon}>
      {cart.couponCode && (
        <p>
          Coupon <strong>{cart.couponCode}</strong>
          {cart.discount && <> · −{format.money(cart.discount)}</>}{' '}
          <Button variant="secondary" onClick={remove} disabled={busy}>
            Remove
          </Button>
        </p>
      )}
      {cart.couponProblem && <Alert tone="error">{cart.couponProblem}</Alert>}
      <form onSubmit={apply} aria-label="Coupon" noValidate>
        <TextField
          label={cart.couponCode ? 'Use a different code' : 'Coupon code'}
          name="coupon"
          autoComplete="off"
          value={code}
          onChange={(e) => setCode(e.target.value)}
          error={error}
        />
        <Button type="submit" variant="secondary" disabled={busy || !code.trim()}>
          Apply
        </Button>
      </form>
    </div>
  )
}
