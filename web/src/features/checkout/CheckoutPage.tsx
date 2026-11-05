import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { api, ApiError, unwrap } from '../../api/client'
import { formMessageFrom } from '../../shared/auth/formErrors'
import { useFormatters } from '../../shared/format/useRegion'
import { Button } from '../../shared/ui/Button'
import { Alert } from '../../shared/ui/Feedback'
import { Spinner } from '../../shared/ui/Spinner'
import { useAddresses } from '../addresses/addressQueries'
import { CouponInput } from '../cart/CouponInput'
import { cartQueryKey, useCart } from '../cart/cartQueries'
import {
  describeCard,
  saveCard,
  savedCardsQueryKey,
  usePaymentMode,
  useSavedCards,
} from '../payment-methods/cardQueries'
import { type CardDetails, CardEntry } from './CardEntry'
import { CheckoutSummary } from './CheckoutSummary'

export function CheckoutPage() {
  const addresses = useAddresses()
  const [chosen, setChosen] = useState<string>()
  const addressId = chosen ?? addresses.data?.find((a) => a.isDefault)?.id
  const format = useFormatters()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  // One key per attempt: a retry of the same request reuses it, so it can never buy twice.
  const attemptKey = useRef(crypto.randomUUID())

  const cart = useCart()
  const mode = usePaymentMode()
  const cards = useSavedCards()
  // 'new' = type a card. Until the customer picks, use their default card if it still works.
  const [pickedCard, setPickedCard] = useState<string>()
  const usableCards = cards.data?.filter((c) => !c.isExpired) ?? []
  const cardChoice =
    pickedCard ?? (usableCards.find((c) => c.isDefault) ?? usableCards[0])?.id ?? 'new'
  const [saveNewCard, setSaveNewCard] = useState(false)
  // The quote is re-asked whenever the address changes: tax depends on the state.
  const quote = useQuery({
    queryKey: ['checkout', 'quote', addressId],
    queryFn: () => unwrap(api.POST('/api/checkout/quote', { body: { addressId: addressId! } })),
    enabled: !!addressId,
  })

  async function placeOrder(card?: CardDetails) {
    if (!addressId) return
    setBusy(true)
    setError(undefined)
    try {
      // "Save this card" saves it first (the gateway checks it), then pays with the saved card.
      let savedCardId = card ? null : cardChoice
      if (card && saveNewCard) {
        savedCardId = (await saveCard(card)).id
        setPickedCard(savedCardId)
        await queryClient.invalidateQueries({ queryKey: savedCardsQueryKey })
      }
      const placed = await unwrap(
        api.POST('/api/checkout', {
          body: {
            addressId,
            card: savedCardId ? null : (card ?? null),
            savedCardId,
            idempotencyKey: attemptKey.current,
          },
        }),
      )
      await queryClient.invalidateQueries({ queryKey: cartQueryKey })
      navigate(`/checkout/complete?order=${encodeURIComponent(placed.orderNumber)}`, {
        replace: true,
      })
    } catch (e) {
      setError(formMessageFrom(e))
      // A decline or a stock problem ends this attempt; the next try is a new one.
      if (e instanceof ApiError && e.status < 500) attemptKey.current = crypto.randomUUID()
      await quote.refetch()
    } finally {
      setBusy(false)
    }
  }

  if (addresses.isPending) return <Spinner />
  if (addresses.isError) return <Alert tone="error">Checkout couldn’t be loaded.</Alert>

  if (addresses.data.length === 0) {
    return (
      <>
        <h1>Checkout</h1>
        <Alert>
          Add a delivery address first. <Link to="/account/addresses">Add an address</Link>
        </Alert>
      </>
    )
  }

  return (
    <>
      <h1>Checkout</h1>
      {mode.data?.simulated && (
        <Alert>
          This is a demo store: no real payment is taken. Use the test card 4242 4242 4242 4242 with
          any future expiry date and any 3-digit code.
        </Alert>
      )}

      <fieldset>
        <legend>Deliver to</legend>
        {addresses.data.map((a) => (
          <label key={a.id} style={{ display: 'block' }}>
            <input
              type="radio"
              name="address"
              value={a.id}
              checked={addressId === a.id}
              onChange={() => setChosen(a.id)}
            />{' '}
            {a.recipientName}, {a.line1}, {a.city}, {a.stateCode} {a.postalCode}
          </label>
        ))}
        <Link to="/account/addresses">Manage addresses</Link>
      </fieldset>

      {cart.data && <CouponInput cart={cart.data} />}

      {quote.isPending ? (
        <Spinner label="Working out shipping and tax…" />
      ) : quote.isError ? (
        <Alert tone="error">{formMessageFrom(quote.error)}</Alert>
      ) : (
        <>
          <CheckoutSummary quote={quote.data} />
          {quote.data.problems.length > 0 && (
            <Alert tone="error">
              {quote.data.problems.join(' ')} <Link to="/cart">Review your cart</Link>
            </Alert>
          )}
        </>
      )}

      {error && <Alert tone="error">{error}</Alert>}
      {quote.data && quote.data.problems.length === 0 && (
        <>
          {(cards.data?.length ?? 0) > 0 && (
            <fieldset>
              <legend>Pay with</legend>
              {cards.data!.map((c) => (
                <label key={c.id} style={{ display: 'block' }}>
                  <input
                    type="radio"
                    name="card"
                    value={c.id}
                    checked={cardChoice === c.id}
                    disabled={c.isExpired}
                    onChange={() => setPickedCard(c.id)}
                  />{' '}
                  {describeCard(c)}
                  {c.isExpired && ' (expired)'}
                </label>
              ))}
              <label style={{ display: 'block' }}>
                <input
                  type="radio"
                  name="card"
                  value="new"
                  checked={cardChoice === 'new'}
                  onChange={() => setPickedCard('new')}
                />{' '}
                Use a new card
              </label>
              <Link to="/account/payment-methods">Manage cards</Link>
            </fieldset>
          )}
          {cardChoice === 'new' ? (
            <CardEntry
              busy={busy}
              submitLabel={`Place order · ${format.money(quote.data.total)}`}
              onSubmit={placeOrder}
            >
              <label style={{ display: 'block' }}>
                <input
                  type="checkbox"
                  checked={saveNewCard}
                  onChange={(e) => setSaveNewCard(e.target.checked)}
                />{' '}
                Save this card for next time
              </label>
            </CardEntry>
          ) : (
            <Button onClick={() => placeOrder()} disabled={busy}>
              {busy ? 'Placing order…' : `Place order · ${format.money(quote.data.total)}`}
            </Button>
          )}
        </>
      )}
    </>
  )
}
