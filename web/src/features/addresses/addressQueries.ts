import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'

export const addressesQueryKey = ['addresses'] as const

/** The signed-in customer's addresses, default first. */
export function useAddresses() {
  return useQuery({
    queryKey: addressesQueryKey,
    queryFn: () => unwrap(api.GET('/api/me/addresses')),
  })
}
