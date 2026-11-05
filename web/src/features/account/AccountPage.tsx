import { useQuery } from '@tanstack/react-query'
import { type FormEvent, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { api, unwrap } from '../../api/client'
import { type FieldErrors, fieldErrorsFrom, formMessageFrom } from '../../shared/auth/formErrors'
import { useRefreshSession } from '../../shared/auth/useRefreshSession'
import { Button } from '../../shared/ui/Button'
import { Alert } from '../../shared/ui/Feedback'
import { Spinner } from '../../shared/ui/Spinner'
import { TextField } from '../../shared/ui/TextField'

export function AccountPage() {
  const account = useQuery({
    queryKey: ['account'],
    queryFn: () => unwrap(api.GET('/api/account')),
  })
  const refreshSession = useRefreshSession()
  const navigate = useNavigate()
  const [errors, setErrors] = useState<FieldErrors>({})
  const [status, setStatus] = useState<{ tone: 'success' | 'error'; text: string }>()
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  if (account.isPending) return <Spinner />
  if (account.isError) return <Alert tone="error">Your account couldn’t be loaded.</Alert>

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const body = {
      displayName: String(form.get('displayName') ?? '').trim(),
      email: String(form.get('email') ?? '').trim(),
    }

    try {
      await unwrap(api.PUT('/api/account', { body }))
      setErrors({})
      setStatus({ tone: 'success', text: 'Your details have been saved.' })
      await Promise.all([refreshSession(), account.refetch()])
    } catch (error) {
      setErrors(fieldErrorsFrom(error))
      setStatus({ tone: 'error', text: formMessageFrom(error) })
    }
  }

  async function deleteAccount() {
    await unwrap(api.DELETE('/api/account'))
    // Leave this signed-in-only page first: once the session refreshes, it unmounts and can't navigate.
    navigate('/', { replace: true })
    await refreshSession()
  }

  return (
    <>
      <h1>Your account</h1>
      <p>
        <Link to="/account/orders">Your orders</Link> · <Link to="/account/wishlist">Wishlist</Link>{' '}
        · <Link to="/account/addresses">Delivery addresses</Link> ·{' '}
        <Link to="/account/payment-methods">Payment methods</Link>
      </p>
      {status && <Alert tone={status.tone}>{status.text}</Alert>}
      <form onSubmit={save} noValidate>
        <TextField
          label="Name"
          name="displayName"
          autoComplete="name"
          defaultValue={account.data.displayName}
          error={errors.displayName}
        />
        <TextField
          label="Email"
          name="email"
          type="email"
          autoComplete="email"
          defaultValue={account.data.email}
          error={errors.email}
        />
        <Button type="submit">Save</Button>
      </form>

      <section aria-labelledby="delete-heading">
        <h2 id="delete-heading">Delete account</h2>
        {confirmingDelete ? (
          <Alert tone="error">
            <p>
              This removes your name, email and sign-in details for good. Past orders are kept
              without your personal details. This can’t be undone.
            </p>
            <Button onClick={deleteAccount}>Yes, delete my account</Button>{' '}
            <Button variant="secondary" onClick={() => setConfirmingDelete(false)}>
              Cancel
            </Button>
          </Alert>
        ) : (
          <Button variant="secondary" onClick={() => setConfirmingDelete(true)}>
            Delete my account
          </Button>
        )}
      </section>
    </>
  )
}
