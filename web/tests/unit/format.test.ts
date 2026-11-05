import { describe, expect, it } from 'vitest'
import { formatDate, formatMoney } from '../../src/shared/format/format'

describe('formatMoney', () => {
  it('formats US dollars with separators and cents', () => {
    expect(formatMoney({ amount: 1234.5, currency: 'USD' }, 'en-US')).toBe('$1,234.50')
  })

  it('formats zero and small amounts', () => {
    expect(formatMoney({ amount: 0, currency: 'USD' }, 'en-US')).toBe('$0.00')
    expect(formatMoney({ amount: 6.25, currency: 'USD' }, 'en-US')).toBe('$6.25')
  })

  it('uses the given locale and currency', () => {
    expect(formatMoney({ amount: 10, currency: 'EUR' }, 'de-DE')).toMatch(/10,00\s€/)
  })
})

describe('formatDate', () => {
  it('can show a UTC calendar day without shifting it', () => {
    expect(formatDate('2026-01-01T00:00:00Z', 'en-US', 'UTC')).toBe('Jan 1, 2026')
  })

  it('formats an ISO timestamp as a medium date', () => {
    expect(formatDate('2026-11-03T12:00:00Z', 'en-US')).toBe('Nov 3, 2026')
  })
})
