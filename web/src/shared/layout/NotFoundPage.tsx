import { Link } from 'react-router'
import { EmptyState } from '../ui/Feedback'

export function NotFoundPage() {
  return (
    <EmptyState title="Page not found">
      <p>
        That page doesn’t exist or isn’t available. <Link to="/">Back to the store</Link>
      </p>
    </EmptyState>
  )
}
