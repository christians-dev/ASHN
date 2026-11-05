import type { FormEvent } from 'react'
import { api, unwrap } from '../../../api/client'
import { Button } from '../../../shared/ui/Button'
import { Alert } from '../../../shared/ui/Feedback'
import { Spinner } from '../../../shared/ui/Spinner'
import { TextField } from '../../../shared/ui/TextField'
import { useCategories } from '../../catalog/catalogQueries'
import styles from '../admin.module.css'
import { useAdminAction } from '../adminQueries'

export function CategoriesPage() {
  const categories = useCategories()
  const { run, error, busy } = useAdminAction()

  function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const formElement = event.currentTarget
    const name = String(new FormData(formElement).get('name') ?? '')
    run(async () => {
      await unwrap(api.POST('/api/admin/categories', { body: { name } }))
      formElement.reset()
    })
  }

  function rename(event: FormEvent<HTMLFormElement>, id: string) {
    event.preventDefault()
    const name = String(new FormData(event.currentTarget).get('name') ?? '')
    run(() =>
      unwrap(api.PUT('/api/admin/categories/{id}', { params: { path: { id } }, body: { name } })),
    )
  }

  return (
    <>
      <h1>Categories</h1>
      {error && <Alert tone="error">{error}</Alert>}
      <form onSubmit={create} className={styles.inline} aria-label="New category">
        <TextField label="New category" name="name" />
        <div>
          <Button type="submit" disabled={busy}>
            Add
          </Button>
        </div>
      </form>

      {categories.isPending ? (
        <Spinner />
      ) : (
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">Name</th>
              <th scope="col">Link</th>
              <th scope="col">Actions</th>
            </tr>
          </thead>
          <tbody>
            {categories.data?.map((c) => (
              <tr key={c.id}>
                <td>
                  <form
                    className={styles.inline}
                    onSubmit={(e) => rename(e, c.id)}
                    aria-label={`Rename ${c.name}`}
                  >
                    <input name="name" defaultValue={c.name} aria-label={`Name of ${c.name}`} />
                    <Button type="submit" variant="secondary" disabled={busy}>
                      Rename
                    </Button>
                  </form>
                </td>
                <td>/c/{c.slug}</td>
                <td>
                  <Button
                    variant="secondary"
                    disabled={busy}
                    onClick={() =>
                      run(() =>
                        unwrap(
                          api.DELETE('/api/admin/categories/{id}', {
                            params: { path: { id: c.id } },
                          }),
                        ),
                      )
                    }
                  >
                    Delete
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <p>A category can only be deleted when it has no products.</p>
    </>
  )
}
