import { type FormEvent, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { api, unwrap } from '../../../api/client'
import { Button } from '../../../shared/ui/Button'
import { Alert } from '../../../shared/ui/Feedback'
import { Select } from '../../../shared/ui/Select'
import { Spinner } from '../../../shared/ui/Spinner'
import { TextField } from '../../../shared/ui/TextField'
import { useCategories } from '../../catalog/catalogQueries'
import styles from '../admin.module.css'
import { type ProductStatus, useAdminAction, useAdminProducts } from '../adminQueries'

const statuses: ProductStatus[] = ['Draft', 'Published', 'Unpublished', 'Archived']

export function ProductListAdminPage() {
  const [status, setStatus] = useState<ProductStatus>()
  const products = useAdminProducts(status)
  const categories = useCategories()
  const navigate = useNavigate()
  const { run, error, busy } = useAdminAction()

  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const created = await run(() =>
      unwrap(
        api.POST('/api/admin/products', {
          body: {
            name: String(form.get('name') ?? ''),
            description: '',
            categoryId: String(form.get('categoryId') ?? ''),
          },
        }),
      ),
    )
    if (created.value) navigate(`/admin/products/${created.value.id}`)
  }

  return (
    <>
      <h1>Products</h1>

      <form onSubmit={create} className={styles.inline} aria-label="New product">
        <TextField label="New product name" name="name" required />
        <Select
          label="Category"
          name="categoryId"
          placeholder="Choose…"
          options={(categories.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
          required
        />
        <div>
          <Button type="submit" disabled={busy}>
            Create draft
          </Button>
        </div>
      </form>
      {error && <Alert tone="error">{error}</Alert>}

      <Select
        label="Show"
        options={statuses.map((s) => ({ value: s, label: s }))}
        placeholder="All products"
        value={status ?? ''}
        onChange={(e) => setStatus((e.target.value || undefined) as ProductStatus | undefined)}
      />

      {products.isPending ? (
        <Spinner />
      ) : products.isError ? (
        <Alert tone="error">Products couldn’t be loaded.</Alert>
      ) : (
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">Name</th>
              <th scope="col">Status</th>
              <th scope="col">Category</th>
              <th scope="col">Variants</th>
              <th scope="col">Featured</th>
            </tr>
          </thead>
          <tbody>
            {products.data.map((p) => (
              <tr key={p.id}>
                <td>
                  <Link to={`/admin/products/${p.id}`}>{p.name}</Link>
                </td>
                <td>{p.status}</td>
                <td>{p.categoryName}</td>
                <td>{p.variantCount}</td>
                <td>{p.isFeatured ? 'Yes' : ''}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  )
}
