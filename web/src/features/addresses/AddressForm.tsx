import { type FormEvent, useState } from 'react'
import type { Schemas } from '../../api/client'
import { type FieldErrors, fieldErrorsFrom, formMessageFrom } from '../../shared/auth/formErrors'
import { useRegion } from '../../shared/format/useRegion'
import { Button } from '../../shared/ui/Button'
import { Alert } from '../../shared/ui/Feedback'
import { Select } from '../../shared/ui/Select'
import { Spinner } from '../../shared/ui/Spinner'
import { TextField } from '../../shared/ui/TextField'

export type AddressInput = Schemas['AddressRequest']
export type Address = Schemas['AddressDto']

interface AddressFormProps {
  initial?: Address
  submitLabel: string
  /** Saves the address; throw (e.g. an ApiError) to show the server's field errors. */
  onSubmit: (address: AddressInput) => Promise<unknown>
  onCancel?: () => void
}

/** Checks the same region rules as the server (states, postal-code format) before sending. */
export function AddressForm({ initial, submitLabel, onSubmit, onCancel }: AddressFormProps) {
  const region = useRegion()
  const [errors, setErrors] = useState<FieldErrors>({})
  const [message, setMessage] = useState<string>()
  const [busy, setBusy] = useState(false)

  if (region.isPending) return <Spinner />
  if (region.isError) return <Alert tone="error">The address form couldn’t be loaded.</Alert>

  const { states, shippingCountries, postalCodePattern, countryCode } = region.data

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const value = (key: string) => String(form.get(key) ?? '').trim()
    const address: AddressInput = {
      recipientName: value('recipientName'),
      line1: value('line1'),
      line2: value('line2') || null,
      city: value('city'),
      stateCode: value('stateCode'),
      postalCode: value('postalCode'),
      countryCode: value('countryCode') || countryCode,
      phone: value('phone'),
    }

    const local: FieldErrors = {
      recipientName: address.recipientName ? undefined : 'Enter the recipient’s name.',
      line1: address.line1 ? undefined : 'Enter the street address.',
      city: address.city ? undefined : 'Enter the city.',
      stateCode: address.stateCode ? undefined : 'Choose a state.',
      postalCode:
        address.postalCode &&
        (!postalCodePattern || new RegExp(postalCodePattern).test(address.postalCode))
          ? undefined
          : 'Enter a valid postal code.',
      phone: address.phone ? undefined : 'Enter a phone number.',
    }
    setErrors(local)
    setMessage(undefined)
    if (Object.values(local).some(Boolean)) return

    setBusy(true)
    try {
      await onSubmit(address)
    } catch (error) {
      const fields = fieldErrorsFrom(error)
      setErrors(fields)
      if (Object.keys(fields).length === 0) setMessage(formMessageFrom(error))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} noValidate aria-label={submitLabel}>
      {message && <Alert tone="error">{message}</Alert>}
      <TextField
        label="Recipient name"
        name="recipientName"
        autoComplete="name"
        defaultValue={initial?.recipientName}
        error={errors.recipientName}
      />
      <TextField
        label="Street address"
        name="line1"
        autoComplete="address-line1"
        defaultValue={initial?.line1}
        error={errors.line1}
      />
      <TextField
        label="Apartment, suite (optional)"
        name="line2"
        autoComplete="address-line2"
        defaultValue={initial?.line2 ?? ''}
        error={errors.line2}
      />
      <TextField
        label="City"
        name="city"
        autoComplete="address-level2"
        defaultValue={initial?.city}
        error={errors.city}
      />
      <Select
        label="State"
        name="stateCode"
        autoComplete="address-level1"
        placeholder="Choose…"
        defaultValue={initial?.stateCode ?? ''}
        options={states.map((s) => ({ value: s.code, label: s.name }))}
        error={errors.stateCode}
      />
      <TextField
        label="ZIP code"
        name="postalCode"
        autoComplete="postal-code"
        inputMode="numeric"
        defaultValue={initial?.postalCode}
        error={errors.postalCode}
      />
      <Select
        label="Country"
        name="countryCode"
        autoComplete="country"
        defaultValue={initial?.countryCode ?? countryCode}
        options={shippingCountries.map((c) => ({ value: c, label: c }))}
        error={errors.countryCode}
      />
      <TextField
        label="Phone"
        name="phone"
        type="tel"
        autoComplete="tel"
        defaultValue={initial?.phone}
        error={errors.phone}
      />
      <Button type="submit" disabled={busy}>
        {busy ? 'Saving…' : submitLabel}
      </Button>{' '}
      {onCancel && (
        <Button variant="secondary" onClick={onCancel}>
          Cancel
        </Button>
      )}
    </form>
  )
}
