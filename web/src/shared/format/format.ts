import type { Schemas } from '../../api/client'

/** Money as the API sends it. Both fields are always present; the generator marks struct fields optional. */
export type Money = Schemas['Money']

/** e.g. { amount: 1234.5, currency: 'USD' } → "$1,234.50" for en-US. */
export function formatMoney(money: Money, locale: string): string {
  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency: money.currency ?? 'USD',
  }).format(money.amount ?? 0)
}

/**
 * e.g. "2026-11-03T17:00:00Z" → "Nov 3, 2026" for en-US. Pass timeZone 'UTC' for calendar days stored as UTC
 * midnight (such as tax effective dates), so they don't shift a day in time zones behind UTC.
 */
export function formatDate(iso: string, locale: string, timeZone?: string): string {
  return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeZone }).format(new Date(iso))
}
