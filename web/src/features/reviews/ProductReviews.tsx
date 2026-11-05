import { useQuery, useQueryClient } from '@tanstack/react-query'
import { type FormEvent, useState } from 'react'
import { api, unwrap, type Schemas } from '../../api/client'
import { type FieldErrors, fieldErrorsFrom, formMessageFrom } from '../../shared/auth/formErrors'
import { useFormatters } from '../../shared/format/useRegion'
import { Button } from '../../shared/ui/Button'
import { Alert } from '../../shared/ui/Feedback'
import { Spinner } from '../../shared/ui/Spinner'
import { StarInput, StarRating } from './StarRating'
import styles from './reviews.module.css'

type Review = Schemas['ReviewDto']

const MaxLength = 2000

/**
 * A product's reviews with its average (FR-040). Customers who received the product can write, edit or delete
 * their own review (FR-039); everyone else just reads.
 */
export function ProductReviews({ productId }: { productId: string }) {
  const queryKey = ['reviews', productId]
  const queryClient = useQueryClient()
  const reviews = useQuery({
    queryKey,
    queryFn: () =>
      unwrap(api.GET('/api/products/{productId}/reviews', { params: { path: { productId } } })),
  })
  const [editing, setEditing] = useState(false)

  async function refresh() {
    setEditing(false)
    // The product's own rating (detail page and lists) changes too.
    await queryClient.invalidateQueries({ queryKey })
    await queryClient.invalidateQueries({ queryKey: ['products'] })
    await queryClient.invalidateQueries({ queryKey: ['product'] })
    await queryClient.invalidateQueries({ queryKey: ['home'] })
  }

  if (reviews.isPending) return <Spinner label="Loading reviews…" />
  if (reviews.isError) return <Alert tone="error">Reviews couldn’t be loaded.</Alert>

  const { summary, mine, mineIsHidden, canReview } = reviews.data
  const showForm = canReview && (editing || !mine)

  return (
    <section className={styles.section} aria-labelledby="reviews-heading">
      <h2 id="reviews-heading">Reviews</h2>
      {summary ? (
        <p>
          <StarRating average={summary.average} count={summary.count} />
        </p>
      ) : (
        <p>No reviews yet.</p>
      )}

      {mine && !editing && (
        <div>
          <h3>Your review</h3>
          {mineIsHidden && (
            <Alert>Your review has been hidden by the store and isn’t shown to others.</Alert>
          )}
          <ReviewItem review={mine} />
          {canReview && (
            <Button variant="secondary" onClick={() => setEditing(true)}>
              Edit your review
            </Button>
          )}
        </div>
      )}

      {showForm && (
        <ReviewForm
          productId={productId}
          existing={mine}
          onDone={refresh}
          onCancel={mine ? () => setEditing(false) : undefined}
        />
      )}

      <ul className={styles.list}>
        {reviews.data.reviews.map((review) => (
          <li key={review.id}>
            <ReviewItem review={review} />
          </li>
        ))}
      </ul>
    </section>
  )
}

function ReviewItem({ review }: { review: Review }) {
  const format = useFormatters()
  return (
    <article>
      <StarRating average={review.rating} /> <strong>{review.author}</strong> ·{' '}
      <time dateTime={review.createdAt}>{format.date(review.createdAt)}</time>
      {review.text && <p>{review.text}</p>}
    </article>
  )
}

function ReviewForm({
  productId,
  existing,
  onDone,
  onCancel,
}: {
  productId: string
  existing: Review | null
  onDone: () => Promise<void>
  onCancel?: () => void
}) {
  const [rating, setRating] = useState(existing?.rating ?? 0)
  const [text, setText] = useState(existing?.text ?? '')
  const [errors, setErrors] = useState<FieldErrors>({})
  const [message, setMessage] = useState<string>()
  const [busy, setBusy] = useState(false)
  const path = { params: { path: { productId } } }

  async function act(action: () => Promise<unknown>) {
    setBusy(true)
    setErrors({})
    setMessage(undefined)
    try {
      await action()
      await onDone()
    } catch (e) {
      setErrors(fieldErrorsFrom(e))
      setMessage(formMessageFrom(e))
    } finally {
      setBusy(false)
    }
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (rating === 0) {
      setErrors({ rating: 'Choose 1 to 5 stars.' })
      return
    }
    act(() =>
      unwrap(
        api.PUT('/api/products/{productId}/reviews/mine', { ...path, body: { rating, text } }),
      ),
    )
  }

  return (
    <form onSubmit={submit} noValidate className={styles.form} aria-label="Your review">
      <h3>{existing ? 'Edit your review' : 'Write a review'}</h3>
      {message && Object.keys(errors).length === 0 && <Alert tone="error">{message}</Alert>}
      <StarInput
        value={rating}
        onChange={(n) => {
          setRating(n)
          setErrors(({ rating: _cleared, ...rest }) => rest)
        }}
        error={errors.rating}
      />
      <label htmlFor="review-text">Your review (optional)</label>
      <textarea
        id="review-text"
        value={text}
        maxLength={MaxLength}
        onChange={(e) => setText(e.target.value)}
        aria-describedby="review-text-count"
      />
      <p id="review-text-count" className={styles.count}>
        {text.length} / {MaxLength.toLocaleString('en-US')}
        {errors.text && <span className={styles.error}> {errors.text}</span>}
      </p>
      <Button type="submit" disabled={busy}>
        {existing ? 'Save review' : 'Post review'}
      </Button>{' '}
      {existing && (
        <Button
          variant="secondary"
          disabled={busy}
          onClick={() =>
            act(() => unwrap(api.DELETE('/api/products/{productId}/reviews/mine', path)))
          }
        >
          Delete review
        </Button>
      )}{' '}
      {onCancel && (
        <Button variant="secondary" disabled={busy} onClick={onCancel}>
          Cancel
        </Button>
      )}
    </form>
  )
}
