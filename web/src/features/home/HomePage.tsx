import { Link } from 'react-router'
import { useHome } from '../catalog/catalogQueries'
import { ProductGrid } from '../catalog/components/ProductCard'
import { Alert } from '../../shared/ui/Feedback'
import { Spinner } from '../../shared/ui/Spinner'

/** Featured products chosen by the admin, plus the categories (FR-006). Visual design comes later. */
export function HomePage() {
  const { data, isPending, isError } = useHome()

  if (isPending) return <Spinner />
  if (isError) return <Alert tone="error">The store couldn’t be loaded. Please try again.</Alert>

  return (
    <>
      <h1>Ashen</h1>
      <nav aria-label="Categories">
        <ul>
          {data.categories.map((category) => (
            <li key={category.id}>
              <Link to={`/c/${category.slug}`}>{category.name}</Link>
            </li>
          ))}
        </ul>
      </nav>
      {data.featured.length > 0 && (
        <section aria-labelledby="featured-heading">
          <h2 id="featured-heading">Featured</h2>
          <ProductGrid products={data.featured} />
        </section>
      )}
    </>
  )
}
