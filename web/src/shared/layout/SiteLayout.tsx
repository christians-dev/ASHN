import { Link, NavLink, Outlet } from 'react-router'
import { CartBadge } from '../../features/cart/CartBadge'
import { AccountMenu } from './AccountMenu'
import { SearchBox } from './SearchBox'
import styles from './layout.module.css'

export function SiteLayout() {
  return (
    <>
      <a href="#main" className={styles.skipLink}>
        Skip to content
      </a>
      <header className={styles.header}>
        <Link to="/" className={styles.brand}>
          Ashen
        </Link>
        <nav aria-label="Main">
          <ul className={styles.nav}>
            <li>
              <NavLink to="/">Home</NavLink>
            </li>
          </ul>
        </nav>
        <SearchBox />
        <CartBadge />
        <AccountMenu />
      </header>
      <main id="main" className={styles.main}>
        <Outlet />
      </main>
      <footer className={styles.footer}>© Ashen</footer>
    </>
  )
}
