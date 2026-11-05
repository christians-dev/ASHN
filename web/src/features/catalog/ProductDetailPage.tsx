import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError } from '../../api/client'
import { useFormatters } from '../../shared/format/useRegion'
import { Alert, EmptyState } from '../../shared/ui/Feedback'
import { Spinner } from '../../shared/ui/Spinner'
import { type Variant, useProduct } from './catalogQueries'
import { AddToCartButton } from '../cart/AddToCartButton'
import { ProductReviews } from '../reviews/ProductReviews'
import { StarRating } from '../reviews/StarRating'
import { WishlistButton } from '../wishlist/WishlistButton'
import { VariantSelector } from './components/VariantSelector'
import styles from './components/catalog.module.css'

export function ProductDetailPage() {
  const { slug = '' } = useParams()
  const product = useProduct(slug)
  const format = useFormatters()
  const [variant, setVariant] = useState<Variant | null>(null)

  if (product.isPending) return <Spinner />
  if (product.error instanceof ApiError && product.error.status === 404) {
    return (
      <EmptyState title="This product isn’t available">
        <p>
          It may have been removed. <Link to="/">Back to the store</Link>
        </p>
      </EmptyState>
    )
  }
  if (product.isError) return <Alert tone="error">This product couldn’t be loaded.</Alert>

  const { data } = product
  const lowestPrice = data.variants.reduce<Variant['price'] | null>(
    (lowest, v) =>
      lowest === null || (v.price.amount ?? 0) < (lowest.amount ?? 0) ? v.price : lowest,
    null,
  )
  const nothingInStock = data.variants.every((v) => !v.isAvailable)

  return (
    <div className={styles.detail}>
      <div className={styles.gallery}>
        {data.images.map((image) => (
          <img key={image.url} src={image.url} alt={image.altText} className={styles.cardImage} />
        ))}
      </div>
      <div>
        <p>
          <Link to={`/c/${data.categorySlug}`}>{data.categoryName}</Link>
        </p>
        <h1>{data.name}</h1>
        {data.rating && (
          <p>
            <a href="#reviews-heading">
              <StarRating average={data.rating.average} count={data.rating.count} />
            </a>
          </p>
        )}
        <p aria-live="polite">
          {variant
            ? format.money(variant.price)
            : lowestPrice && `From ${format.money(lowestPrice)}`}
        </p>
        <p>{data.description}</p>
        {nothingInStock ? (
          <Alert>Sold out.</Alert>
        ) : (
          <VariantSelector options={data.options} variants={data.variants} onChange={setVariant} />
        )}
        <AddToCartButton variant={variant} />{' '}
        <WishlistButton productId={data.id} productName={data.name} />
        <ProductReviews productId={data.id} />
      </div>
    </div>
  )
}
