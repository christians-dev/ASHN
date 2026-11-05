import { Link } from 'react-router'
import { useFormatters } from '../../../shared/format/useRegion'
import { StarRating } from '../../reviews/StarRating'
import { WishlistButton } from '../../wishlist/WishlistButton'
import type { ProductSummary } from '../catalogQueries'
import styles from './catalog.module.css'

export function ProductCard({ product }: { product: ProductSummary }) {
  const format = useFormatters()

  return (
    <article className={styles.card}>
      <Link to={`/products/${product.slug}`} className={styles.cardLink}>
        {product.imageUrl ? (
          <img src={product.imageUrl} alt="" className={styles.cardImage} loading="lazy" />
        ) : (
          <div className={styles.cardImage} />
        )}
        <h3 className={styles.cardName}>{product.name}</h3>
      </Link>
      {product.fromPrice && <p>{format.money(product.fromPrice)}</p>}
      {product.ratingAverage != null && (
        <p>
          <StarRating average={product.ratingAverage} count={product.ratingCount} />
        </p>
      )}
      {!product.inStock && <p className={styles.muted}>Sold out</p>}
      <WishlistButton productId={product.productId} productName={product.name} />
    </article>
  )
}

export function ProductGrid({ products }: { products: ProductSummary[] }) {
  return (
    <ul className={styles.grid}>
      {products.map((product) => (
        <li key={product.productId}>
          <ProductCard product={product} />
        </li>
      ))}
    </ul>
  )
}
