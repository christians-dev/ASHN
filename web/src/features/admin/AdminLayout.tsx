import { NavLink, Outlet } from 'react-router'
import styles from '../../shared/layout/layout.module.css'

const sections = [
  { to: 'products', label: 'Products' },
  { to: 'categories', label: 'Categories' },
  { to: 'featured', label: 'Featured' },
  { to: 'orders', label: 'Orders' },
  { to: 'coupons', label: 'Coupons' },
  { to: 'tax-and-shipping', label: 'Tax & Shipping' },
  { to: 'reviews', label: 'Reviews' },
  { to: 'admins', label: 'Admins' },
]

export function AdminLayout() {
  return (
    <div className={styles.adminShell}>
      <nav aria-label="Admin" className={styles.adminNav}>
        <h2>Admin</h2>
        <ul>
          {sections.map((section) => (
            <li key={section.to}>
              <NavLink to={section.to}>{section.label}</NavLink>
            </li>
          ))}
        </ul>
      </nav>
      <section>
        <Outlet />
      </section>
    </div>
  )
}

export function AdminHome() {
  return <h1>Store administration</h1>
}
