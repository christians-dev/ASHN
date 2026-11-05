import { Button } from './Button'
import styles from './ui.module.css'

interface PaginationProps {
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
}

export function Pagination({ page, pageSize, totalCount, onPageChange }: PaginationProps) {
  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize))
  if (pageCount === 1) return null

  return (
    <nav aria-label="Pagination" className={styles.pagination}>
      <Button variant="secondary" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
        Previous
      </Button>
      <span aria-current="page">
        Page {page} of {pageCount}
      </span>
      <Button
        variant="secondary"
        disabled={page >= pageCount}
        onClick={() => onPageChange(page + 1)}
      >
        Next
      </Button>
    </nav>
  )
}
