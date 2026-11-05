import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, unwrap, type Schemas } from '../../api/client'
import { formMessageFrom } from '../../shared/auth/formErrors'

export type AdminProductRow = Schemas['AdminProductRow']
export type AdminProduct = Schemas['AdminProductDetail']
export type ProductStatus = NonNullable<Schemas['ProductStatus']>

export function useAdminProducts(status?: ProductStatus) {
  return useQuery({
    queryKey: ['admin', 'products', status ?? 'all'],
    queryFn: () => unwrap(api.GET('/api/admin/products', { params: { query: { status } } })),
  })
}

export function useAdminProduct(id: string) {
  return useQuery({
    queryKey: ['admin', 'product', id],
    queryFn: () => unwrap(api.GET('/api/admin/products/{id}', { params: { path: { id } } })),
  })
}

/**
 * Runs an admin change, shows its error if it fails, and refreshes everything admin and catalog related,
 * so lists, the editor and the storefront all show the new state.
 */
export function useAdminAction() {
  const queryClient = useQueryClient()
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)

  async function run<T>(action: () => Promise<T>): Promise<{ ok: boolean; value?: T }> {
    setBusy(true)
    setError(undefined)
    try {
      const value = await action()
      await queryClient.invalidateQueries()
      return { ok: true, value }
    } catch (e) {
      setError(formMessageFrom(e))
      return { ok: false }
    } finally {
      setBusy(false)
    }
  }

  return { run, error, busy }
}
