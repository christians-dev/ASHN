import { Navigate, Outlet, useLocation } from 'react-router'
import { Spinner } from '../ui/Spinner'
import { isAdmin, useSession } from './useSession'
import { NotFoundPage } from '../layout/NotFoundPage'

function SignInRedirect() {
  const location = useLocation()
  const returnUrl = encodeURIComponent(location.pathname + location.search)
  return <Navigate to={`/sign-in?returnUrl=${returnUrl}`} replace />
}

/** Renders child routes only for signed-in shoppers; others go to sign-in and come back. */
export function RequireAuth() {
  const { data: session, isPending } = useSession()
  if (isPending) return <Spinner />
  return session?.isAuthenticated ? <Outlet /> : <SignInRedirect />
}

/** Admin pages. The API enforces the Admin role regardless; this only keeps the UI tidy. */
export function RequireAdmin() {
  const { data: session, isPending } = useSession()
  if (isPending) return <Spinner />
  if (!session?.isAuthenticated) return <SignInRedirect />
  return isAdmin(session) ? <Outlet /> : <NotFoundPage />
}
