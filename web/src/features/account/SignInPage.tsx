import { type FormEvent, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { api, unwrap } from '../../api/client'
import { formMessageFrom, safeReturnUrl } from '../../shared/auth/formErrors'
import { useRefreshSession } from '../../shared/auth/useRefreshSession'
import { Button } from '../../shared/ui/Button'
import { Alert } from '../../shared/ui/Feedback'
import { TextField } from '../../shared/ui/TextField'

const googleErrors: Record<string, string> = {
  unverified: 'Your Google email address isn’t verified, so we can’t use it to sign you in.',
  locked: 'This account is temporarily locked. Please try again in 15 minutes.',
  external: 'Google sign-in didn’t complete. Please try again.',
}

export function SignInPage() {
  const [params] = useSearchParams()
  const returnUrl = safeReturnUrl(params.get('returnUrl'))
  const navigate = useNavigate()
  const refreshSession = useRefreshSession()
  const [errors, setErrors] = useState<{ email?: string; password?: string }>({})
  const [message, setMessage] = useState(googleErrors[params.get('error') ?? ''])
  const [busy, setBusy] = useState(false)

  async function signIn(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const email = String(form.get('email') ?? '').trim()
    const password = String(form.get('password') ?? '')

    const missing = {
      email: email ? undefined : 'Enter your email address.',
      password: password ? undefined : 'Enter your password.',
    }
    setErrors(missing)
    if (missing.email || missing.password) return

    setBusy(true)
    try {
      await unwrap(api.POST('/api/auth/login', { body: { email, password } }))
      await refreshSession()
      navigate(returnUrl, { replace: true })
    } catch (error) {
      setMessage(formMessageFrom(error))
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <h1>Sign in</h1>
      {message && <Alert tone="error">{message}</Alert>}
      <form onSubmit={signIn} noValidate>
        <TextField
          label="Email"
          name="email"
          type="email"
          autoComplete="email"
          error={errors.email}
        />
        <TextField
          label="Password"
          name="password"
          type="password"
          autoComplete="current-password"
          error={errors.password}
        />
        <Button type="submit" disabled={busy}>
          Sign in
        </Button>
      </form>
      <p>
        <a href={`/api/auth/google?returnUrl=${encodeURIComponent(returnUrl)}`}>
          Continue with Google
        </a>
      </p>
      <p>
        <Link to="/forgot-password">Forgot your password?</Link> · New here?{' '}
        <Link to={`/register?returnUrl=${encodeURIComponent(returnUrl)}`}>Create an account</Link>
      </p>
    </>
  )
}
