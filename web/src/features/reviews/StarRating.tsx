import styles from './reviews.module.css'

/** "★★★★☆ 4.3 (12)": stars rounded to the nearest whole star, with the exact average for screen readers. */
export function StarRating({ average, count }: { average: number; count?: number }) {
  const filled = Math.round(average)
  const label =
    count === undefined
      ? `${average} out of 5 stars`
      : `Rated ${average.toFixed(1)} out of 5 from ${count} ${count === 1 ? 'review' : 'reviews'}`

  return (
    <span className={styles.stars} role="img" aria-label={label}>
      <span aria-hidden="true">
        {'★'.repeat(filled)}
        {'☆'.repeat(5 - filled)}
      </span>
      {count !== undefined && (
        <span aria-hidden="true" className={styles.count}>
          {' '}
          {average.toFixed(1)} ({count})
        </span>
      )}
    </span>
  )
}

/** Five radio buttons, so keyboard and screen-reader users choose a rating like any other option. */
export function StarInput({
  value,
  onChange,
  error,
}: {
  value: number
  onChange: (rating: number) => void
  error?: string
}) {
  return (
    <fieldset className={styles.starInput} aria-invalid={error ? true : undefined}>
      <legend>Your rating</legend>
      {[1, 2, 3, 4, 5].map((n) => (
        <label key={n}>
          <input
            type="radio"
            name="rating"
            value={n}
            checked={value === n}
            onChange={() => onChange(n)}
          />
          <span aria-hidden="true">{n <= value ? '★' : '☆'}</span>
          <span className={styles.visuallyHidden}>
            {n} {n === 1 ? 'star' : 'stars'}
          </span>
        </label>
      ))}
      {error && <p className={styles.error}>{error}</p>}
    </fieldset>
  )
}
