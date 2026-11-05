import { type FormEvent, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { api, unwrap } from '../../api/client'
import {
  type FieldErrors,
  fieldErrorsFrom,
  formMessageFrom,
  safeReturnUrl,
} from '../../shared/auth/formErrors'
import { useRefreshSession } from '../../shared/auth/useRefreshSession'
import { Button } from '../../shared/ui/Button'
import { Alert } from '../../shared/ui/Feedback'
import { TextField } from '../../shared/ui/TextField'

export function RegisterPage() {
  const [params] = useSearchParams()
  const returnUrl = safeReturnUrl(params.get('returnUrl'))
  const navigate = useNavigate()
  const refreshSession = useRefreshSession()
  const [errors, setErrors] = useState<FieldErrors>({})
  const [message, setMessage] = useState<string>()
  const [busy, setBusy] = useState(false)

  async function register(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const displayName = String(form.get('displayName') ?? '').trim()
    const email = String(form.get('email') ?? '').trim()
    const password = String(form.get('password') ?? '')

    // Quick checks here; the server still decides (duplicate email, breached passwords).
    const local: FieldErrors = {
      displayName: displayName ? undefined : 'Enter your name.',
      email: email ? undefined : 'Enter your email address.',
      password: password.length >= 8 ? undefined : 'Use at least 8 characters.',
    }
    setErrors(local)
    setMessage(undefined)
    if (Object.values(local).some(Boolean)) return

    setBusy(true)
    try {
      await unwrap(api.POST('/api/auth/register', { body: { displayName, email, password } }))
      await refreshSession()
      navigate(returnUrl, { replace: true })
    } catch (error) {
      const fields = fieldErrorsFrom(error)
      setErrors(fields)
      if (Object.keys(fields).length === 0) setMessage(formMessageFrom(error))
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <h1>Create an account</h1>
      {message && <Alert tone="error">{message}</Alert>}
      <form onSubmit={register} noValidate>
        <TextField label="Name" name="displayName" autoComplete="name" error={errors.displayName} />
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
          autoComplete="new-password"
          error={errors.password}
        />
        <Button type="submit" disabled={busy}>
          Create account
        </Button>
      </form>
      <p>
        <a href={`/api/auth/google?returnUrl=${encodeURIComponent(returnUrl)}`}>
          Continue with Google
        </a>
      </p>
      <p>
        Already have an account? <Link to="/sign-in">Sign in</Link>
      </p>
    </>
  )
}
