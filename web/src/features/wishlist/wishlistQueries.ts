import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap, type Schemas } from '../../api/client'
import { useSession } from '../../shared/auth/useSession'

export type WishlistItem = Schemas['WishlistItemDto']

export const wishlistQueryKey = ['me', 'wishlist'] as const

/** The signed-in customer's wishlist; nothing is fetched for visitors. */
export function useWishlist() {
  const signedIn = useSession().data?.isAuthenticated ?? false
  return useQuery({
    queryKey: wishlistQueryKey,
    queryFn: () => unwrap(api.GET('/api/me/wishlist')),
    enabled: signedIn,
  })
}

/** Adds or removes a product, then refreshes the wishlist everywhere it's shown. */
export function useToggleWishlist() {
  const queryClient = useQueryClient()
  return async (productId: string, saved: boolean) => {
    const path = { params: { path: { productId } } }
    await unwrap(
      saved
        ? api.DELETE('/api/me/wishlist/{productId}', path)
        : api.PUT('/api/me/wishlist/{productId}', path),
    )
    await queryClient.invalidateQueries({ queryKey: wishlistQueryKey })
  }
}
