import { useQuery } from '@tanstack/react-query'
import { api, unwrap, type Schemas } from '../../api/client'
import type { CardDetails } from '../checkout/CardEntry'

export type SavedCard = Schemas['SavedCardDto']

export const savedCardsQueryKey = ['me', 'cards'] as const

export function useSavedCards() {
  return useQuery({
    queryKey: savedCardsQueryKey,
    queryFn: () => unwrap(api.GET('/api/me/cards')),
  })
}

/** Whether payments are simulated, so pages can say plainly that no real money is taken (FR-028). */
export function usePaymentMode() {
  return useQuery({
    queryKey: ['payments', 'mode'],
    queryFn: () => unwrap(api.GET('/api/payments/mode')),
    staleTime: Infinity,
  })
}

export const saveCard = (card: CardDetails) => unwrap(api.POST('/api/me/cards', { body: card }))

/** "Mastercard •••• 4444, expires 12/30". */
export const describeCard = (card: SavedCard) =>
  `${card.brand} •••• ${card.last4}, expires ${String(card.expMonth).padStart(2, '0')}/${String(card.expYear % 100).padStart(2, '0')}`
