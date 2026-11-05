import { useQuery } from '@tanstack/react-query'
import type { FormEvent } from 'react'
import { api, unwrap } from '../../../api/client'
import { useFormatters, useRegion } from '../../../shared/format/useRegion'
import { Button } from '../../../shared/ui/Button'
import { Alert } from '../../../shared/ui/Feedback'
import { Select } from '../../../shared/ui/Select'
import { Spinner } from '../../../shared/ui/Spinner'
import { TextField } from '../../../shared/ui/TextField'
import styles from '../admin.module.css'
import { useAdminAction } from '../adminQueries'

/** 0.0625 → "6.25%". */
const percent = (rate: number) =>
  `${(rate * 100).toLocaleString('en-US', { maximumFractionDigits: 3 })}%`

/**
 * Sales tax per state and the flat shipping fee (FR-024, FR-030). A rate change adds a dated entry, so past
 * orders keep the rate they were charged and future changes can be scheduled.
 */
export function TaxAndShippingPage() {
  const region = useRegion()
  const format = useFormatters()
  const rates = useQuery({
    queryKey: ['admin', 'tax-rates'],
    queryFn: () => unwrap(api.GET('/api/admin/tax-rates')),
  })
  const settings = useQuery({
    queryKey: ['admin', 'store-settings'],
    queryFn: () => unwrap(api.GET('/api/admin/store-settings')),
  })
  const tax = useAdminAction()
  const shipping = useAdminAction()

  function setRate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const code = String(form.get('state') ?? '')
    const rate = Number(form.get('rate')) / 100
    const date = String(form.get('effectiveFrom') ?? '')
    tax.run(() =>
      unwrap(
        api.PUT('/api/admin/tax-rates/{jurisdictionCode}', {
          params: { path: { jurisdictionCode: code } },
          // A calendar day, stored as UTC midnight.
          body: { rate, effectiveFrom: date ? `${date}T00:00:00Z` : null },
        }),
      ),
    )
  }

  function setShipping(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const flatShippingFee = Number(new FormData(event.currentTarget).get('fee'))
    shipping.run(() => unwrap(api.PUT('/api/admin/store-settings', { body: { flatShippingFee } })))
  }

  if (region.isPending || rates.isPending || settings.isPending) return <Spinner />
  if (region.isError || rates.isError || settings.isError) {
    return <Alert tone="error">Tax and shipping settings couldn’t be loaded.</Alert>
  }

  return (
    <>
      <h1>Tax &amp; shipping</h1>

      <section aria-labelledby="tax-heading">
        <h2 id="tax-heading">Sales tax</h2>
        <p>
          States not listed charge no tax. Tax applies to merchandise after discounts, not to
          shipping.
        </p>
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">State</th>
              <th scope="col">Rate</th>
              <th scope="col">From</th>
              <th scope="col" />
            </tr>
          </thead>
          <tbody>
            {rates.data.map((r) => (
              <tr key={`${r.jurisdictionCode}-${r.effectiveFrom}`}>
                <td>{r.jurisdictionName}</td>
                <td>{percent(r.rate)}</td>
                <td>{format.date(r.effectiveFrom, 'UTC')}</td>
                <td>{r.isCurrent ? 'Current' : r.isScheduled ? 'Scheduled' : 'Past'}</td>
              </tr>
            ))}
          </tbody>
        </table>

        {tax.error && <Alert tone="error">{tax.error}</Alert>}
        <form onSubmit={setRate} className={styles.inline} aria-label="Set a tax rate">
          <Select
            label="State"
            name="state"
            placeholder="Choose…"
            options={region.data.states.map((s) => ({ value: s.code, label: s.name }))}
          />
          <TextField label="Rate (%)" name="rate" type="number" min={0} max={19.999} step="0.001" />
          <TextField label="From (optional, defaults to now)" name="effectiveFrom" type="date" />
          <div>
            <Button type="submit" disabled={tax.busy}>
              Set rate
            </Button>
          </div>
        </form>
      </section>

      <section className={styles.section} aria-labelledby="shipping-heading">
        <h2 id="shipping-heading">Shipping</h2>
        <p>Current flat fee: {format.money(settings.data.flatShippingFee)} per order.</p>
        {shipping.error && <Alert tone="error">{shipping.error}</Alert>}
        <form onSubmit={setShipping} className={styles.inline} aria-label="Set the shipping fee">
          <TextField
            label={`Flat fee (${region.data.currencyCode})`}
            name="fee"
            type="number"
            min={0}
            step="0.01"
            defaultValue={settings.data.flatShippingFee.amount}
          />
          <div>
            <Button type="submit" disabled={shipping.busy}>
              Save fee
            </Button>
          </div>
        </form>
      </section>
    </>
  )
}
