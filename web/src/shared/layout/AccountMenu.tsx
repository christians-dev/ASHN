import { Link, NavLink, useLocation, useNavigate } from 'react-router'
import { api, unwrap } from '../../api/client'
import { useRefreshSession } from '../auth/useRefreshSession'
import { isAdmin, useSession } from '../auth/useSession'
import { Button } from '../ui/Button'
import styles from './layout.module.css'

export function AccountMenu() {
  const { data: session } = useSession()
  const location = useLocation()
  const navigate = useNavigate()
  const refreshSession = useRefreshSession()

  async function signOut() {
    await unwrap(api.POST('/api/auth/logout'))
    navigate('/') // before the refresh, which would bounce a signed-in-only page to sign-in
    await refreshSession()
  }

  if (!session?.isAuthenticated) {
    const returnUrl = encodeURIComponent(location.pathname + location.search)
    return <Link to={`/sign-in?returnUrl=${returnUrl}`}>Sign in</Link>
  }

  return (
    <ul className={styles.nav}>
      {isAdmin(session) && (
        <li>
          <NavLink to="/admin">Admin</NavLink>
        </li>
      )}
      <li>
        <NavLink to="/account">{session.displayName ?? 'Account'}</NavLink>
      </li>
      <li>
        <Button variant="secondary" onClick={signOut}>
          Sign out
        </Button>
      </li>
    </ul>
  )
}
