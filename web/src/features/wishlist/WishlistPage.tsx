import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { formMessageFrom } from '../../shared/auth/formErrors'
import { useFormatters } from '../../shared/format/useRegion'
import { Button } from '../../shared/ui/Button'
import { Alert, EmptyState } from '../../shared/ui/Feedback'
import { Spinner } from '../../shared/ui/Spinner'
import { type Variant, useProduct } from '../catalog/catalogQueries'
import { VariantSelector } from '../catalog/components/VariantSelector'
import { useCart, useSetCartQuantity } from '../cart/cartQueries'
import styles from './wishlist.module.css'
import {
  type WishlistItem,
  useToggleWishlist,
  useWishlist,
  wishlistQueryKey,
} from './wishlistQueries'

/** Saved products: remove them, or pick a size/colour and move one into the cart (Story 11). */
export function WishlistPage() {
  const wishlist = useWishlist()
  const [moved, setMoved] = useState<string>()

  if (wishlist.isPending) return <Spinner />
  if (wishlist.isError) return <Alert tone="error">Your wishlist couldn’t be loaded.</Alert>

  return (
    <>
      <h1>Your wishlist</h1>
      {moved && (
        <Alert tone="success">
          {moved} is in your cart. <Link to="/cart">View cart</Link>
        </Alert>
      )}
      {wishlist.data.length === 0 ? (
        <EmptyState title="Nothing saved yet">
          <p>
            Tap <span aria-hidden="true">♡</span> Save on any product to keep it here.{' '}
            <Link to="/">Start shopping</Link>
          </p>
        </EmptyState>
      ) : (
        <ul className={styles.items}>
          {wishlist.data.map((item) => (
            <li key={item.productId} className={styles.item}>
              <WishlistRow item={item} onMoved={() => setMoved(item.name)} />
            </li>
          ))}
        </ul>
      )}
    </>
  )
}

function WishlistRow({ item, onMoved }: { item: WishlistItem; onMoved: () => void }) {
  const format = useFormatters()
  const toggle = useToggleWishlist()
  const [choosing, setChoosing] = useState(false)
  const [error, setError] = useState<string>()

  async function remove() {
    setError(undefined)
    try {
      await toggle(item.productId, true)
    } catch (e) {
      setError(formMessageFrom(e))
    }
  }

  return (
    <>
      {item.imageUrl ? (
        <img src={item.imageUrl} alt="" className={styles.image} />
      ) : (
        <div className={styles.image} />
      )}
      <div>
        <h2>{item.slug ? <Link to={`/products/${item.slug}`}>{item.name}</Link> : item.name}</h2>
        {item.fromPrice && <p>From {format.money(item.fromPrice)}</p>}
        {!item.isAvailable && (
          <p className={styles.muted}>No longer available, so it can’t be added to the cart.</p>
        )}
        {error && <Alert tone="error">{error}</Alert>}
        {choosing && item.slug ? (
          <MoveToCart slug={item.slug} productId={item.productId} onMoved={onMoved} />
        ) : (
          <div className={styles.actions}>
            {item.isAvailable && <Button onClick={() => setChoosing(true)}>Move to cart</Button>}
            <Button
              variant="secondary"
              onClick={remove}
              aria-label={`Remove ${item.name} from wishlist`}
            >
              Remove
            </Button>
          </div>
        )}
      </div>
    </>
  )
}

/** Choose the size/colour with the product page's selector, then add one to the cart and drop it from the wishlist. */
function MoveToCart({
  slug,
  productId,
  onMoved,
}: {
  slug: string
  productId: string
  onMoved: () => void
}) {
  const product = useProduct(slug)
  const cart = useCart()
  const setQuantity = useSetCartQuantity()
  const toggle = useToggleWishlist()
  const queryClient = useQueryClient()
  const [variant, setVariant] = useState<Variant | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()

  async function move() {
    if (!variant) return
    setBusy(true)
    setError(undefined)
    try {
      const inCart = cart.data?.items.find((i) => i.variantId === variant.id)?.quantity ?? 0
      await setQuantity(variant.id, inCart + 1)
      onMoved()
      await toggle(productId, true)
    } catch (e) {
      setError(formMessageFrom(e))
      await queryClient.invalidateQueries({ queryKey: wishlistQueryKey })
    } finally {
      setBusy(false)
    }
  }

  if (product.isPending) return <Spinner label="Loading options…" />
  if (product.isError) return <Alert tone="error">This product isn’t available any more.</Alert>

  return (
    <div>
      <VariantSelector
        options={product.data.options}
        variants={product.data.variants}
        onChange={setVariant}
      />
      {error && <Alert tone="error">{error}</Alert>}
      <Button onClick={move} disabled={!variant || busy}>
        {variant ? 'Add to cart' : 'Choose options'}
      </Button>
    </div>
  )
}
