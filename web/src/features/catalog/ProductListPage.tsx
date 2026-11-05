import type { FormEvent } from 'react'
import { useParams, useSearchParams } from 'react-router'
import { Button } from '../../shared/ui/Button'
import { Alert, EmptyState } from '../../shared/ui/Feedback'
import { Pagination } from '../../shared/ui/Pagination'
import { Select } from '../../shared/ui/Select'
import { Spinner } from '../../shared/ui/Spinner'
import { TextField } from '../../shared/ui/TextField'
import { type BrowseFilters, useCategories, useProducts } from './catalogQueries'
import { ProductGrid } from './components/ProductCard'
import styles from './components/catalog.module.css'

const sortOptions = [
  { value: 'newest', label: 'Newest' },
  { value: 'price_asc', label: 'Price: low to high' },
  { value: 'price_desc', label: 'Price: high to low' },
  { value: 'rating', label: 'Best rated' },
]

const filterKeys = ['size', 'colour', 'minPrice', 'maxPrice', 'sort'] as const

/** A category (/c/:categorySlug) or search results (/search?q=). Filters live in the URL so results can be shared. */
export function ProductListPage() {
  const { categorySlug } = useParams()
  const [params, setParams] = useSearchParams()
  const numberParam = (key: string) => (params.get(key) ? Number(params.get(key)) : undefined)

  const filters: BrowseFilters = {
    category: categorySlug,
    q: params.get('q') ?? undefined,
    size: params.get('size') ?? undefined,
    colour: params.get('colour') ?? undefined,
    minPrice: numberParam('minPrice'),
    maxPrice: numberParam('maxPrice'),
    sort: params.get('sort') ?? undefined,
    page: numberParam('page'),
  }

  const products = useProducts(filters)
  const categoryName = useCategories().data?.find((c) => c.slug === categorySlug)?.name
  const title = categorySlug
    ? (categoryName ?? 'Products')
    : `Search results for “${filters.q ?? ''}”`

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const next = new URLSearchParams()
    if (filters.q) next.set('q', filters.q)
    for (const key of filterKeys) {
      const value = String(form.get(key) ?? '').trim()
      if (value) next.set(key, value)
    }
    setParams(next) // a new filter starts again at page 1
  }

  function goToPage(page: number) {
    const next = new URLSearchParams(params)
    next.set('page', String(page))
    setParams(next)
  }

  return (
    <>
      <h1>{title}</h1>
      <form className={styles.filters} onSubmit={applyFilters} aria-label="Filter products">
        <TextField label="Size" name="size" defaultValue={filters.size} />
        <TextField label="Colour" name="colour" defaultValue={filters.colour} />
        <TextField
          label="Min price"
          name="minPrice"
          type="number"
          min={0}
          defaultValue={filters.minPrice}
        />
        <TextField
          label="Max price"
          name="maxPrice"
          type="number"
          min={0}
          defaultValue={filters.maxPrice}
        />
        <Select
          label="Sort by"
          name="sort"
          options={sortOptions}
          defaultValue={filters.sort ?? 'newest'}
        />
        <div>
          <Button type="submit">Apply</Button>
        </div>
      </form>

      {products.isPending ? (
        <Spinner />
      ) : products.isError ? (
        <Alert tone="error">Products couldn’t be loaded. Please try again.</Alert>
      ) : products.data.items.length === 0 ? (
        <EmptyState title="No products found">
          <p>Try removing a filter.</p>
        </EmptyState>
      ) : (
        <>
          <ProductGrid products={products.data.items} />
          <Pagination
            page={products.data.page}
            pageSize={products.data.pageSize}
            totalCount={products.data.totalCount}
            onPageChange={goToPage}
          />
        </>
      )}
    </>
  )
}
