import { type FormEvent, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { api, unwrap } from '../../../api/client'
import { useFormatters } from '../../../shared/format/useRegion'
import { Button } from '../../../shared/ui/Button'
import { Alert } from '../../../shared/ui/Feedback'
import { Select } from '../../../shared/ui/Select'
import { Spinner } from '../../../shared/ui/Spinner'
import { TextField } from '../../../shared/ui/TextField'
import { useCategories } from '../../catalog/catalogQueries'
import styles from '../admin.module.css'
import { type AdminProduct, useAdminAction, useAdminProduct } from '../adminQueries'

type Props = { product: AdminProduct }
const text = (form: FormData, key: string) => String(form.get(key) ?? '').trim()

export function ProductEditorPage() {
  const { id = '' } = useParams()
  const product = useAdminProduct(id)

  if (product.isPending) return <Spinner />
  if (product.isError) return <Alert tone="error">This product couldn’t be loaded.</Alert>

  return (
    <>
      <p>
        <Link to="/admin/products">← All products</Link>
      </p>
      <h1>{product.data.name}</h1>
      <StatusSection product={product.data} />
      <DetailsSection product={product.data} />
      <OptionsSection product={product.data} />
      <VariantsSection product={product.data} />
      <ImagesSection product={product.data} />
    </>
  )
}

function StatusSection({ product }: Props) {
  const { run, error, busy } = useAdminAction()
  const navigate = useNavigate()
  const [confirmDelete, setConfirmDelete] = useState(false)
  const path = { params: { path: { id: product.id } } }

  async function remove() {
    if ((await run(() => unwrap(api.DELETE('/api/admin/products/{id}', path)))).ok) {
      navigate('/admin/products')
    }
  }

  return (
    <section aria-labelledby="status-heading">
      <h2 id="status-heading">Status: {product.status}</h2>
      {error && <Alert tone="error">{error}</Alert>}
      <div className={styles.inline}>
        {product.status !== 'Published' && product.status !== 'Archived' && (
          <Button
            disabled={busy}
            onClick={() => run(() => unwrap(api.POST('/api/admin/products/{id}/publish', path)))}
          >
            Publish
          </Button>
        )}
        {product.status === 'Published' && (
          <Button
            variant="secondary"
            disabled={busy}
            onClick={() => run(() => unwrap(api.POST('/api/admin/products/{id}/unpublish', path)))}
          >
            Unpublish
          </Button>
        )}
        {product.status !== 'Archived' && (
          <Button
            variant="secondary"
            disabled={busy}
            onClick={() => run(() => unwrap(api.POST('/api/admin/products/{id}/archive', path)))}
          >
            Archive
          </Button>
        )}
        {confirmDelete ? (
          <>
            <span>
              {product.hasBeenOrdered
                ? 'This product has been ordered, so it will be archived instead of deleted.'
                : 'Delete this product and its images for good?'}
            </span>
            <Button disabled={busy} onClick={remove}>
              Yes, delete
            </Button>
            <Button variant="secondary" onClick={() => setConfirmDelete(false)}>
              Cancel
            </Button>
          </>
        ) : (
          <Button variant="secondary" onClick={() => setConfirmDelete(true)}>
            Delete…
          </Button>
        )}
      </div>
    </section>
  )
}

function DetailsSection({ product }: Props) {
  const categories = useCategories()
  const { run, error, busy } = useAdminAction()

  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const body = {
      name: text(form, 'name'),
      description: text(form, 'description'),
      categoryId: text(form, 'categoryId'),
    }
    run(() =>
      unwrap(api.PUT('/api/admin/products/{id}', { params: { path: { id: product.id } }, body })),
    )
  }

  return (
    <section className={styles.section} aria-labelledby="details-heading">
      <h2 id="details-heading">Details</h2>
      {error && <Alert tone="error">{error}</Alert>}
      <form onSubmit={save}>
        <TextField label="Name" name="name" defaultValue={product.name} />
        <label htmlFor="description">Description</label>
        <textarea
          id="description"
          name="description"
          className={styles.textarea}
          defaultValue={product.description}
        />
        <Select
          label="Category"
          name="categoryId"
          defaultValue={product.categoryId}
          options={(categories.data ?? []).map((c) => ({ value: c.id, label: c.name }))}
        />
        <Button type="submit" disabled={busy}>
          Save details
        </Button>
      </form>
    </section>
  )
}

/** Up to 3 options; values are typed comma-separated, e.g. "S, M, L". */
function OptionsSection({ product }: Props) {
  const { run, error, busy } = useAdminAction()
  const rows = [0, 1, 2].map((i) => product.options[i])

  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const options = [0, 1, 2]
      .map((i) => ({
        name: text(form, `name${i}`),
        values: text(form, `values${i}`)
          .split(',')
          .map((v) => v.trim())
          .filter(Boolean),
      }))
      .filter((o) => o.name)
    run(() =>
      unwrap(
        api.PUT('/api/admin/products/{id}/options', {
          params: { path: { id: product.id } },
          body: { options },
        }),
      ),
    )
  }

  return (
    <section className={styles.section} aria-labelledby="options-heading">
      <h2 id="options-heading">Options</h2>
      <p>
        Options can only be added or removed while the product has no variants. Values can always be
        added.
      </p>
      {error && <Alert tone="error">{error}</Alert>}
      <form onSubmit={save}>
        {rows.map((option, i) => (
          <div key={i} className={styles.inline}>
            <TextField
              label={`Option ${i + 1} name`}
              name={`name${i}`}
              defaultValue={option?.name}
              placeholder={i === 0 ? 'Size' : ''}
            />
            <TextField
              label={`Option ${i + 1} values (comma-separated)`}
              name={`values${i}`}
              defaultValue={option?.values.join(', ')}
              placeholder={i === 0 ? 'S, M, L' : ''}
            />
          </div>
        ))}
        <Button type="submit" disabled={busy}>
          Save options
        </Button>
      </form>
    </section>
  )
}

function VariantsSection({ product }: Props) {
  const format = useFormatters()
  const { run, error, busy } = useAdminAction()
  const productPath = { id: product.id }

  function saveRow(event: FormEvent<HTMLFormElement>, variant: AdminProduct['variants'][number]) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const price = Number(form.get('price'))
    const stock = Number(form.get('stock'))
    run(async () => {
      if (price !== variant.price.amount) {
        await unwrap(
          api.PUT('/api/admin/products/{id}/variants/{variantId}', {
            params: { path: { ...productPath, variantId: variant.id } },
            body: { optionValues: variant.optionValues, price },
          }),
        )
      }
      if (stock !== variant.stockOnHand) {
        await unwrap(
          api.PUT('/api/admin/products/{id}/variants/{variantId}/stock', {
            params: { path: { ...productPath, variantId: variant.id } },
            body: { stockOnHand: stock },
          }),
        )
      }
    })
  }

  function add(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const optionValues = Object.fromEntries(
      product.options.map((o) => [o.name, text(form, `option-${o.name}`)]),
    )
    const body = {
      sku: text(form, 'sku'),
      optionValues,
      price: Number(form.get('price')),
      stockOnHand: Number(form.get('stock')),
    }
    run(() =>
      unwrap(
        api.POST('/api/admin/products/{id}/variants', { params: { path: productPath }, body }),
      ),
    )
  }

  if (product.options.length === 0) {
    return (
      <section className={styles.section}>
        <h2>Variants</h2>
        <p>Define at least one option before adding variants.</p>
      </section>
    )
  }

  return (
    <section className={styles.section} aria-labelledby="variants-heading">
      <h2 id="variants-heading">Variants</h2>
      {error && <Alert tone="error">{error}</Alert>}
      <table className={styles.table}>
        <thead>
          <tr>
            {product.options.map((o) => (
              <th key={o.name} scope="col">
                {o.name}
              </th>
            ))}
            <th scope="col">SKU</th>
            <th scope="col">Price / stock</th>
            <th scope="col">Reserved</th>
          </tr>
        </thead>
        <tbody>
          {product.variants.map((v) => (
            <tr key={v.id}>
              {product.options.map((o) => (
                <td key={o.name}>{v.optionValues[o.name]}</td>
              ))}
              <td>{v.sku}</td>
              <td>
                <form
                  className={styles.inline}
                  onSubmit={(e) => saveRow(e, v)}
                  aria-label={`Price and stock for ${v.sku}`}
                >
                  <input
                    name="price"
                    type="number"
                    min={0}
                    step="0.01"
                    defaultValue={v.price.amount}
                    aria-label={`Price (now ${format.money(v.price)})`}
                  />
                  <input
                    name="stock"
                    type="number"
                    min={0}
                    step={1}
                    defaultValue={v.stockOnHand}
                    aria-label="Stock on hand"
                  />
                  <Button type="submit" variant="secondary" disabled={busy}>
                    Save
                  </Button>
                </form>
              </td>
              <td>{v.stockReserved}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <h3>Add a variant</h3>
      <form onSubmit={add} className={styles.inline}>
        {product.options.map((o) => (
          <Select
            key={o.name}
            label={o.name}
            name={`option-${o.name}`}
            options={o.values.map((v) => ({ value: v, label: v }))}
          />
        ))}
        <TextField label="SKU" name="sku" />
        <TextField label="Price" name="price" type="number" min={0} step="0.01" />
        <TextField label="Stock" name="stock" type="number" min={0} step={1} defaultValue={0} />
        <div>
          <Button type="submit" disabled={busy}>
            Add variant
          </Button>
        </div>
      </form>
    </section>
  )
}

function ImagesSection({ product }: Props) {
  const { run, error, busy } = useAdminAction()
  const path = { params: { path: { id: product.id } } }
  const ids = product.images.map((i) => i.id)

  function move(index: number, by: -1 | 1) {
    const order = [...ids]
    ;[order[index], order[index + by]] = [order[index + by], order[index]]
    run(() =>
      unwrap(
        api.PUT('/api/admin/products/{id}/images/order', { ...path, body: { imageIds: order } }),
      ),
    )
  }

  function upload(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const formElement = event.currentTarget
    run(async () => {
      // Multipart upload: send the form as-is instead of JSON.
      await unwrap(
        api.POST('/api/admin/products/{id}/images', {
          ...path,
          body: form as never,
          bodySerializer: () => form,
        }),
      )
      formElement.reset()
    })
  }

  return (
    <section className={styles.section} aria-labelledby="images-heading">
      <h2 id="images-heading">Images</h2>
      {error && <Alert tone="error">{error}</Alert>}
      <ul className={styles.thumbs}>
        {product.images.map((image, index) => (
          <li key={image.id}>
            <img src={image.url} alt={image.altText} />
            <div className={styles.inline}>
              <Button
                variant="secondary"
                disabled={busy || index === 0}
                onClick={() => move(index, -1)}
                aria-label={`Move image ${index + 1} earlier`}
              >
                ←
              </Button>
              <Button
                variant="secondary"
                disabled={busy || index === ids.length - 1}
                onClick={() => move(index, 1)}
                aria-label={`Move image ${index + 1} later`}
              >
                →
              </Button>
              <Button
                variant="secondary"
                disabled={busy}
                onClick={() =>
                  run(() =>
                    unwrap(
                      api.DELETE('/api/admin/products/{id}/images/{imageId}', {
                        params: { path: { id: product.id, imageId: image.id } },
                      }),
                    ),
                  )
                }
              >
                Delete
              </Button>
            </div>
          </li>
        ))}
      </ul>
      <form onSubmit={upload} className={styles.inline}>
        <div>
          <label htmlFor="image-file">Image (JPEG, PNG or WebP, up to 10 MB)</label>
          <br />
          <input
            id="image-file"
            name="file"
            type="file"
            accept="image/jpeg,image/png,image/webp"
            required
          />
        </div>
        <TextField label="Alt text" name="altText" placeholder={product.name} />
        <div>
          <Button type="submit" disabled={busy}>
            Upload
          </Button>
        </div>
      </form>
    </section>
  )
}
