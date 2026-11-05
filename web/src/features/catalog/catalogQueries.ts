import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, unwrap, type Schemas } from '../../api/client'

export type ProductSummary = Schemas['ProductSummary']
export type ProductDetail = Schemas['ProductDetail']
export type Variant = Schemas['VariantDto']
export type ProductOption = Schemas['OptionDto']

export interface BrowseFilters {
  category?: string
  q?: string
  size?: string
  colour?: string
  minPrice?: number
  maxPrice?: number
  sort?: string
  page?: number
}

export function useHome() {
  return useQuery({ queryKey: ['home'], queryFn: () => unwrap(api.GET('/api/home')) })
}

export function useCategories() {
  return useQuery({
    queryKey: ['categories'],
    queryFn: () => unwrap(api.GET('/api/categories')),
    staleTime: 5 * 60_000,
  })
}

export function useProducts(filters: BrowseFilters) {
  return useQuery({
    queryKey: ['products', filters],
    queryFn: () => unwrap(api.GET('/api/products', { params: { query: filters } })),
    // Keep showing the current page while the next one loads.
    placeholderData: keepPreviousData,
  })
}

export function useProduct(slug: string) {
  return useQuery({
    queryKey: ['product', slug],
    queryFn: () => unwrap(api.GET('/api/products/{slug}', { params: { path: { slug } } })),
  })
}
