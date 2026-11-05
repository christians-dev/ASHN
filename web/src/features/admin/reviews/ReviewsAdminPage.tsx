import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { api, unwrap } from '../../../api/client'
import { useFormatters } from '../../../shared/format/useRegion'
import { Button } from '../../../shared/ui/Button'
import { Alert } from '../../../shared/ui/Feedback'
import { Pagination } from '../../../shared/ui/Pagination'
import { Select } from '../../../shared/ui/Select'
import { Spinner } from '../../../shared/ui/Spinner'
import { StarRating } from '../../reviews/StarRating'
import styles from '../admin.module.css'
import { useAdminAction } from '../adminQueries'

type Visibility = 'visible' | 'hidden'

/** Every review, newest first; hide and unhide (FR-040). Hidden reviews leave the product's average. */
export function ReviewsAdminPage() {
  const [visibility, setVisibility] = useState<Visibility>()
  const [page, setPage] = useState(1)
  const format = useFormatters()
  const { run, error, busy } = useAdminAction()
  const hidden = visibility === undefined ? undefined : visibility === 'hidden'
  const reviews = useQuery({
    queryKey: ['admin', 'reviews', visibility ?? 'all', page],
    queryFn: () => unwrap(api.GET('/api/admin/reviews', { params: { query: { hidden, page } } })),
  })

  const setHidden = (id: string, hide: boolean) =>
    run(() =>
      unwrap(
        hide
          ? api.POST('/api/admin/reviews/{id}/hide', { params: { path: { id } } })
          : api.POST('/api/admin/reviews/{id}/unhide', { params: { path: { id } } }),
      ),
    )

  return (
    <>
      <h1>Reviews</h1>
      <Select
        label="Show"
        placeholder="All reviews"
        options={[
          { value: 'visible', label: 'Visible' },
          { value: 'hidden', label: 'Hidden' },
        ]}
        value={visibility ?? ''}
        onChange={(e) => {
          setVisibility((e.target.value || undefined) as Visibility | undefined)
          setPage(1)
        }}
      />
      {error && <Alert tone="error">{error}</Alert>}

      {reviews.isPending ? (
        <Spinner />
      ) : reviews.isError ? (
        <Alert tone="error">Reviews couldn’t be loaded.</Alert>
      ) : (
        <>
          <table className={styles.table}>
            <thead>
              <tr>
                <th scope="col">Product</th>
                <th scope="col">Rating</th>
                <th scope="col">Review</th>
                <th scope="col">Author</th>
                <th scope="col">Date</th>
                <th scope="col" />
              </tr>
            </thead>
            <tbody>
              {reviews.data.items.map((r) => (
                <tr key={r.id}>
                  <td>
                    {r.productSlug ? (
                      <Link to={`/products/${r.productSlug}`}>{r.productName}</Link>
                    ) : (
                      r.productName
                    )}
                  </td>
                  <td>
                    <StarRating average={r.rating} />
                  </td>
                  <td>{r.text}</td>
                  <td>{r.author}</td>
                  <td>{format.date(r.createdAt)}</td>
                  <td>
                    <Button
                      variant="secondary"
                      disabled={busy}
                      onClick={() => setHidden(r.id, !r.isHidden)}
                    >
                      {r.isHidden ? 'Unhide' : 'Hide'}
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {reviews.data.items.length === 0 && <p>No reviews.</p>}
          <Pagination
            page={reviews.data.page}
            pageSize={reviews.data.pageSize}
            totalCount={reviews.data.totalCount}
            onPageChange={setPage}
          />
        </>
      )}
    </>
  )
}
