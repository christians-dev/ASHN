import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, unwrap } from '../../api/client'
import { formMessageFrom } from '../../shared/auth/formErrors'
import { Button } from '../../shared/ui/Button'
import { Alert, EmptyState } from '../../shared/ui/Feedback'
import { Spinner } from '../../shared/ui/Spinner'
import { type Address, AddressForm } from './AddressForm'
import { addressesQueryKey, useAddresses } from './addressQueries'

/** The customer's delivery addresses (FR-016): add, edit, delete, choose the default. */
export function AddressesPage() {
  const addresses = useAddresses()
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<Address | 'new' | null>(null)
  const [error, setError] = useState<string>()
  const refresh = () => queryClient.invalidateQueries({ queryKey: addressesQueryKey })

  async function act(action: () => Promise<unknown>) {
    setError(undefined)
    try {
      await action()
      await refresh()
    } catch (e) {
      setError(formMessageFrom(e))
    }
  }

  if (addresses.isPending) return <Spinner />
  if (addresses.isError) return <Alert tone="error">Your addresses couldn’t be loaded.</Alert>

  if (editing) {
    const isNew = editing === 'new'
    return (
      <>
        <h1>{isNew ? 'Add an address' : 'Edit address'}</h1>
        <AddressForm
          initial={isNew ? undefined : editing}
          submitLabel={isNew ? 'Add address' : 'Save address'}
          onCancel={() => setEditing(null)}
          onSubmit={async (body) => {
            await unwrap(
              isNew
                ? api.POST('/api/me/addresses', { body })
                : api.PUT('/api/me/addresses/{id}', { params: { path: { id: editing.id } }, body }),
            )
            await refresh()
            setEditing(null)
          }}
        />
      </>
    )
  }

  return (
    <>
      <h1>Delivery addresses</h1>
      {error && <Alert tone="error">{error}</Alert>}
      {addresses.data.length === 0 ? (
        <EmptyState title="No addresses yet">
          <p>Add one now so checkout is quicker.</p>
        </EmptyState>
      ) : (
        <ul>
          {addresses.data.map((a) => (
            <li key={a.id}>
              <address>
                <strong>{a.recipientName}</strong>
                {a.isDefault && ' (default)'}
                <br />
                {a.line1}
                {a.line2 && `, ${a.line2}`}, {a.city}, {a.stateCode} {a.postalCode}, {a.countryCode}
                <br />
                {a.phone}
              </address>
              <Button variant="secondary" onClick={() => setEditing(a)}>
                Edit
              </Button>{' '}
              {!a.isDefault && (
                <Button
                  variant="secondary"
                  onClick={() =>
                    act(() =>
                      unwrap(
                        api.POST('/api/me/addresses/{id}/default', {
                          params: { path: { id: a.id } },
                        }),
                      ),
                    )
                  }
                >
                  Make default
                </Button>
              )}{' '}
              <Button
                variant="secondary"
                onClick={() =>
                  act(() =>
                    unwrap(
                      api.DELETE('/api/me/addresses/{id}', { params: { path: { id: a.id } } }),
                    ),
                  )
                }
              >
                Delete
              </Button>
            </li>
          ))}
        </ul>
      )}
      <Button onClick={() => setEditing('new')}>Add an address</Button>
    </>
  )
}
