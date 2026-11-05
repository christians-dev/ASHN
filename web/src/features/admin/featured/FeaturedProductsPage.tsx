import { useState } from 'react'
import { api, unwrap } from '../../../api/client'
import { Button } from '../../../shared/ui/Button'
import { Alert } from '../../../shared/ui/Feedback'
import { Select } from '../../../shared/ui/Select'
import { Spinner } from '../../../shared/ui/Spinner'
import { useHome } from '../../catalog/catalogQueries'
import styles from '../admin.module.css'
import { useAdminAction, useAdminProducts } from '../adminQueries'

type Pick = { id: string; name: string }

/** Choose and order the products shown on the home page. Only published products can be featured. */
export function FeaturedProductsPage() {
  const home = useHome()
  const published = useAdminProducts('Published')
  const { run, error, busy } = useAdminAction()
  const [picks, setPicks] = useState<Pick[]>()
  const [saved, setSaved] = useState(false)

  if (home.isPending || published.isPending) return <Spinner />
  if (home.isError || published.isError)
    return <Alert tone="error">Products couldn’t be loaded.</Alert>

  // Start from what the home page shows now, until the admin changes something.
  const current = picks ?? home.data.featured.map((f) => ({ id: f.productId, name: f.name }))
  const available = published.data.filter((p) => !current.some((c) => c.id === p.id))

  function change(next: Pick[]) {
    setSaved(false)
    setPicks(next)
  }

  function move(index: number, by: -1 | 1) {
    const next = [...current]
    ;[next[index], next[index + by]] = [next[index + by], next[index]]
    change(next)
  }

  async function save() {
    const result = await run(() =>
      unwrap(api.PUT('/api/admin/featured', { body: { productIds: current.map((p) => p.id) } })),
    )
    if (result.ok) {
      setSaved(true)
    }
  }

  return (
    <>
      <h1>Featured products</h1>
      {error && <Alert tone="error">{error}</Alert>}
      {saved && <Alert tone="success">The home page has been updated.</Alert>}
      <ol>
        {current.map((p, index) => (
          <li key={p.id} className={styles.inline}>
            <span>{p.name}</span>
            <Button
              variant="secondary"
              disabled={index === 0}
              onClick={() => move(index, -1)}
              aria-label={`Move ${p.name} up`}
            >
              ↑
            </Button>
            <Button
              variant="secondary"
              disabled={index === current.length - 1}
              onClick={() => move(index, 1)}
              aria-label={`Move ${p.name} down`}
            >
              ↓
            </Button>
            <Button
              variant="secondary"
              onClick={() => change(current.filter((c) => c.id !== p.id))}
            >
              Remove
            </Button>
          </li>
        ))}
      </ol>
      {available.length > 0 && (
        <Select
          label="Add a published product"
          placeholder="Choose…"
          value=""
          options={available.map((p) => ({ value: p.id, label: p.name }))}
          onChange={(e) => {
            const product = available.find((p) => p.id === e.target.value)
            if (product) change([...current, { id: product.id, name: product.name }])
          }}
        />
      )}
      <Button disabled={busy} onClick={save}>
        Save
      </Button>
    </>
  )
}
