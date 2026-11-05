import { useState } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { useSession } from '../../shared/auth/useSession'
import styles from './wishlist.module.css'
import { useToggleWishlist, useWishlist } from './wishlistQueries'

/**
 * Saves a product to the wishlist or removes it (FR-041). Visitors are sent to sign in first and come back here
 * afterwards (Story 11 #2).
 */
export function WishlistButton({
  productId,
  productName,
}: {
  productId: string
  productName: string
}) {
  const session = useSession()
  const wishlist = useWishlist()
  const toggle = useToggleWishlist()
  const navigate = useNavigate()
  const location = useLocation()
  const [busy, setBusy] = useState(false)
  const [failed, setFailed] = useState(false)

  const saved = wishlist.data?.some((i) => i.productId === productId) ?? false

  async function click() {
    if (!session.data?.isAuthenticated) {
      navigate(`/sign-in?returnUrl=${encodeURIComponent(location.pathname + location.search)}`)
      return
    }
    setBusy(true)
    setFailed(false)
    try {
      await toggle(productId, saved)
    } catch {
      setFailed(true)
    } finally {
      setBusy(false)
    }
  }

  return (
    <button
      type="button"
      className={styles.toggle}
      onClick={click}
      disabled={busy}
      aria-pressed={saved}
      aria-label={saved ? `Remove ${productName} from wishlist` : `Save ${productName} to wishlist`}
    >
      <span aria-hidden="true">{saved ? '♥' : '♡'}</span>{' '}
      {failed ? 'Didn’t save, try again' : saved ? 'Saved' : 'Save'}
    </button>
  )
}
