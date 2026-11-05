import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import { formatDate, formatMoney, type Money } from './format'

/** Country, currency, locale and states, from the store's region settings (FR-033). */
export function useRegion() {
  return useQuery({
    queryKey: ['region'],
    queryFn: () => unwrap(api.GET('/api/region')),
    staleTime: Infinity,
  })
}

/** Formatters bound to the region's locale; falls back to en-US until the region has loaded. */
export function useFormatters() {
  const locale = useRegion().data?.locale ?? 'en-US'
  return {
    money: (money: Money) => formatMoney(money, locale),
    date: (iso: string, timeZone?: string) => formatDate(iso, locale, timeZone),
  }
}
