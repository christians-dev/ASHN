import { useQuery } from '@tanstack/react-query'
import { type FormEvent, useState } from 'react'
import { api, unwrap, type Schemas } from '../../../api/client'
import { useFormatters } from '../../../shared/format/useRegion'
import { Button } from '../../../shared/ui/Button'
import { Alert } from '../../../shared/ui/Feedback'
import { Select } from '../../../shared/ui/Select'
import { Spinner } from '../../../shared/ui/Spinner'
import { TextField } from '../../../shared/ui/TextField'
import styles from '../admin.module.css'
import { useAdminAction } from '../adminQueries'

type Coupon = Schemas['CouponDto']
type DiscountType = Schemas['DiscountType']

/** "2026-11-04T00:00:00Z" → "2026-11-04" for a date input (calendar days are stored as UTC midnight). */
const dayOf = (iso?: string | null) => (iso ? iso.slice(0, 10) : '')
const numberOrNull = (value: FormDataEntryValue | null) => (value ? Number(value) : null)

/** Coupons: create, edit and deactivate (FR-037). */
export function CouponsPage() {
  const format = useFormatters()
  const coupons = useQuery({
    queryKey: ['admin', 'coupons'],
    queryFn: () => unwrap(api.GET('/api/admin/coupons')),
  })
  const { run, error, busy } = useAdminAction()
  // The coupon being edited, or undefined when the form creates a new one.
  const [editing, setEditing] = useState<Coupon>()

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const starts = String(form.get('startsAt') ?? '')
    const ends = String(form.get('endsAt') ?? '')
    const body = {
      code: String(form.get('code') ?? ''),
      discountType: String(form.get('discountType')) as DiscountType,
      value: Number(form.get('value')),
      minimumSpend: numberOrNull(form.get('minimumSpend')),
      startsAt: starts ? `${starts}T00:00:00Z` : null,
      endsAt: ends ? `${ends}T00:00:00Z` : null,
      totalUsageLimit: numberOrNull(form.get('totalUsageLimit')),
      onePerCustomer: form.get('onePerCustomer') === 'on',
    }
    const saved = await run(() =>
      unwrap(
        editing
          ? api.PUT('/api/admin/coupons/{id}', { params: { path: { id: editing.id } }, body })
          : api.POST('/api/admin/coupons', { body }),
      ),
    )
    if (saved.ok) {
      setEditing(undefined)
      if (!editing) event.currentTarget?.reset()
    }
  }

  const deactivate = (id: string) =>
    run(() => unwrap(api.POST('/api/admin/coupons/{id}/deactivate', { params: { path: { id } } })))

  const describe = (c: Coupon) =>
    c.discountType === 'Percentage'
      ? `${c.value}% off`
      : `${format.money({ amount: c.value, currency: 'USD' })} off`

  if (coupons.isPending) return <Spinner />
  if (coupons.isError) return <Alert tone="error">Coupons couldn’t be loaded.</Alert>

  return (
    <>
      <h1>Coupons</h1>
      <table className={styles.table}>
        <thead>
          <tr>
            <th scope="col">Code</th>
            <th scope="col">Discount</th>
            <th scope="col">Minimum</th>
            <th scope="col">Valid</th>
            <th scope="col">Used</th>
            <th scope="col">Status</th>
            <th scope="col" />
          </tr>
        </thead>
        <tbody>
          {coupons.data.map((c) => (
            <tr key={c.id}>
              <td>{c.code}</td>
              <td>
                {describe(c)}
                {c.onePerCustomer && ', once per customer'}
              </td>
              <td>{c.minimumSpend ? format.money(c.minimumSpend) : ''}</td>
              <td>
                {format.date(c.startsAt, 'UTC')} –{' '}
                {c.endsAt ? format.date(c.endsAt, 'UTC') : 'no end'}
              </td>
              <td>
                {c.redemptionCount}
                {c.totalUsageLimit != null && ` of ${c.totalUsageLimit}`}
              </td>
              <td>{c.isActive ? 'Active' : 'Deactivated'}</td>
              <td>
                <Button variant="secondary" disabled={busy} onClick={() => setEditing(c)}>
                  Edit
                </Button>{' '}
                {c.isActive && (
                  <Button variant="secondary" disabled={busy} onClick={() => deactivate(c.id)}>
                    Deactivate
                  </Button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <section className={styles.section} aria-labelledby="coupon-form-heading">
        <h2 id="coupon-form-heading">{editing ? `Edit ${editing.code}` : 'New coupon'}</h2>
        {error && <Alert tone="error">{error}</Alert>}
        {/* A new key resets the form's defaults when switching between coupons. */}
        <form key={editing?.id ?? 'new'} onSubmit={save} className={styles.inline}>
          <TextField label="Code" name="code" defaultValue={editing?.code} required />
          <Select
            label="Type"
            name="discountType"
            defaultValue={editing?.discountType ?? 'Percentage'}
            options={[
              { value: 'Percentage', label: 'Percentage' },
              { value: 'FixedAmount', label: 'Fixed amount' },
            ]}
          />
          <TextField
            label="Value"
            name="value"
            type="number"
            min={0}
            step="0.01"
            defaultValue={editing?.value}
            required
          />
          <TextField
            label="Minimum spend (optional)"
            name="minimumSpend"
            type="number"
            min={0}
            step="0.01"
            defaultValue={editing?.minimumSpend?.amount ?? ''}
          />
          <TextField
            label="Starts (optional, defaults to now)"
            name="startsAt"
            type="date"
            defaultValue={dayOf(editing?.startsAt)}
          />
          <TextField
            label="Ends (optional)"
            name="endsAt"
            type="date"
            defaultValue={dayOf(editing?.endsAt)}
          />
          <TextField
            label="Total uses (optional)"
            name="totalUsageLimit"
            type="number"
            min={1}
            step="1"
            defaultValue={editing?.totalUsageLimit ?? ''}
          />
          <label>
            <input
              type="checkbox"
              name="onePerCustomer"
              defaultChecked={editing?.onePerCustomer ?? false}
            />{' '}
            Once per customer
          </label>
          <div>
            <Button type="submit" disabled={busy}>
              {editing ? 'Save changes' : 'Create coupon'}
            </Button>{' '}
            {editing && (
              <Button variant="secondary" onClick={() => setEditing(undefined)}>
                Cancel
              </Button>
            )}
          </div>
        </form>
      </section>
    </>
  )
}
