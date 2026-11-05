import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, unwrap } from '../../api/client'
import { formMessageFrom } from '../../shared/auth/formErrors'
import { Button } from '../../shared/ui/Button'
import { Alert, EmptyState } from '../../shared/ui/Feedback'
import { Spinner } from '../../shared/ui/Spinner'
import { type CardDetails, CardEntry } from '../checkout/CardEntry'
import {
  describeCard,
  saveCard,
  savedCardsQueryKey,
  usePaymentMode,
  useSavedCards,
} from './cardQueries'

/** Saved cards: shown as brand, last four and expiry only (FR-029). */
export function PaymentMethodsPage() {
  const cards = useSavedCards()
  const mode = usePaymentMode()
  const queryClient = useQueryClient()
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<{ tone: 'error' | 'success'; text: string }>()
  // A new key remounts the card form, clearing the typed number and CVC once saved.
  const [formKey, setFormKey] = useState(0)

  async function act(action: () => Promise<unknown>, success: string) {
    setBusy(true)
    setMessage(undefined)
    try {
      await action()
      setMessage({ tone: 'success', text: success })
    } catch (e) {
      setMessage({ tone: 'error', text: formMessageFrom(e) })
    } finally {
      setBusy(false)
      await queryClient.invalidateQueries({ queryKey: savedCardsQueryKey })
    }
  }

  const add = (card: CardDetails) =>
    act(async () => {
      await saveCard(card)
      setFormKey((k) => k + 1)
    }, 'Card saved.')
  const makeDefault = (id: string) =>
    act(
      () => unwrap(api.POST('/api/me/cards/{id}/default', { params: { path: { id } } })),
      'Default card changed.',
    )
  const remove = (id: string) =>
    act(
      () => unwrap(api.DELETE('/api/me/cards/{id}', { params: { path: { id } } })),
      'Card removed.',
    )

  if (cards.isPending) return <Spinner />
  if (cards.isError) return <Alert tone="error">Your cards couldn’t be loaded.</Alert>

  return (
    <>
      <h1>Payment methods</h1>
      {mode.data?.simulated && (
        <Alert>
          This is a demo store: no real payment is taken and no real card is charged. Use a test
          card such as 4242 4242 4242 4242 or 5555 5555 5555 4444.
        </Alert>
      )}
      {message && <Alert tone={message.tone}>{message.text}</Alert>}

      {cards.data.length === 0 ? (
        <EmptyState title="No saved cards" />
      ) : (
        <ul aria-label="Saved cards">
          {cards.data.map((card) => (
            <li key={card.id}>
              {describeCard(card)}
              {card.isDefault && <strong> · Default</strong>}
              {card.isExpired && <strong> · Expired</strong>}{' '}
              {!card.isDefault && !card.isExpired && (
                <Button variant="secondary" disabled={busy} onClick={() => makeDefault(card.id)}>
                  Make default
                </Button>
              )}{' '}
              <Button
                variant="secondary"
                disabled={busy}
                onClick={() => remove(card.id)}
                aria-label={`Remove ${describeCard(card)}`}
              >
                Remove
              </Button>
            </li>
          ))}
        </ul>
      )}

      <h2>Add a card</h2>
      <CardEntry
        key={formKey}
        submitLabel="Save card"
        busyLabel="Saving…"
        busy={busy}
        onSubmit={add}
      />
    </>
  )
}
