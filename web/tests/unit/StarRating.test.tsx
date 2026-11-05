import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { StarRating } from '../../src/features/reviews/StarRating'

describe('StarRating', () => {
  it('rounds to whole stars and gives screen readers the exact average', () => {
    render(<StarRating average={4.3} count={12} />)

    const rating = screen.getByRole('img', { name: 'Rated 4.3 out of 5 from 12 reviews' })
    expect(rating).toHaveTextContent('★★★★☆')
    expect(rating).toHaveTextContent('4.3 (12)')
  })

  it('says "review" for a single one', () => {
    render(<StarRating average={5} count={1} />)

    expect(
      screen.getByRole('img', { name: 'Rated 5.0 out of 5 from 1 review' }),
    ).toBeInTheDocument()
  })

  it('shows a single review’s stars without a count', () => {
    render(<StarRating average={2} />)

    expect(screen.getByRole('img', { name: '2 out of 5 stars' })).toHaveTextContent('★★☆☆☆')
  })
})
