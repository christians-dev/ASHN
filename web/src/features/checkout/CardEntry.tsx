import { type FormEvent, type ReactNode, useState } from 'react'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'

export interface CardDetails {
  number: string
  expMonth: number
  expYear: number
  cvc: string
}

interface CardEntryProps {
  submitLabel: string
  busyLabel?: string
  busy?: boolean
  onSubmit: (card: CardDetails) => void
  /** Extra controls shown above the submit button, e.g. "Save this card". */
  children?: ReactNode
}

/** "4242424242424242" → "4242 4242 4242 4242". */
const groupDigits = (value: string) =>
  value
    .replace(/\D/g, '')
    .slice(0, 19)
    .replace(/(\d{4})(?=\d)/g, '$1 ')

/**
 * Card details for one checkout. Held only in this component's state and sent once to the server: never
 * written to browser storage. A real payment provider would replace this with its own hosted fields (FR-027).
 */
export function CardEntry({
  submitLabel,
  busyLabel = 'Placing order…',
  busy,
  onSubmit,
  children,
}: CardEntryProps) {
  const [number, setNumber] = useState('')
  const [expiry, setExpiry] = useState('')
  const [cvc, setCvc] = useState('')
  const [errors, setErrors] = useState<{ number?: string; expiry?: string; cvc?: string }>({})

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const digits = number.replace(/\D/g, '')
    const [, month, year] = /^(\d{2})\s*\/\s*(\d{2})$/.exec(expiry.trim()) ?? []
    const expMonth = Number(month)
    const expYear = 2000 + Number(year)
    const now = new Date()
    const expired =
      expYear < now.getFullYear() ||
      (expYear === now.getFullYear() && expMonth < now.getMonth() + 1)

    const found = {
      number: digits.length >= 12 && digits.length <= 19 ? undefined : 'Enter the card number.',
      expiry:
        !month || expMonth < 1 || expMonth > 12
          ? 'Enter the expiry date as MM/YY.'
          : expired
            ? 'This card has expired.'
            : undefined,
      cvc: /^\d{3,4}$/.test(cvc) ? undefined : 'Enter the 3 or 4 digit security code.',
    }
    setErrors(found)
    if (found.number || found.expiry || found.cvc) return

    onSubmit({ number: digits, expMonth, expYear, cvc })
  }

  return (
    <form onSubmit={submit} noValidate aria-label="Payment">
      <TextField
        label="Card number"
        name="cardNumber"
        inputMode="numeric"
        autoComplete="cc-number"
        value={number}
        onChange={(e) => setNumber(groupDigits(e.target.value))}
        error={errors.number}
      />
      <TextField
        label="Expiry (MM/YY)"
        name="cardExpiry"
        inputMode="numeric"
        autoComplete="cc-exp"
        placeholder="MM/YY"
        value={expiry}
        onChange={(e) => setExpiry(e.target.value)}
        error={errors.expiry}
      />
      <TextField
        label="Security code"
        name="cardCvc"
        inputMode="numeric"
        autoComplete="cc-csc"
        value={cvc}
        onChange={(e) => setCvc(e.target.value.replace(/\D/g, '').slice(0, 4))}
        error={errors.cvc}
      />
      {children}
      <Button type="submit" disabled={busy}>
        {busy ? busyLabel : submitLabel}
      </Button>
    </form>
  )
}
