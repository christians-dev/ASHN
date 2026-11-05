import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap, type Schemas } from '../../api/client'

export type Cart = Schemas['CartDto']
export type CartLine = Schemas['CartLineDto']

export const cartQueryKey = ['cart'] as const

/** The shopper's cart (guest or signed in). The server merges a guest cart after sign-in. */
export function useCart() {
  return useQuery({ queryKey: cartQueryKey, queryFn: () => unwrap(api.GET('/api/cart')) })
}

/** Sets a line's quantity (0 removes it) and shows the server's new cart straight away. */
export function useSetCartQuantity() {
  const queryClient = useQueryClient()
  return async (variantId: string, quantity: number) => {
    const cart = await unwrap(
      api.PUT('/api/cart/items/{variantId}', {
        params: { path: { variantId } },
        body: { quantity },
      }),
    )
    queryClient.setQueryData(cartQueryKey, cart)
    return cart
  }
}
