import { useState } from 'react'
import { Link } from 'react-router'
import { formMessageFrom } from '../../shared/auth/formErrors'
import { Button } from '../../shared/ui/Button'
import { Alert } from '../../shared/ui/Feedback'
import type { Variant } from '../catalog/catalogQueries'
import { useCart, useSetCartQuantity } from './cartQueries'

/** Adds one of the chosen variant to the cart; disabled until a complete, in-stock choice is made. */
export function AddToCartButton({ variant }: { variant: Variant | null }) {
  const cart = useCart()
  const setQuantity = useSetCartQuantity()
  const [status, setStatus] = useState<{ tone: 'success' | 'error'; text: string }>()
  const [busy, setBusy] = useState(false)

  async function add() {
    if (!variant) return
    const inCart = cart.data?.items.find((i) => i.variantId === variant.id)?.quantity ?? 0
    setBusy(true)
    try {
      await setQuantity(variant.id, inCart + 1)
      setStatus({ tone: 'success', text: 'Added to your cart.' })
    } catch (error) {
      setStatus({ tone: 'error', text: formMessageFrom(error) })
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <Button onClick={add} disabled={!variant || busy}>
        {variant ? 'Add to cart' : 'Choose options'}
      </Button>
      {status && (
        <Alert tone={status.tone}>
          {status.text} {status.tone === 'success' && <Link to="/cart">View cart</Link>}
        </Alert>
      )}
    </>
  )
}
