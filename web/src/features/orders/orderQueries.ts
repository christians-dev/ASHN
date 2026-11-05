import { useQuery } from '@tanstack/react-query'
import { api, unwrap, type Schemas } from '../../api/client'

export type OrderSummary = Schemas['OrderSummaryDto']
export type OrderDetail = Schemas['OrderDetailDto']
export type OrderStatus = Schemas['OrderStatus']

export const myOrdersQueryKey = ['me', 'orders'] as const

export function useMyOrders() {
  return useQuery({
    queryKey: myOrdersQueryKey,
    queryFn: () => unwrap(api.GET('/api/me/orders')),
  })
}

export function useMyOrder(id: string) {
  return useQuery({
    queryKey: [...myOrdersQueryKey, id],
    queryFn: () => unwrap(api.GET('/api/me/orders/{id}', { params: { path: { id } } })),
  })
}
