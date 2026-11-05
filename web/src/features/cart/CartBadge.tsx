import { NavLink } from 'react-router'
import { useCart } from './cartQueries'

export function CartBadge() {
  const count = useCart().data?.itemCount ?? 0
  return (
    <NavLink to="/cart" aria-label={`Cart, ${count} ${count === 1 ? 'item' : 'items'}`}>
      Cart ({count})
    </NavLink>
  )
}
